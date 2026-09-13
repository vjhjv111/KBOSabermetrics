using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record ComparisonRequest(int Year = 2026, int MinPa = 100, string[]? Codes = null, string TargetCode = "")
{
    public void Validate()
    {
        if (Year is < 1900 or > 2200 || MinPa is < 1 or > 1000)
            throw new RequestError("비교 연도 또는 최소 타석 조건이 잘못되었습니다.");
        if (Codes is { Length: > 4 } || Codes is not null &&
            (Codes.Any(code => !IsCode(code)) || Codes.Distinct(StringComparer.Ordinal).Count() != Codes.Length))
            throw new RequestError("서로 다른 선수 코드를 최대 4명까지 선택하세요.");
        if (TargetCode is null || TargetCode.Length > 0 && !IsCode(TargetCode))
            throw new RequestError("유사 선수 기준 코드가 잘못되었습니다.");
    }

    internal static bool IsCode(string? code) => code is { Length: >= 4 and <= 10 } && code.All(char.IsAsciiDigit);
}

public sealed record ComparisonMetric(string Key, string Label, string Format, bool HigherIsBetter = true);
public sealed record ComparisonPlayer(string Code, string Name, string[] Teams, string? PhotoUrl,
    Dictionary<string, double?> Values, Dictionary<string, double?> Percentiles);
public sealed record SimilarPlayer(string Code, double Distance, Dictionary<string, double?> Components);
public sealed record ComparisonResult(int Year, int MinPa, string? LastGameDate, int CohortSize, string[] Notes,
    ComparisonMetric[] Metrics, ComparisonPlayer[] Players, string[] SelectedCodes, string TargetCode, SimilarPlayer[] Similar);

public sealed class ComparisonWebService(DatabaseCacheService db, SiteOptions options)
{
    private static readonly string[] Totals = ["PA", "AB", "H", "TB", "HR", "BB", "HBP", "SO", "SH", "SF", "SB"];
    private static readonly string[] SimilarityKeys = ["AVG", "ISO", "BBPct", "KPct"];
    private static readonly ComparisonMetric[] Metrics = [
        new("PA", "타석", "integer"), new("AVG", "타율", "rate"), new("OBP", "출루율", "rate"),
        new("SLG", "장타율", "rate"), new("OPS", "OPS", "rate"), new("ISO", "순장타율", "rate"),
        new("HR", "홈런", "integer"), new("BBPct", "볼넷%", "percent"),
        new("KPct", "삼진%", "percent", false), new("SB", "도루", "integer")
    ];
    private const string GamesFilter = """
        g.SeasonYear=$year AND LOWER(TRIM(g.RoundCode))='kbo_r' AND UPPER(g.StatusCode)='RESULT'
        AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE')
        """;

