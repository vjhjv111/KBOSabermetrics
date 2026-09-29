using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record RecentStarterRow(string Pcode, string Name, int Starts, int PlateAppearances);

// 라인업 최적화(페이즈2)에 쓰는 "타자 vs 투수손" 기록 누적치입니다. Singles는 총타수 기준
// 1루타 수로, TotalBases로 구분합니다(2루타=2, 3루타=3, 홈런=4) — DatabasePlayerPageService.Splits
// 의 SplitAccumulator와 같은 방식입니다.
public sealed class LineupOptimizerBatterCounts
{
    public int PA, AB, H, Singles, Doubles, Triples, HomeRuns, Walks, HitByPitch;

    public void Add(int pa, int ab, int h, int singles, int doubles, int triples, int homeRuns, int walks, int hitByPitch)
    {
        PA += pa; AB += ab; H += h; Singles += singles; Doubles += doubles; Triples += triples;
        HomeRuns += homeRuns; Walks += walks; HitByPitch += hitByPitch;
    }
}

public sealed record LineupOptimizerHandSplits(LineupOptimizerBatterCounts Overall, LineupOptimizerBatterCounts VsLeft, LineupOptimizerBatterCounts VsRight);

// 상대 선발투수 한 명의 "좌타자/우타자 상대 FIP" 성분 누적치(라인업 최적화용). Outs는
// PlateAppearances 레벨 IsOut 집계로 근사한 아웃수라(도루사·주루사 등 타석 밖 아웃은 못 잡음)
// 실제 이닝과 정확히 같지 않지만, 같은 투수의 "전체 대비 좌/우 상대 스플릿" 비율만 쓰는 용도라
// 이 근사 오차는 분자/분모에 동일하게 실려 비율 계산에서 크게 상쇄됩니다.
public sealed class PitcherFipComponents
{
    public int BattersFaced, Outs, HomeRuns, Walks, HitByPitch, Strikeouts;

    public void Add(bool isOut, bool isHomeRun, bool isWalk, bool isHitByPitch, bool isStrikeout)
    {
        BattersFaced++;
        if (isOut) Outs++;
        if (isHomeRun) HomeRuns++;
        if (isWalk) Walks++;
        if (isHitByPitch) HitByPitch++;
        if (isStrikeout) Strikeouts++;
    }

    /// <summary>FanGraphs식 FIP = (13*HR + 3*(BB+HBP) - 2*K) / IP + 리그 FIP 상수. 이닝(Outs/3)이
    /// 0이면(표본 없음) null입니다.</summary>
    public double? Fip(double leagueFipConstant)
    {
        var innings = Outs / 3.0;
        if (innings <= 0) return null;
        return (13.0 * HomeRuns + 3.0 * (Walks + HitByPitch) - 2.0 * Strikeouts) / innings + leagueFipConstant;
    }
}

public sealed record PitcherFipSplits(PitcherFipComponents Overall, PitcherFipComponents VsLeft, PitcherFipComponents VsRight);

// 특정 시즌의 리그 전체 타석 결과 집계(모델 보정계수 계산용). 리그 평균 타자 9명을 가정한 합성
// 타순의 "모델 기대 득점"을 구하는 데 씁니다.
public sealed record SeasonLeagueBattingTotals(int PlateAppearances, int Walks, int HitByPitch,
    int Singles, int Doubles, int Triples, int HomeRuns);

// LineupOptimizerResults 테이블에 저장/조회되는 한 조합(날짜+팀+타자9명+상대손)의 전체 결과.
public sealed record LineupOptimizerStoredResult(
    string[] Algorithm1Order, string[] Algorithm1OrderPcodes, double? Algorithm1ExpectedRuns, double? Algorithm1EstimatedRuns,
    string[] Algorithm2Order, string[] Algorithm2OrderPcodes, double Algorithm2ExpectedRuns, double? Algorithm2EstimatedRuns,
    double? CalibrationFactor);

