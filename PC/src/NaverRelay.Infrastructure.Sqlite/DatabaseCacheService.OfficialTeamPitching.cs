using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record OfficialTeamPitchingRow(string TeamCode, string TeamName, int Games,
    int InningsOuts, int Runs, int EarnedRuns, double Era);
public sealed record OfficialTeamPitchingSyncResult(int Teams, int Updated, int Pending, string Message);

public sealed partial class DatabaseCacheService
{
    private const string OfficialTeamPitchingUrl = "https://www.koreabaseball.com/Record/Team/Pitcher/Basic1.aspx";
    private const string OfficialTeamPitchingSchema = """
        CREATE TABLE IF NOT EXISTS OfficialTeamPitchingSources(
            Year INTEGER NOT NULL, Competition TEXT NOT NULL, Json TEXT NOT NULL,
            SourceHtml TEXT NOT NULL, CheckedUtc TEXT NOT NULL, SourceUrl TEXT NOT NULL,
            DatabaseFingerprint TEXT NOT NULL, DatabaseLastDate TEXT NOT NULL,
            PRIMARY KEY(Year,Competition));
        CREATE TABLE IF NOT EXISTS OfficialTeamPitchingAttempts(
            Year INTEGER NOT NULL, Competition TEXT NOT NULL, CheckedUtc TEXT NOT NULL,
            SourceHtml TEXT NOT NULL, Message TEXT NOT NULL, Applied INTEGER NOT NULL,
            PRIMARY KEY(Year,Competition));
        """;