    public async Task<ComparisonResult> QueryAsync(ComparisonRequest request, CancellationToken ct)
    {
        request.Validate();
        ct.ThrowIfCancellationRequested();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = db.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private
        }.ToString());
        await connection.OpenAsync(ct);
        var players = new List<ComparisonPlayer>();
        await using (var command = Command(connection, request, $"""
            SELECT s.Pcode,COALESCE(MAX(NULLIF(s.Name,'')),s.Pcode) Name,GROUP_CONCAT(DISTINCT s.TeamCode) Teams,
                   {string.Join(",", Totals.Select(key => $"SUM(s.{key}) {key}"))}
            FROM BatterGameStats s JOIN Games g ON g.GameId=s.GameId
            WHERE {GamesFilter} AND LENGTH(s.Pcode) BETWEEN 4 AND 10 AND s.Pcode NOT GLOB '*[^0-9]*'
            GROUP BY s.Pcode HAVING SUM(s.PA)>=$minPa ORDER BY PA DESC,s.Pcode LIMIT 501
            """))
        {
            using var cancellation = ct.Register(command.Cancel);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (players.Count == 500)
                    throw new RequestError("비교 대상이 500명을 초과했습니다. 최소 타석을 높여 다시 조회하세요.", 400, "COMPARISON_COHORT_TOO_LARGE");
                var values = new Dictionary<string, double?>(StringComparer.Ordinal);
                for (var i = 0; i < Totals.Length; i++)
                    values[Totals[i]] = reader.IsDBNull(i + 3) ? null : Convert.ToDouble(reader.GetValue(i + 3), CultureInfo.InvariantCulture);
                AddRates(values);
                var teams = reader.IsDBNull(2) ? [] : reader.GetString(2).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray();
                players.Add(new(reader.GetString(0), reader.GetString(1), teams, null, values, new(StringComparer.Ordinal)));
            }
        }
        string? lastGameDate;
        await using (var command = Command(connection, request, $"SELECT SUBSTR(MAX(g.GameDate),1,10) FROM Games g WHERE {GamesFilter}"))
        {
            using var cancellation = ct.Register(command.Cancel);
            lastGameDate = await command.ExecuteScalarAsync(ct) as string;
        }
        AddPercentiles(players, ct);
        var photos = await PhotoUrlsAsync(connection, players.Select(player => player.Code).ToHashSet(StringComparer.Ordinal), ct);
        for (var i = 0; i < players.Count; i++)
            players[i] = players[i] with { PhotoUrl = photos.GetValueOrDefault(players[i].Code) };

        var notes = new List<string>
        {
            "적재된 종료 경기의 정규시즌 타자 기록을 선수별로 합산합니다. 최소 타석을 충족한 선수 전체가 비교군이며, 팀 표시 필터는 퍼센타일과 유사도를 바꾸지 않습니다.",
            "타율·출루율·장타율은 경기별 비율의 평균이 아닌 합산 분자/분모입니다. BB에는 고의4구가 이미 포함되어 한 번만 계산합니다. 분모가 없으면 —로 표시합니다.",
            "퍼센타일은 100×(더 낮은 값의 인원+동률 인원/2)/유효 인원입니다. 삼진%만 방향을 뒤집어 높을수록 우수하게 표시합니다. PA·HR·SB는 누적량의 순위입니다.",
            "유사도는 타율·순장타율·볼넷%·삼진%의 퍼센타일 차이를 같은 비중으로 계산한 RMS 거리입니다. 0에 가까울수록 기록 구성이 비슷하며, 실력·성장·성공 확률의 예측이 아닙니다. 네 지표가 모두 있는 선수만 비교합니다."
        };
        var byCode = players.ToDictionary(player => player.Code, StringComparer.Ordinal);
        var requestedCodes = request.Codes ?? [];
        var selectedCodes = requestedCodes.Where(byCode.ContainsKey).ToArray();
        if (selectedCodes.Length != requestedCodes.Length)
            notes.Add($"선택 선수 {requestedCodes.Length - selectedCodes.Length}명은 해당 시즌에 최소 {request.MinPa}타석을 충족하지 않아 제외했습니다.");
        var targetCode = byCode.ContainsKey(request.TargetCode) ? request.TargetCode : "";
        if (request.TargetCode.Length > 0 && targetCode.Length == 0)
            notes.Add("유사 선수 기준이 비교군에 없어 선택 선수 또는 최다 타석 선수로 변경했습니다.");
        if (targetCode.Length == 0) targetCode = selectedCodes.FirstOrDefault() ?? players.FirstOrDefault()?.Code ?? "";
        if (selectedCodes.Length == 0 && targetCode.Length > 0) selectedCodes = [targetCode];
        var similar = targetCode.Length == 0 ? [] : Similarity(byCode[targetCode], players, ct);
        if (players.Count == 0) notes.Add("이 시즌·최소 타석 조건에 맞는 타자가 없습니다.");
        else if (SimilarityKeys.Any(key => byCode[targetCode].Percentiles.GetValueOrDefault(key) is null))
            notes.Add("기준 선수의 유사도 지표에 분모가 없는 값이 있어 유사 선수 순위를 표시하지 않습니다.");
        return new(request.Year, request.MinPa, lastGameDate, players.Count, notes.ToArray(), Metrics,
            players.ToArray(), selectedCodes, targetCode, similar);
    }

    private SqliteCommand Command(SqliteConnection connection, ComparisonRequest request, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = options.QuerySeconds;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$year", request.Year);
        command.Parameters.AddWithValue("$minPa", request.MinPa);
        return command;
    }

    private static double? Ratio(double? numerator, double? denominator, double scale = 1) =>
        numerator.HasValue && denominator is > 0 ? numerator.Value * scale / denominator.Value : null;

    private static void AddRates(Dictionary<string, double?> values)
    {
        values["AVG"] = Ratio(values["H"], values["AB"]);
        values["OBP"] = Ratio(values["H"] + values["BB"] + values["HBP"], values["AB"] + values["BB"] + values["HBP"] + values["SF"]);
        values["SLG"] = Ratio(values["TB"], values["AB"]);
        values["OPS"] = values["OBP"] + values["SLG"];
        values["ISO"] = Ratio(values["TB"] - values["H"], values["AB"]);
        values["BBPct"] = Ratio(values["BB"], values["PA"], 100);
        values["KPct"] = Ratio(values["SO"], values["PA"], 100);
    }

    private static void AddPercentiles(List<ComparisonPlayer> players, CancellationToken ct)
    {
        foreach (var metric in Metrics)
        {
            ct.ThrowIfCancellationRequested();
            var eligible = players.Where(player => player.Values[metric.Key].HasValue).ToArray();
            var ranks = eligible.GroupBy(player => player.Values[metric.Key]!.Value)
                .OrderBy(group => group.Key).ToArray();
            var below = 0;
            var percentileByValue = new Dictionary<double, double>();
            foreach (var group in ranks)
            {
                var count = group.Count();
                var percentile = 100d * (below + 0.5 * count) / eligible.Length;
                percentileByValue[group.Key] = metric.HigherIsBetter ? percentile : 100 - percentile;
                below += count;
            }
            foreach (var player in players)
                player.Percentiles[metric.Key] = player.Values[metric.Key] is double value ? percentileByValue[value] : null;
        }
    }

    private static SimilarPlayer[] Similarity(ComparisonPlayer target, List<ComparisonPlayer> players, CancellationToken ct)
    {
        if (SimilarityKeys.Any(key => target.Percentiles[key] is null)) return [];
        var result = new List<SimilarPlayer>();
        foreach (var player in players)
        {
            ct.ThrowIfCancellationRequested();
            if (player.Code == target.Code || SimilarityKeys.Any(key => player.Percentiles[key] is null)) continue;
            var components = SimilarityKeys.ToDictionary(key => key,
                key => (double?)Math.Abs(player.Percentiles[key]!.Value - target.Percentiles[key]!.Value), StringComparer.Ordinal);
            var distance = Math.Sqrt(components.Values.Sum(value => value!.Value * value.Value) / SimilarityKeys.Length);
            result.Add(new(player.Code, distance, components));
        }
        return result.OrderBy(player => player.Distance).ThenBy(player => player.Code, StringComparer.Ordinal).Take(10).ToArray();
    }

    private async Task<Dictionary<string, string>> PhotoUrlsAsync(SqliteConnection connection, HashSet<string> codes, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (codes.Count == 0) return result;
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.QuerySeconds;
        using var cancellation = ct.Register(command.Cancel);
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OfficialPlayerProfiles' LIMIT 1";
        if (await command.ExecuteScalarAsync(ct) is null) return result;
        var parameters = codes.Select((code, index) => (code, name: "$code" + index)).ToArray();
        command.CommandText = $"SELECT Pcode,PhotoFileName FROM OfficialPlayerProfiles WHERE Pcode IN ({string.Join(",", parameters.Select(item => item.name))})";
        foreach (var item in parameters) command.Parameters.AddWithValue(item.name, item.code);
        var directory = string.IsNullOrWhiteSpace(options.PlayerPhotoDirectory)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(db.DatabasePath))!, "player-photos")
            : Path.GetFullPath(options.PlayerPhotoDirectory);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (reader.IsDBNull(1)) continue;
            var code = reader.GetString(0); var name = reader.GetString(1);
            if (name != $"{code}.jpg" && name != $"{code}.jpeg" && name != $"{code}.png") continue;
            try
            {
                var info = new FileInfo(Path.Combine(directory, name));
                if (info.Exists && info.Length is >= 8 and <= 5 * 1024 * 1024 && (info.Attributes & FileAttributes.ReparsePoint) == 0)
                    result[code] = $"/api/player-photo/{code}";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }
}