public partial class DatabaseCacheService
{
    /// <summary>OfficialPlayerProfiles.BatsThrows(예: "우투우타")를 pcode별로 그대로 돌려줍니다.
    /// 이 테이블은 수집기가 선택적으로 채우는 부가 테이블이라 없으면 빈 딕셔너리를 돌려줍니다.</summary>
    public async Task<Dictionary<string, string?>> GetBatsThrowsMapAsync(IReadOnlyCollection<string> pcodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string?>();
        if (pcodes.Count == 0) return result;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        if (!await LineupOptimizerTableExistsAsync(connection, "OfficialPlayerProfiles", ct).ConfigureAwait(false)) return result;

        var list = pcodes.Distinct().ToList();
        const int batchSize = 400;
        for (var offset = 0; offset < list.Count; offset += batchSize)
        {
            var batch = list.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"SELECT Pcode, BatsThrows FROM OfficialPlayerProfiles WHERE Pcode IN ({placeholders});";
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        return result;
    }

    /// <summary>OfficialPlayerProfiles.Position(광범위 카테고리, 예: "내야수"/"외야수")을 pcode별로
    /// 돌려줍니다. 실제 출전 포지션(GetMostRecentSpecificPositionsAsync)을 못 찾았을 때만 쓰는
    /// 최후의 폴백입니다.</summary>
    public async Task<Dictionary<string, string?>> GetOfficialPositionMapAsync(IReadOnlyCollection<string> pcodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string?>();
        if (pcodes.Count == 0) return result;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        if (!await LineupOptimizerTableExistsAsync(connection, "OfficialPlayerProfiles", ct).ConfigureAwait(false)) return result;

        var list = pcodes.Distinct().ToList();
        const int batchSize = 400;
        for (var offset = 0; offset < list.Count; offset += batchSize)
        {
            var batch = list.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"SELECT Pcode, Position FROM OfficialPlayerProfiles WHERE Pcode IN ({placeholders});";
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        return result;
    }

    /// <summary>주어진 pcode들이 "가장 최근 실제로 출전했을 때 라인업 패널에 찍힌 구체적 수비
    /// 포지션"(예: "중견수")을 DailyLineupEntries에서 찾습니다. lookbackDays 안에서 못 찾은
    /// pcode는 결과 딕셔너리에서 아예 빠지므로, 호출자가 GetOfficialPositionMapAsync(광범위
    /// 카테고리)로 폴백하면 됩니다.</summary>
    public async Task<Dictionary<string, string>> GetMostRecentSpecificPositionsAsync(
        IReadOnlyCollection<string> pcodes, string beforeDate, int lookbackDays, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        if (pcodes.Count == 0) return result;
        string? earliest = DateTime.TryParseExact(beforeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.AddDays(-lookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var list = pcodes.Distinct().ToList();
        const int batchSize = 300;
        for (var offset = 0; offset < list.Count; offset += batchSize)
        {
            var batch = list.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"""
                SELECT d.Pcode, d.Position
                FROM DailyLineupEntries d
                WHERE d.Role='batter' AND d.Position IS NOT NULL AND TRIM(d.Position) <> ''
                  AND d.Pcode IN ({placeholders}) AND d.GameDate < $before
                  {(earliest is null ? "" : "AND d.GameDate >= $earliest")}
                  AND d.GameDate = (
                      SELECT MAX(d2.GameDate) FROM DailyLineupEntries d2
                      WHERE d2.Pcode = d.Pcode AND d2.Role='batter'
                        AND d2.Position IS NOT NULL AND TRIM(d2.Position) <> ''
                        AND d2.GameDate < $before
                        {(earliest is null ? "" : "AND d2.GameDate >= $earliest")}
                  );
                """;
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            command.Parameters.AddWithValue("$before", beforeDate);
            if (earliest is not null) command.Parameters.AddWithValue("$earliest", earliest);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result[reader.GetString(0)] = reader.GetString(1);
        }
        return result;
    }

    /// <summary>"우투우타"/"좌투좌타"/"우투좌타"/"양투양타" 같은 BatsThrows 원문에서 투수 던지는 손과
    /// 타자 타석 방향을 각각 파싱합니다("L"/"R"/"S"). 둘 다 없으면 null입니다.</summary>
    public static (string? Throws, string? Stands) ParseBatsThrows(string? batsThrows)
    {
        if (string.IsNullOrWhiteSpace(batsThrows)) return (null, null);
        string? throwsHand = batsThrows.Contains("좌투") ? "L" : batsThrows.Contains("우투") ? "R" : batsThrows.Contains("양투") ? "S" : null;
        string? standSide = batsThrows.Contains("좌타") ? "L" : batsThrows.Contains("우타") ? "R" : batsThrows.Contains("양타") ? "S" : null;
        return (throwsHand, standSide);
    }

    /// <summary>타자별 통산 정규시즌(kbo_r) 기록을 상대 투수의 던지는 손으로 나눠 돌려줍니다.
    /// VsLeft/VsRight는 PlateAppearances를 상대 투수 pcode로 OfficialPlayerProfiles와 조인해서
    /// 구합니다. OfficialPlayerProfiles가 없거나 특정 투수의 BatsThrows를 모르면 그 타석은 어느
    /// 쪽에도 집계되지 않습니다(표본 부족으로 자연스럽게 Overall로 폴백하도록 호출자가 처리).
    /// Overall은 BatterGameStats 기반의 더 가벼운 집계로, OfficialPlayerProfiles 유무와 무관하게
    /// 항상 채워집니다.</summary>
    public async Task<Dictionary<string, LineupOptimizerHandSplits>> GetLineupOptimizerHandSplitsAsync(
        IReadOnlyCollection<string> batterPcodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, LineupOptimizerHandSplits>();
        if (batterPcodes.Count == 0) return result;
        var pcodes = batterPcodes.Distinct().ToList();
        foreach (var pcode in pcodes) result[pcode] = new LineupOptimizerHandSplits(new(), new(), new());

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        const int batchSize = 300;

        for (var offset = 0; offset < pcodes.Count; offset += batchSize)
        {
            var batch = pcodes.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"""
                SELECT b.Pcode, SUM(b.PA), SUM(b.AB), SUM(b.H), SUM(b.Singles), SUM(b.Doubles), SUM(b.Triples),
                       SUM(b.HR), SUM(b.BB), SUM(b.HBP)
                FROM BatterGameStats b JOIN Games g ON g.GameId = b.GameId
                WHERE b.Pcode IN ({placeholders}) AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
                GROUP BY b.Pcode;
                """;
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                result[pcode].Overall.Add(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9));
            }
        }

        if (!await LineupOptimizerTableExistsAsync(connection, "OfficialPlayerProfiles", ct).ConfigureAwait(false)) return result;

        for (var offset = 0; offset < pcodes.Count; offset += batchSize)
        {
            var batch = pcodes.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"""
                SELECT pa.BatterPcode, op.BatsThrows, pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
                       pa.IsWalk, pa.IsIntentionalWalk
                FROM PlateAppearances pa
                JOIN Games g ON g.GameId = pa.GameId
                LEFT JOIN OfficialPlayerProfiles op ON op.Pcode = pa.PitcherPcode
                WHERE pa.IsOfficial = 1 AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
                  AND pa.BatterPcode IN ({placeholders});
                """;
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                var (throwsHand, _) = ParseBatsThrows(reader.IsDBNull(1) ? null : reader.GetString(1));
                if (throwsHand is not ("L" or "R")) continue;

                var resultType = reader.GetInt32(2);
                var countsAsAtBat = reader.GetInt32(3) != 0;
                var isHit = reader.GetInt32(4) != 0;
                var totalBases = reader.GetInt32(5);
                var isWalk = reader.GetInt32(6) != 0 || reader.GetInt32(7) != 0;
                var isHbp = resultType == (int)BattingResultType.HitByPitch;
                var singles = isHit && totalBases == 1 ? 1 : 0;
                var doubles = totalBases == 2 ? 1 : 0;
                var triples = totalBases == 3 ? 1 : 0;
                var hr = totalBases == 4 ? 1 : 0;

                var target = throwsHand == "L" ? result[pcode].VsLeft : result[pcode].VsRight;
                target.Add(1, countsAsAtBat ? 1 : 0, isHit ? 1 : 0, singles, doubles, triples, hr, isWalk ? 1 : 0, isHbp ? 1 : 0);
            }
        }
        return result;
    }

    /// <summary>KBO 공식 "전체 등록 현황"(RegisterAll, DailyLineupWorker.ApplyEntryRosterAsync가
    /// 매 주기 저장) 기준으로, 그 팀에 등록된 타자 전원을 돌려줍니다. onOrBeforeDate 당일 스냅샷이
    /// 있으면 그걸 쓰고, 없으면(아직 그날 주기가 안 돌았거나 실패한 경우) lookbackDays 안에서 가장
    /// 최근 스냅샷 날짜 하나를 통째로 씁니다(여러 날짜를 섞지 않습니다 — 그사이 말소/콜업된 선수가
    /// 뒤섞이는 걸 피하기 위함).</summary>
    public async Task<List<(string Pcode, string Name)>> GetLatestEntryRosterBattersAsync(
        string teamCode, string onOrBeforeDate, int lookbackDays, CancellationToken ct = default)
    {
        var result = new List<(string, string)>();
        if (!DateOnly.TryParse(onOrBeforeDate, CultureInfo.InvariantCulture, out var before)) return result;
        var since = before.AddDays(-lookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Pcode, PlayerName
            FROM DailyEntryRosters
            WHERE TeamCode = $team AND Role = 'batter'
              AND GameDate = (
                  SELECT MAX(GameDate) FROM DailyEntryRosters
                  WHERE TeamCode = $team AND Role = 'batter' AND GameDate <= $before AND GameDate >= $since
              );
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        command.Parameters.AddWithValue("$before", onOrBeforeDate);
        command.Parameters.AddWithValue("$since", since);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    /// <summary>그 팀이 그 시즌 들어 onOrBeforeDate까지(포함) 치른 정규시즌(kbo_r) 경기 수입니다.
    /// KBO의 "규정타석" 기준(팀 경기수 × 3.1)을 시즌 도중 시점 기준으로 근사 계산하는 데 씁니다 —
    /// 공식 규정타석도 시즌 내내 고정값이 아니라 팀이 치른 경기 수에 비례해 매일 올라갑니다.</summary>
    public async Task<int> GetTeamGamesPlayedAsync(string teamCode, string onOrBeforeDate, CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(onOrBeforeDate, CultureInfo.InvariantCulture, out var before)) return 0;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT g.GameId)
            FROM Games g
            WHERE (g.HomeTeamCode = $team OR g.AwayTeamCode = $team)
              AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
              AND g.SeasonYear = $year
              AND g.GameDate <= $before;
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        command.Parameters.AddWithValue("$year", before.Year);
        command.Parameters.AddWithValue("$before", onOrBeforeDate);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is long l ? (int)l : result is int i ? i : 0;
    }

    /// <summary>GetLineupOptimizerHandSplitsAsync와 같은 구조지만, 통산 전체가 아니라 [sinceDate,
    /// beforeDate) 구간(기본 최근 30일)만 집계합니다 — "최근 전적"을 상대손 스플릿으로 반영하는 데
    /// 씁니다.</summary>
    public async Task<Dictionary<string, LineupOptimizerHandSplits>> GetLineupOptimizerRecentHandSplitsAsync(
        IReadOnlyCollection<string> batterPcodes, string sinceDate, string beforeDate, CancellationToken ct = default)
    {
        var result = new Dictionary<string, LineupOptimizerHandSplits>();
        if (batterPcodes.Count == 0) return result;
        var pcodes = batterPcodes.Distinct().ToList();
        foreach (var pcode in pcodes) result[pcode] = new LineupOptimizerHandSplits(new(), new(), new());

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        const int batchSize = 300;

        for (var offset = 0; offset < pcodes.Count; offset += batchSize)
        {
            var batch = pcodes.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"""
                SELECT b.Pcode, SUM(b.PA), SUM(b.AB), SUM(b.H), SUM(b.Singles), SUM(b.Doubles), SUM(b.Triples),
                       SUM(b.HR), SUM(b.BB), SUM(b.HBP)
                FROM BatterGameStats b JOIN Games g ON g.GameId = b.GameId
                WHERE b.Pcode IN ({placeholders}) AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
                  AND g.GameDate >= $since AND g.GameDate < $before
                GROUP BY b.Pcode;
                """;
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            command.Parameters.AddWithValue("$since", sinceDate);
            command.Parameters.AddWithValue("$before", beforeDate);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                result[pcode].Overall.Add(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9));
            }
        }