    // The official page has no date selector: a snapshot is valid only for the identical
    // complete season scope. Never distribute a team ER correction among individual pitchers.
    public static IReadOnlyList<OfficialTeamPitchingRow> ParseOfficialTeamPitching(
        string html, int year, string competition = "정규시즌")
    {
        RequireTeamPitchingScope(year, competition);
        var selections = TeamPitchingSelections(html);
        if (selections.SingleOrDefault(s => s.Name.EndsWith("$ddlSeason", StringComparison.Ordinal)).Value != year.ToString(CultureInfo.InvariantCulture) ||
            selections.SingleOrDefault(s => s.Name.EndsWith("$ddlSeries", StringComparison.Ordinal)).Value != "0")
            throw new InvalidDataException("KBO 팀 투수 기록의 선택 연도/대회가 요청과 다릅니다.");

        var result = new List<OfficialTeamPitchingRow>();
        foreach (Match table in Regex.Matches(html, @"<table\b[^>]*>[\s\S]*?</table>", RegexOptions.IgnoreCase))
        {
            var headers = Regex.Matches(table.Value, @"<th\b[^>]*>([\s\S]*?)</th>", RegexOptions.IgnoreCase)
                .Select(m => TeamPitchingText(m.Groups[1].Value)).ToArray();
            if (string.Join(",", headers) != "순위,팀명,ERA,G,W,L,SV,HLD,WPCT,IP,H,HR,BB,HBP,SO,R,ER,WHIP") continue;
            // The footer has a colspan league-total row (G counts games, not team appearances).
            // Parse only the team rows; do not mistake that footer for an eleventh team.
            var body = Regex.Match(table.Value, @"<tbody\b[^>]*>([\s\S]*?)</tbody>", RegexOptions.IgnoreCase);
            if (!body.Success) throw new InvalidDataException("KBO 팀 투수 기록의 본문 표가 없습니다.");
            foreach (Match tr in Regex.Matches(body.Groups[1].Value, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
            {
                var cells = Regex.Matches(tr.Value, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase)
                    .Select(m => TeamPitchingText(m.Groups[1].Value)).ToArray();
                if (cells.Length == 0) continue;
                if (cells.Length != headers.Length) throw new InvalidDataException("KBO 팀 투수 기록의 열 형식이 변경되었습니다.");
                var name = cells[1];
                var code = KboTeams.GetValueOrDefault(name) ?? name switch
                {
                    "넥센" or "히어로즈" or "우리" => "WO", "현대" => "HD", _ => null
                };
                if (code == null || result.Any(r => r.TeamCode == code))
                    throw new InvalidDataException($"KBO 팀 투수 기록의 팀명이 없거나 중복됩니다: {name}");
                int Number(int index) => int.TryParse(cells[index].Replace(",", ""), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : throw new InvalidDataException("KBO 팀 투수 기록 숫자가 올바르지 않습니다.");
                var innings = Regex.Match(cells[9], @"^(\d+)(?:\s+([12])/3)?$");
                if (!innings.Success) throw new InvalidDataException("KBO 팀 투구이닝을 아웃 수로 변환할 수 없습니다.");
                var outs = checked(int.Parse(innings.Groups[1].Value, CultureInfo.InvariantCulture) * 3 +
                    (innings.Groups[2].Success ? int.Parse(innings.Groups[2].Value, CultureInfo.InvariantCulture) : 0));
                var games = Number(3); var runs = Number(15); var earned = Number(16);
                if (!double.TryParse(cells[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var era) ||
                    !double.IsFinite(era) || era < 0 || earned > runs || outs <= 0 || games <= 0 ||
                    Math.Abs(era - earned * 27.0 / outs) > 0.005001 || Number(4) + Number(5) > games)
                    throw new InvalidDataException($"KBO {name} 팀의 경기/이닝/실점/자책점/ERA가 일관되지 않습니다.");
                result.Add(new(code, name, games, outs, runs, earned, era));
            }
        }
        var expected = year >= 2015 ? 10 : year >= 2013 ? 9 : 8;
        if (result.Count != expected || result.Sum(r => r.Games) % 2 != 0)
            throw new InvalidDataException($"KBO 팀 투수 기록 {expected}개 팀 전체를 확인할 수 없습니다 ({result.Count}개).");
        return result;
    }

    public async Task<OfficialTeamPitchingSyncResult> SyncOfficialTeamPitchingAsync(int year,
        IProgress<string>? progress = null, CancellationToken ct = default, HttpClient? transport = null,
        string competition = "정규시즌")
    {
        RequireTeamPitchingScope(year, competition);
        progress?.Report($"KBO {year} {competition} 팀 자책점 대조 중");
        try
        {
            using var owned = transport == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
            var http = transport ?? owned!;
            if (!http.DefaultRequestHeaders.UserAgent.Any()) http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            var html = await FetchOfficialTeamPitchingHtmlAsync(http, year, ct).ConfigureAwait(false);
            var result = await StoreAndReconcileOfficialTeamPitchingAsync(year, html, ct, competition).ConfigureAwait(false);
            progress?.Report(result.Message);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var message = $"KBO {year} 팀 자책점 대조 보류: {ex.Message}. 기존 스냅샷 유지, DB 범위가 일치할 때만 표시합니다.";
            progress?.Report(message);
            return new(0, 0, 1, message);
        }
    }

    public async Task<OfficialTeamPitchingSyncResult> StoreAndReconcileOfficialTeamPitchingAsync(
        int year, string html, CancellationToken ct = default, string competition = "정규시즌")
    {
        var rows = ParseOfficialTeamPitching(html, year, competition);
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = c.BeginTransaction();
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = OfficialTeamPitchingSchema; await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var state = await ReadTeamPitchingScopeAsync(c, tx, year, ct).ConfigureAwait(false);
        var mismatches = TeamPitchingMismatches(rows, state);
        var utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var accepted = mismatches.Count == 0;
        var message = accepted
            ? $"KBO {year} 팀 자책점 {rows.Count}팀 대조 완료 · {state.LastDate}까지 경기/이닝/실점 일치 · 공식 자책점 {rows.Sum(r => r.EarnedRuns):N0} 적용"
            : $"KBO {year} 팀 자책점 적용 보류 · {string.Join("; ", mismatches)}. 기존 스냅샷 유지, 같은 DB 범위에서만 표시합니다.";
        cmd.CommandText = """
            INSERT INTO OfficialTeamPitchingAttempts VALUES($year,$competition,$utc,$html,$message,$applied)
            ON CONFLICT(Year,Competition) DO UPDATE SET CheckedUtc=excluded.CheckedUtc,
                SourceHtml=excluded.SourceHtml,Message=excluded.Message,Applied=excluded.Applied;
            """;
        cmd.Parameters.AddWithValue("$year", year); cmd.Parameters.AddWithValue("$competition", competition);
        cmd.Parameters.AddWithValue("$utc", utc); cmd.Parameters.AddWithValue("$html", html);
        cmd.Parameters.AddWithValue("$message", message); cmd.Parameters.AddWithValue("$applied", accepted ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (accepted)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = """
                INSERT INTO OfficialTeamPitchingSources VALUES($year,$competition,$json,$html,$utc,$url,$fingerprint,$lastDate)
                ON CONFLICT(Year,Competition) DO UPDATE SET Json=excluded.Json,SourceHtml=excluded.SourceHtml,
                    CheckedUtc=excluded.CheckedUtc,SourceUrl=excluded.SourceUrl,
                    DatabaseFingerprint=excluded.DatabaseFingerprint,DatabaseLastDate=excluded.DatabaseLastDate;
                """;
            cmd.Parameters.AddWithValue("$year", year); cmd.Parameters.AddWithValue("$competition", competition);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(rows)); cmd.Parameters.AddWithValue("$html", html);
            cmd.Parameters.AddWithValue("$utc", utc); cmd.Parameters.AddWithValue("$url", OfficialTeamPitchingUrl);
            cmd.Parameters.AddWithValue("$fingerprint", state.Fingerprint); cmd.Parameters.AddWithValue("$lastDate", state.LastDate);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await BumpDataVersionAndInvalidateCachesAsync(c, tx, ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new(rows.Count, accepted ? rows.Count : 0, mismatches.Count, message);
    }

    public async Task<IReadOnlyDictionary<string, OfficialTeamPitchingRow>> GetApplicableOfficialTeamPitchingAsync(
        GameQuery query, CancellationToken ct = default)
    {
        var empty = new Dictionary<string, OfficialTeamPitchingRow>(StringComparer.Ordinal);
        if (!IsOfficialTeamPitchingScope(query)) return empty;
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = c.BeginTransaction(deferred: true);
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='OfficialTeamPitchingSources'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 0) return empty;
        cmd.CommandText = "SELECT Json,DatabaseFingerprint FROM OfficialTeamPitchingSources WHERE Year=$year AND Competition=$competition";
        cmd.Parameters.AddWithValue("$year", query.SeasonYear!.Value); cmd.Parameters.AddWithValue("$competition", query.Competition);
        string json, fingerprint;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return empty;
            json = reader.GetString(0); fingerprint = reader.GetString(1);
        }
        var state = await ReadTeamPitchingScopeAsync(c, tx, query.SeasonYear.Value, ct).ConfigureAwait(false);
        if (fingerprint != state.Fingerprint) return empty;
        var rows = JsonSerializer.Deserialize<List<OfficialTeamPitchingRow>>(json);
        if (rows == null || TeamPitchingMismatches(rows, state).Count != 0) return empty;
        return rows.Where(r => string.IsNullOrWhiteSpace(query.TeamCode) || r.TeamCode == query.TeamCode)
            .ToDictionary(r => r.TeamCode, StringComparer.Ordinal);
    }

    private static bool IsOfficialTeamPitchingScope(GameQuery query) =>
        query.Grouping == AnalyticsGrouping.Team && query.SeasonYear >= 2001 && query.Competition == "정규시즌" &&
        string.IsNullOrWhiteSpace(query.OpponentCode) && string.IsNullOrWhiteSpace(query.Venue) &&
        string.IsNullOrWhiteSpace(query.Stadium) && string.IsNullOrWhiteSpace(query.Weekday) &&
        !query.StartDate.HasValue && !query.EndDate.HasValue && !query.RecentGameCount.HasValue && !query.HasSituationFilters;

    private static void RequireTeamPitchingScope(int year, string competition)
    {
        if (year < 2001 || year > DateTime.UtcNow.AddHours(9).Year || competition != "정규시즌")
            throw new ArgumentException("공식 팀 자책점 동기화는 2001년 이후 정규시즌 전체 기록만 지원합니다.");
    }

    private sealed record TeamPitchingScope(string Fingerprint, string LastDate, bool Valid,
        Dictionary<string, (int Games, int Outs, int Runs)> Teams);

    private static async Task<TeamPitchingScope> ReadTeamPitchingScopeAsync(SqliteConnection c,
        SqliteTransaction tx, int year, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT g.GameId,g.GameDate,t.TeamCode,t.RunsAllowed,
                   COALESCE(SUM(p.InningsOuts),0),COALESCE(SUM(p.RunsAllowed),0),COUNT(p.GameId)
            FROM Games g JOIN (
                SELECT GameId,HomeTeamCode AS TeamCode,AwayScore AS RunsAllowed FROM Games
                UNION ALL SELECT GameId,AwayTeamCode,HomeScore FROM Games
            ) t ON t.GameId=g.GameId
            LEFT JOIN PitcherGameStats p ON p.GameId=g.GameId AND p.TeamCode=t.TeamCode AND p.HasFinalLine=1
            WHERE g.SeasonYear=$year AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY g.GameId,g.GameDate,t.TeamCode,t.RunsAllowed ORDER BY g.GameId,t.TeamCode;
            """;
        cmd.Parameters.AddWithValue("$year", year);
        var parts = new StringBuilder(); var teams = new Dictionary<string, (int Games, int Outs, int Runs)>(StringComparer.Ordinal);
        var lastDate = ""; var valid = true;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = reader.GetString(0); var date = NullableString(reader, 1) ?? "";
            var team = NullableString(reader, 2) ?? ""; var score = NullableInt(reader, 3);
            var outs = ReadInt32(reader, 4); var runs = ReadInt32(reader, 5); var count = ReadInt32(reader, 6);
            valid &= DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                date.StartsWith(year + "-", StringComparison.Ordinal) && team.Length > 0 && score.HasValue &&
                score == runs && count > 0 && outs > 0 && date.CompareTo(DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd")) <= 0;
            if (string.CompareOrdinal(date, lastDate) > 0) lastDate = date;
            var previous = teams.GetValueOrDefault(team);
            teams[team] = (previous.Games + 1, previous.Outs + outs, previous.Runs + runs);
            parts.Append(id).Append('|').Append(date).Append('|').Append(team).Append('|')
                .Append(score).Append('|').Append(outs).Append('|').Append(runs).Append('\n');
        }
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString()))), lastDate,
            valid && teams.Count > 0, teams);
    }

    private static List<string> TeamPitchingMismatches(IReadOnlyList<OfficialTeamPitchingRow> rows, TeamPitchingScope state)
    {
        var result = new List<string>();
        if (!state.Valid) result.Add("DB 경기별 날짜/최종 점수/투수 기록이 완전하지 않음");
        if (rows.Count != state.Teams.Count || rows.Select(r => r.TeamCode).Distinct().Count() != rows.Count)
            result.Add("공식/DB 팀 수 또는 팀 식별 불일치");
        foreach (var row in rows)
        {
            if (!state.Teams.TryGetValue(row.TeamCode, out var db)) { result.Add($"{row.TeamName}: DB 팀 기록 없음"); continue; }
            if (db.Games != row.Games || db.Outs != row.InningsOuts || db.Runs != row.Runs)
                result.Add($"{row.TeamName}: 경기 {db.Games}/{row.Games}, 아웃 {db.Outs}/{row.InningsOuts}, 실점 {db.Runs}/{row.Runs} (DB/공식)");
        }
        return result;
    }

    private static string TeamPitchingText(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();
    private static string TeamPitchingAttribute(string html, string name) => WebUtility.HtmlDecode(
        Regex.Match(html, @"\b" + Regex.Escape(name) + "\\s*=\\s*([\"'])(.*?)\\1", RegexOptions.IgnoreCase).Groups[2].Value);

    private static List<(string Name, string Value, string Html)> TeamPitchingSelections(string html)
    {
        var result = new List<(string, string, string)>();
        foreach (Match select in Regex.Matches(html, @"<select\b[^>]*>[\s\S]*?</select>", RegexOptions.IgnoreCase))
        {
            var options = Regex.Matches(select.Value, @"<option\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Value).ToList();
            var selected = options.FirstOrDefault(s => Regex.IsMatch(s, @"\bselected(?:\s|=|>)", RegexOptions.IgnoreCase)) ?? options.FirstOrDefault() ?? "";
            result.Add((TeamPitchingAttribute(select.Value, "name"), TeamPitchingAttribute(selected, "value"), select.Value));
        }
        return result;
    }

    private static async Task<string> FetchOfficialTeamPitchingHtmlAsync(HttpClient http, int year, CancellationToken ct)
    {
        var html = await http.GetStringAsync(OfficialTeamPitchingUrl, ct).ConfigureAwait(false);
        var selections = TeamPitchingSelections(html);
        var season = selections.SingleOrDefault(s => s.Name.EndsWith("$ddlSeason", StringComparison.Ordinal));
        var series = selections.SingleOrDefault(s => s.Name.EndsWith("$ddlSeries", StringComparison.Ordinal));
        if (string.IsNullOrEmpty(season.Name) || string.IsNullOrEmpty(series.Name))
            throw new InvalidDataException("KBO 팀 기록 연도/대회 선택 항목이 없습니다.");
        if (season.Value == year.ToString(CultureInfo.InvariantCulture) && series.Value == "0") return html;
        if (!Regex.Matches(season.Html, @"<option\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()
            .Any(m => TeamPitchingAttribute(m.Value, "value") == year.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidDataException($"KBO 팀 기록에 {year}시즌 선택 항목이 없습니다.");
        var form = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(html, @"<input\b[^>]*>", RegexOptions.IgnoreCase))
            if (TeamPitchingAttribute(input.Value, "type").Equals("hidden", StringComparison.OrdinalIgnoreCase))
            {
                var name = TeamPitchingAttribute(input.Value, "name");
                if (name.Length > 0) form[name] = TeamPitchingAttribute(input.Value, "value");
            }
        foreach (var select in selections) if (select.Name.Length > 0) form[select.Name] = select.Value;
        if (!form.ContainsKey("__VIEWSTATE")) throw new InvalidDataException("KBO 팀 기록 폼 상태가 없습니다.");
        form[season.Name] = year.ToString(CultureInfo.InvariantCulture); form[series.Name] = "0";
        form["__EVENTTARGET"] = season.Value != form[season.Name] ? season.Name : series.Name;
        form["__EVENTARGUMENT"] = "";
        using var response = await http.PostAsync(OfficialTeamPitchingUrl, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