        if (!await LineupOptimizerTableExistsAsync(connection, "OfficialPlayerProfiles", ct).ConfigureAwait(false)) return result;

        for (var offset = 0; offset < pcodes.Count; offset += batchSize)
        {
            var batch = pcodes.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"""
                SELECT pa.BatterPcode, op.BatsThrows, pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
                       pa.IsWalk, pa.IsIntentionalWalk
                FROM PlateAppearances pa
                JOIN Games g ON g.GameId = pa.GameId
                LEFT JOIN OfficialPlayerProfiles op ON op.Pcode = pa.PitcherPcode
                WHERE pa.IsOfficial = 1 AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
                  AND g.GameDate >= $since AND g.GameDate < $before
                  AND pa.BatterPcode IN ({placeholders});
                """;
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            command.Parameters.AddWithValue("$since", sinceDate);
            command.Parameters.AddWithValue("$before", beforeDate);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                var (throwsHand, _) = ParseBatsThrows(reader.IsDBNull(1) ? null : reader.GetString(1));
                if (throwsHand is not ("L" or "R")) continue;

                var resultType = reader.GetInt32(2);
                var countsAsAtBat = reader.GetInt32(3) != 0;
                var isHit = reader.GetInt32(4) != 0;
                var totalBases = reader.GetInt32(5);
                var isWalk = reader.GetInt32(6) != 0 || reader.GetInt32(7) != 0;
                var isHbp = resultType == (int)BattingResultType.HitByPitch;
                var singles = isHit && totalBases == 1 ? 1 : 0;
                var doubles = totalBases == 2 ? 1 : 0;
                var triples = totalBases == 3 ? 1 : 0;
                var hr = totalBases == 4 ? 1 : 0;

                var target = throwsHand == "L" ? result[pcode].VsLeft : result[pcode].VsRight;
                target.Add(1, countsAsAtBat ? 1 : 0, isHit ? 1 : 0, singles, doubles, triples, hr, isWalk ? 1 : 0, isHbp ? 1 : 0);
            }
        }
        return result;
    }

    /// <summary>특정 투수 한 명의 통산 정규시즌(kbo_r) FIP 성분을, 상대한 타자의 타석 방향(좌/우)
    /// 으로 나눠 집계합니다. 라인업 최적화에서 "이 투수가 좌타자/우타자 상대로 얼마나 잘 던지는지"의
    /// 스플릿을 반영하는 데 씁니다. OfficialPlayerProfiles가 없거나 특정 타자의 BatsThrows를 모르면
    /// 그 타석은 Overall에만 집계되고 좌/우 어느 쪽에도 들어가지 않습니다.</summary>
    public async Task<PitcherFipSplits> GetPitcherFipSplitAsync(string pitcherPcode, CancellationToken ct = default)
    {
        var overall = new PitcherFipComponents();
        var vsLeft = new PitcherFipComponents();
        var vsRight = new PitcherFipComponents();

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var hasProfiles = await LineupOptimizerTableExistsAsync(connection, "OfficialPlayerProfiles", ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = hasProfiles ? """
            SELECT pa.ResultType, pa.IsHit, pa.TotalBases, pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout, pa.IsOut,
                   op.BatsThrows
            FROM PlateAppearances pa
            JOIN Games g ON g.GameId = pa.GameId
            LEFT JOIN OfficialPlayerProfiles op ON op.Pcode = pa.BatterPcode
            WHERE pa.IsOfficial = 1 AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
              AND pa.PitcherPcode = $pitcher;
            """ : """
            SELECT pa.ResultType, pa.IsHit, pa.TotalBases, pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout, pa.IsOut,
                   NULL AS BatsThrows
            FROM PlateAppearances pa
            JOIN Games g ON g.GameId = pa.GameId
            WHERE pa.IsOfficial = 1 AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
              AND pa.PitcherPcode = $pitcher;
            """;
        command.Parameters.AddWithValue("$pitcher", pitcherPcode);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var resultType = reader.GetInt32(0);
            var isHit = reader.GetInt32(1) != 0;
            var totalBases = reader.GetInt32(2);
            var isWalk = reader.GetInt32(3) != 0 || reader.GetInt32(4) != 0;
            var isStrikeout = reader.GetInt32(5) != 0;
            var isOut = reader.GetInt32(6) != 0;
            var isHbp = resultType == (int)BattingResultType.HitByPitch;
            var isHomeRun = isHit && totalBases == 4;

            overall.Add(isOut, isHomeRun, isWalk, isHbp, isStrikeout);

            var (_, standSide) = ParseBatsThrows(reader.IsDBNull(7) ? null : reader.GetString(7));
            if (standSide == "L") vsLeft.Add(isOut, isHomeRun, isWalk, isHbp, isStrikeout);
            else if (standSide == "R") vsRight.Add(isOut, isHomeRun, isWalk, isHbp, isStrikeout);
        }
        return new PitcherFipSplits(overall, vsLeft, vsRight);
    }

    /// <summary>그 구단의 공식/예고 라인업이 없을 때 쓰는 최후의 폴백입니다. 최근 lookbackDays일
    /// 동안(정규시즌 kbo_r만) 그 팀 소속으로 가장 많은 경기에 출장한 타자 9명을(동률이면 타석 수
    /// 순으로) 돌려줍니다. BatterGameStats에는 "그날 선발" 여부가 따로 없어서(대타/대수비 포함)
    /// 정확한 "선발 라인업"은 아니지만, 공식 라인업이 전혀 없을 때 그 팀이 최근 실제로 자주 쓴
    /// 타자 9명을 근사하는 최선의 대안입니다.</summary>
    public async Task<List<RecentStarterRow>> GetRecentStartersAsync(string teamCode, string beforeDate, int lookbackDays, CancellationToken ct = default)
    {
        var since = DateTime.ParseExact(beforeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(-lookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Pcode, MAX(b.Name) AS Name, COUNT(DISTINCT b.GameId) AS Starts, SUM(b.PA) AS Pa
            FROM BatterGameStats b JOIN Games g ON g.GameId = b.GameId
            WHERE b.TeamCode = $team AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r'
              AND g.GameDate >= $since AND g.GameDate < $before AND b.PA > 0
            GROUP BY b.Pcode
            ORDER BY Starts DESC, Pa DESC
            LIMIT 9;
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        command.Parameters.AddWithValue("$since", since);
        command.Parameters.AddWithValue("$before", beforeDate);
        var rows = new List<RecentStarterRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        return rows;
    }

    /// <summary>특정 시즌(kbo_r)의 리그 전체 타석 결과 합계입니다. 모델 보정계수를 구하려고 "리그
    /// 평균 타자 9명"으로 이뤄진 합성 타순을 만들 때 씁니다.</summary>
    public async Task<SeasonLeagueBattingTotals> GetSeasonLeagueBattingTotalsAsync(int seasonYear, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(b.PA),0), COALESCE(SUM(b.BB),0), COALESCE(SUM(b.HBP),0),
                   COALESCE(SUM(b.Singles),0), COALESCE(SUM(b.Doubles),0), COALESCE(SUM(b.Triples),0),
                   COALESCE(SUM(b.HR),0)
            FROM BatterGameStats b JOIN Games g ON g.GameId = b.GameId
            WHERE g.SeasonYear = $year AND LOWER(TRIM(COALESCE(g.RoundCode,''))) = 'kbo_r';
            """;
        command.Parameters.AddWithValue("$year", seasonYear);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return new SeasonLeagueBattingTotals(0, 0, 0, 0, 0, 0, 0);
        return new SeasonLeagueBattingTotals(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6));
    }

    /// <summary>특정 시즌(kbo_r) 정규시즌의 실제 "팀당 경기당 득점" 평균입니다(완료된 경기만,
    /// (홈팀 득점 합 + 원정팀 득점 합) / (2 × 경기 수)). 모델 보정계수의 분자로 씁니다.</summary>
    public async Task<double> GetSeasonActualRunsPerTeamPerGameAsync(int seasonYear, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(HomeScore),0) + COALESCE(SUM(AwayScore),0), COUNT(*)
            FROM Games
            WHERE SeasonYear = $year AND LOWER(TRIM(COALESCE(RoundCode,''))) = 'kbo_r'
              AND UPPER(COALESCE(StatusCode,'')) IN ('RESULT','ENDED')
              AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$year", seasonYear);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return 0.0;
        var totalRuns = reader.GetInt32(0);
        var games = reader.GetInt32(1);
        return games > 0 ? totalRuns / (2.0 * games) : 0.0;
    }

    /// <summary>이미 계산해 저장해 둔 라인업 최적화 결과가 있으면 돌려줍니다(없으면 null). 앱이
    /// 재시작돼도 같은 (날짜, 팀, 타자9명, 상대손) 조합은 전수조사를 다시 돌리지 않기 위한
    /// 영구 캐시 조회입니다.</summary>
    public async Task<LineupOptimizerStoredResult?> GetLineupOptimizerResultAsync(string cacheKey, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        if (!await LineupOptimizerTableExistsAsync(connection, "LineupOptimizerResults", ct).ConfigureAwait(false)) return null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Algorithm1OrderJson, Algorithm1ExpectedRuns, Algorithm1EstimatedRuns,
                   Algorithm2OrderJson, Algorithm2ExpectedRuns, Algorithm2EstimatedRuns, CalibrationFactor
            FROM LineupOptimizerResults WHERE CacheKey = $key LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", cacheKey);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;

        // Algorithm2는 저장돼 있어야 이 결과를 "완료됨"으로 취급합니다(핵심 결과).
        if (reader.IsDBNull(3) || reader.IsDBNull(4)) return null;

        var algo1Order = reader.IsDBNull(0) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(reader.GetString(0)) ?? Array.Empty<string>();
        var algo2Order = JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? Array.Empty<string>();

        return new LineupOptimizerStoredResult(
            Algorithm1Order: Array.Empty<string>(), Algorithm1OrderPcodes: algo1Order,
            Algorithm1ExpectedRuns: reader.IsDBNull(1) ? null : reader.GetDouble(1),
            Algorithm1EstimatedRuns: reader.IsDBNull(2) ? null : reader.GetDouble(2),
            Algorithm2Order: Array.Empty<string>(), Algorithm2OrderPcodes: algo2Order,
            Algorithm2ExpectedRuns: reader.GetDouble(4),
            Algorithm2EstimatedRuns: reader.IsDBNull(5) ? null : reader.GetDouble(5),
            CalibrationFactor: reader.IsDBNull(6) ? null : reader.GetDouble(6));
    }

    /// <summary>계산이 끝난 라인업 최적화 결과를 영구 저장합니다(INSERT OR REPLACE — 같은 조합은
    /// 새 계산으로 덮어씁니다).</summary>
    public async Task SaveLineupOptimizerResultAsync(
        string cacheKey, string gameDate, string teamCode, string? opponentCode, string? opponentPitcherHand,
        IReadOnlyList<string> batterPoolPcodes,
        IReadOnlyList<string>? algorithm1OrderPcodes, double? algorithm1ExpectedRuns, double? algorithm1EstimatedRuns,
        IReadOnlyList<string> algorithm2OrderPcodes, double algorithm2ExpectedRuns, double? algorithm2EstimatedRuns,
        double? calibrationFactor, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO LineupOptimizerResults
                (CacheKey, GameDate, TeamCode, OpponentCode, OpponentPitcherHand, BatterPoolPcodesJson,
                 Algorithm1OrderJson, Algorithm1ExpectedRuns, Algorithm1EstimatedRuns,
                 Algorithm2OrderJson, Algorithm2ExpectedRuns, Algorithm2EstimatedRuns, CalibrationFactor, ComputedUtc)
            VALUES
                ($key, $date, $team, $opp, $hand, $pool,
                 $a1order, $a1exp, $a1est,
                 $a2order, $a2exp, $a2est, $calib, $now);
            """;
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$date", gameDate);
        command.Parameters.AddWithValue("$team", teamCode);
        command.Parameters.AddWithValue("$opp", (object?)opponentCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$hand", (object?)opponentPitcherHand ?? DBNull.Value);
        command.Parameters.AddWithValue("$pool", JsonSerializer.Serialize(batterPoolPcodes));
        command.Parameters.AddWithValue("$a1order", algorithm1OrderPcodes is null ? DBNull.Value : JsonSerializer.Serialize(algorithm1OrderPcodes));
        command.Parameters.AddWithValue("$a1exp", (object?)algorithm1ExpectedRuns ?? DBNull.Value);
        command.Parameters.AddWithValue("$a1est", (object?)algorithm1EstimatedRuns ?? DBNull.Value);
        command.Parameters.AddWithValue("$a2order", JsonSerializer.Serialize(algorithm2OrderPcodes));
        command.Parameters.AddWithValue("$a2exp", algorithm2ExpectedRuns);
        command.Parameters.AddWithValue("$a2est", (object?)algorithm2EstimatedRuns ?? DBNull.Value);
        command.Parameters.AddWithValue("$calib", (object?)calibrationFactor ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>오래된(기본 14일 이전) 라인업 최적화 결과 행을 정리합니다. 표가 무한정 커지는 걸
    /// 막는 가벼운 유지보수 작업으로, DailyLineupWorker의 주기 안에서 호출됩니다.</summary>
    public async Task<int> CleanupOldLineupOptimizerResultsAsync(int keepDays = 14, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        if (!await LineupOptimizerTableExistsAsync(connection, "LineupOptimizerResults", ct).ConfigureAwait(false)) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-keepDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LineupOptimizerResults WHERE GameDate < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> LineupOptimizerTableExistsAsync(SqliteConnection connection, string tableName, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }
}
