using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record OfficialDailyBatting(string Date, string Opponent, int PA, int AB, int H, int HR, int BB, int HBP, int SO, int RBI);
public sealed record OfficialRbiSyncResult(int Players, int Updated, int Pending, string Message)
{
    public IReadOnlyList<string> PendingPlayerCodes { get; init; } = Array.Empty<string>();
}

public sealed partial class DatabaseCacheService
{
    public async Task<IReadOnlyList<int>> GetRegularSeasonYearsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT SeasonYear FROM Games WHERE RoundCode='kbo_r' AND SeasonYear>=2022 ORDER BY SeasonYear DESC";
        var years = new List<int>(); await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) years.Add(reader.GetInt32(0)); return years;
    }
    private const string OfficialRbiSchema = """
        CREATE TABLE IF NOT EXISTS OfficialDailyBattingSources(
            Year INTEGER NOT NULL, Pcode TEXT NOT NULL, Json TEXT NOT NULL, CheckedUtc TEXT NOT NULL,
            SourceUrl TEXT NOT NULL, PRIMARY KEY(Year,Pcode));
        """;

    // A daily row cannot identify two games on the same day. Those rows are deliberately not applied.
    public static IReadOnlyList<OfficialDailyBatting> ParseOfficialDailyBatting(string html, int year)
    {
        var heading = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");
        if (!heading.Contains($"{year}년 일자별 성적")) throw new InvalidDataException("KBO 일자별 기록의 연도를 확인할 수 없습니다.");
        var result = new List<OfficialDailyBatting>();
        foreach (Match table in Regex.Matches(html, @"<table\b[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase))
        {
            var headers = Regex.Match(table.Value, @"<thead\b[^>]*>([\s\S]*?)</thead>", RegexOptions.IgnoreCase).Value;
            var names = Regex.Matches(headers, @"<th\b[^>]*>([\s\S]*?)</th>", RegexOptions.IgnoreCase)
                .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim()).ToArray();
            if (names.Length != 18 || names[1] != "상대" || string.Join(",", names.Skip(3).Take(14)) != "PA,AB,R,H,2B,3B,HR,RBI,SB,CS,BB,HBP,SO,GDP") continue;
            foreach (Match row in Regex.Matches(table.Value, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
            {
                var cells = Regex.Matches(row.Value, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase)
                    .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim()).ToArray();
                if (cells.Length == 0) continue;
                if (cells.Length != 18 || !DateOnly.TryParseExact($"{year}.{cells[0]}", "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new InvalidDataException("KBO 일자별 기록 표 형식이 변경되었습니다.");
                int N(int i) => int.TryParse(cells[i], NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : throw new InvalidDataException("KBO 일자별 기록에 잘못된 숫자가 있습니다.");
                if (!KboTeams.TryGetValue(cells[1], out var opponent)) throw new InvalidDataException("KBO 상대팀을 식별할 수 없습니다.");
                var item = new OfficialDailyBatting(date.ToString("yyyy-MM-dd"), opponent, N(3), N(4), N(6), N(9), N(13), N(14), N(15), N(10));
                if (item.H > item.AB || item.AB > item.PA || item.HR > item.H || item.SO > item.AB || item.BB + item.HBP > item.PA || item.RBI > item.PA * 4)
                    throw new InvalidDataException("KBO 일자별 기록 수치가 일관되지 않습니다.");
                result.Add(item);
            }
        }
        if (result.Count == 0) throw new InvalidDataException("KBO 일자별 기록에서 검증 가능한 행을 찾지 못했습니다.");
        return result;
    }

    private static string OfficialDailyUrl(string pcode) => "https://www.koreabaseball.com/Record/Player/HitterDetail/Daily.aspx?playerId=" + Uri.EscapeDataString(pcode);

    private static async Task<string> FetchDailyHtml(HttpClient http, string pcode, int year, CancellationToken ct)
    {
        var url = OfficialDailyUrl(pcode);
        var html = await http.GetStringAsync(url, ct);
        if (Regex.Replace(html, "<[^>]+>", " ").Contains($"{year}년 일자별 성적")) return html;
        // The site defaults to the current year. Use its form state for historical seasons.
        var form = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(html, @"<input\b[^>]*>", RegexOptions.IgnoreCase))
        {
            string Attr(string key) => WebUtility.HtmlDecode(Regex.Match(input.Value, key + "=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value);
            if (Attr("type") == "hidden" && Attr("name") != "") form[Attr("name")] = Attr("value");
        }
        string? yearField = null;
        foreach (Match select in Regex.Matches(html, "<select[^>]*name=\"([^\"]+)\"[^>]*>([\\s\\S]*?)</select>", RegexOptions.IgnoreCase))
        {
            var name = WebUtility.HtmlDecode(select.Groups[1].Value);
            var options = Regex.Matches(select.Groups[2].Value, @"<option\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value).ToArray();
            var option = options.FirstOrDefault(s => s.Contains("selected", StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault() ?? "";
            form[name] = WebUtility.HtmlDecode(Regex.Match(option, "value=\"([^\"]*)\"").Groups[1].Value);
            if (name.EndsWith("$ddlYear")) { yearField = name; form[name] = year.ToString(CultureInfo.InvariantCulture); }
            if (name.EndsWith("$ddlSeries")) form[name] = "0";
        }
        if (yearField == null || !form.ContainsKey("__VIEWSTATE")) throw new InvalidDataException("KBO 연도 선택 형식이 변경되었습니다.");
        form["__EVENTTARGET"] = yearField; form["__EVENTARGUMENT"] = "";
        using var response = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static bool SameBattingLine(OfficialDailyBatting r, int pa, int ab, int h, int hr, int bb, int hbp, int so) =>
        r.PA == pa && r.AB == ab && r.H == h && r.HR == hr && r.BB == bb && r.HBP == hbp && r.SO == so;

    public async Task<OfficialRbiSyncResult> SyncOfficialRbiAsync(int year, IProgress<string>? progress = null,
        CancellationToken ct = default, HttpClient? transport = null, IReadOnlyCollection<string>? playerCodes = null)
    {
        var players = new List<string>();
        await using (var c = await OpenAsync(ct))
        {
            await using var cmd = c.CreateCommand(); cmd.CommandText = OfficialRbiSchema; await cmd.ExecuteNonQueryAsync(ct);
            cmd.CommandText = "SELECT DISTINCT b.Pcode FROM BatterGameStats b JOIN Games g USING(GameId) WHERE g.SeasonYear=$year AND g.RoundCode='kbo_r' AND b.PA>0 ORDER BY b.Pcode";
            cmd.Parameters.AddWithValue("$year", year);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) if (Regex.IsMatch(r.GetString(0), @"^\d+$") && (playerCodes == null || playerCodes.Contains(r.GetString(0)))) players.Add(r.GetString(0));
        }
        using var ownedHttp = transport == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        var http = transport ?? ownedHttp!;
        if (!http.DefaultRequestHeaders.UserAgent.Any()) http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        int updated = 0, pending = 0, completed = 0, consecutiveErrors = 0, attempted = 0;
        var pendingPlayerCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pcode in players)
        {
            ct.ThrowIfCancellationRequested();
            attempted++;
            progress?.Report($"KBO 타점 대조 {attempted}/{players.Count} (선수 {pcode})");
            try
            {
                // One request per player per collection. An updated final record can change without a new game.
                var rows = ParseOfficialDailyBatting(await FetchDailyHtml(http, pcode, year, ct), year);
                var changes = await StoreAndReconcileOfficialRbiAsync(year, pcode, rows, ct);
                updated += changes.Updated; pending += changes.Pending; completed++; consecutiveErrors = 0;
                if (changes.Pending > 0)
                {
                    pendingPlayerCodes.Add(pcode);
                    await using var connection = await OpenAsync(ct);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT d.GameId,d.Message FROM Diagnostics d JOIN Games g USING(GameId) WHERE g.SeasonYear=$year AND d.Code='KBO_RBI_PENDING' AND d.Message LIKE $prefix ORDER BY d.GameId";
                    command.Parameters.AddWithValue("$year", year); command.Parameters.AddWithValue("$prefix", pcode+":%");
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    while(await reader.ReadAsync(ct)) progress?.Report($"KBO 타점 보류 {reader.GetString(0)}: {reader.GetString(1)}");
                }
                await Task.Delay(100, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                pending++; consecutiveErrors++; pendingPlayerCodes.Add(pcode);
                progress?.Report($"KBO 타점 대조 보류 ({pcode}): {ex.Message}. 기존 기록 유지.");
                if (consecutiveErrors >= 3)
                {
                    foreach (var remaining in players.Skip(attempted)) pendingPlayerCodes.Add(remaining);
                    pending += players.Count - attempted;
                    break;
                }
            }
        }
        return new(completed, updated, pending, $"KBO 타점 {completed}/{players.Count}선수 대조 · {updated}경기 선수 기록 보정 · {pending}건 확인 필요")
        { PendingPlayerCodes = pendingPlayerCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray() };
    }

    public async Task<(int Updated, int Pending)> StoreAndReconcileOfficialRbiAsync(int year, string pcode,
        IReadOnlyList<OfficialDailyBatting> rows, CancellationToken ct = default, DateTimeOffset? sourceTime = null)
    {
        var checkedAt = sourceTime ?? DateTimeOffset.UtcNow;
        if (!Regex.IsMatch(pcode, @"^\d+$") || rows.Count == 0 || rows.Any(r => !r.Date.StartsWith(year + "-"))) throw new InvalidDataException("공식 타점 자료 식별 오류");
        await using var c = await OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = OfficialRbiSchema + OfficialBoxSourceSchema; await cmd.ExecuteNonQueryAsync(ct);
        cmd.CommandText = "INSERT INTO OfficialDailyBattingSources VALUES($year,$pcode,$json,$utc,$url) ON CONFLICT(Year,Pcode) DO UPDATE SET Json=excluded.Json,CheckedUtc=excluded.CheckedUtc,SourceUrl=excluded.SourceUrl WHERE julianday(excluded.CheckedUtc)>=julianday(OfficialDailyBattingSources.CheckedUtc)";
        cmd.Parameters.AddWithValue("$year", year); cmd.Parameters.AddWithValue("$pcode", pcode); cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(rows));
        cmd.Parameters.AddWithValue("$utc", checkedAt.UtcDateTime.ToString("O")); cmd.Parameters.AddWithValue("$url", OfficialDailyUrl(pcode));
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.CommitAsync(ct); return (0,0); }
        cmd.Parameters.Clear();
        cmd.CommandText = """
            SELECT b.GameId,g.GameDate,CASE WHEN b.TeamCode=g.HomeTeamCode THEN g.AwayTeamCode ELSE g.HomeTeamCode END,
                   b.PA,b.AB,b.H,b.HR,b.BB,b.HBP,b.SO,b.RBI,
                   (SELECT COUNT(*) FROM Games x WHERE x.GameDate=g.GameDate AND x.RoundCode=g.RoundCode
                     AND (x.HomeTeamCode=b.TeamCode OR x.AwayTeamCode=b.TeamCode)),
                   (SELECT s.DownloadedUtc FROM OfficialBoxScorePlayers s WHERE s.GameId=b.GameId AND s.Pcode=b.Pcode AND s.TeamCode=b.TeamCode AND s.Role='batter' AND json_extract(s.StatsJson,'$.RunsBattedIn') IS NOT NULL)
            FROM BatterGameStats b JOIN Games g USING(GameId) WHERE g.SeasonYear=$year AND g.RoundCode='kbo_r' AND b.Pcode=$pcode
            """;
        cmd.Parameters.AddWithValue("$year", year); cmd.Parameters.AddWithValue("$pcode", pcode);
        var games = new List<(string Id, string Date, string Opponent, int[] Stats, DateTimeOffset? BoxTime)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct))
            games.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), Enumerable.Range(3,9).Select(reader.GetInt32).ToArray(),reader.IsDBNull(12)?null:OfficialSourceTime(reader.GetString(12))));
        int updated = 0, pending = 0;
        foreach (var g in games)
        {
            var matches = rows.Where(r => r.Date == g.Date).ToArray();
            if (g.BoxTime.HasValue && !SourceIsAtLeastAsNew(checkedAt,g.BoxTime)) continue;
            if (matches.Length == 0 && g.Stats[0] == 0) continue; // Unused substitute or runner-only appearance.
            var valid = matches.Length == 1 && g.Stats[8] == 1 && matches[0].Opponent == g.Opponent &&
                SameBattingLine(matches[0],g.Stats[0],g.Stats[1],g.Stats[2],g.Stats[3],g.Stats[4],g.Stats[5],g.Stats[6]);
            if (!valid)
            {
                pending++;
                cmd.Parameters.Clear();
                cmd.CommandText = "DELETE FROM Diagnostics WHERE GameId=$id AND Code='KBO_RBI_PENDING' AND Message LIKE $prefix; INSERT INTO Diagnostics(GameId,Severity,Code,Message) VALUES($id,1,'KBO_RBI_PENDING',$message)";
                cmd.Parameters.AddWithValue("$id",g.Id); cmd.Parameters.AddWithValue("$prefix",pcode+":%");
                var official = string.Join("; ", matches.Select(r=>$"상대 {r.Opponent}, PA/AB/H/HR/BB/HBP/SO={r.PA}/{r.AB}/{r.H}/{r.HR}/{r.BB}/{r.HBP}/{r.SO}"));
                cmd.Parameters.AddWithValue("$message",$"{pcode}: {g.Date} 타점 보류. DB 상대 {g.Opponent}, PA/AB/H/HR/BB/HBP/SO={string.Join("/",g.Stats.Take(7))}, 당일 팀 경기 {g.Stats[8]}; 공식 {matches.Length}행 [{official}]. {OfficialDailyUrl(pcode)}");
                await cmd.ExecuteNonQueryAsync(ct); continue;
            }
            cmd.Parameters.Clear(); cmd.CommandText = "DELETE FROM Diagnostics WHERE GameId=$id AND Code='KBO_RBI_PENDING' AND Message LIKE $prefix";
            cmd.Parameters.AddWithValue("$id",g.Id); cmd.Parameters.AddWithValue("$prefix",pcode+":%"); await cmd.ExecuteNonQueryAsync(ct);
            var rbi = matches[0].RBI;
            if (rbi == g.Stats[7]) continue;
            cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$id",g.Id); cmd.Parameters.AddWithValue("$pcode",pcode); cmd.Parameters.AddWithValue("$rbi",rbi);
            cmd.CommandText = "UPDATE BatterGameStats SET RBI=$rbi WHERE GameId=$id AND Pcode=$pcode; UPDATE BattingGameLines SET RunsBattedIn=$rbi WHERE GameId=$id AND Pcode=$pcode;";
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO Diagnostics(GameId,Severity,Code,Message) VALUES($id,0,'KBO_RBI_APPLIED',$message)";
            cmd.Parameters.AddWithValue("$id",g.Id); cmd.Parameters.AddWithValue("$message",$"{pcode}: 타점 {g.Stats[7]}→{rbi}, 공식 일자별 기록 대조 {OfficialDailyUrl(pcode)}");
            await cmd.ExecuteNonQueryAsync(ct); updated++;
        }
        if (updated > 0) await BumpDataVersionAndInvalidateCachesAsync(c, tx, ct);
        await tx.CommitAsync(ct);
        return (updated, pending);
    }

    private async Task ApplyStoredOfficialRbiAsync(NormalizedGame game, CancellationToken ct)
    {
        if (!game.IsRegularSeason) return;
        await using var c = await OpenAsync(ct); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='OfficialDailyBattingSources'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0) return;
        // Do not match a daily total to one half of a doubleheader, even if the player appeared in only one game.
        cmd.CommandText = "SELECT COUNT(*) FROM Games WHERE GameDate=$date AND RoundCode='kbo_r' AND GameId<>$id AND (HomeTeamCode=$home OR AwayTeamCode=$home OR HomeTeamCode=$away OR AwayTeamCode=$away)";
        cmd.Parameters.AddWithValue("$date",game.GameDate ?? ""); cmd.Parameters.AddWithValue("$id",game.GameId);
        cmd.Parameters.AddWithValue("$home",game.HomeTeam.TeamCode ?? ""); cmd.Parameters.AddWithValue("$away",game.AwayTeam.TeamCode ?? "");
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 0) return;
        foreach (var b in game.BattingLines)
        {
            cmd.Parameters.Clear(); cmd.CommandText = "SELECT Json,CheckedUtc FROM OfficialDailyBattingSources WHERE Year=$year AND Pcode=$pcode";
            cmd.Parameters.AddWithValue("$year",game.SeasonYear ?? 0); cmd.Parameters.AddWithValue("$pcode",b.Pcode ?? "");
            string json; DateTimeOffset? checkedAt;
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) continue;
                json=r.GetString(0); checkedAt=OfficialSourceTime(r.GetString(1));
            }
            if (b.OfficialStats?.RunsBattedIn.HasValue == true && (!b.OfficialStats.SourceTime.HasValue || !SourceIsAtLeastAsNew(checkedAt,b.OfficialStats.SourceTime))) continue;
            var rows = JsonSerializer.Deserialize<List<OfficialDailyBatting>>(json)!.Where(r => r.Date == game.GameDate).ToArray();
            var pa = game.PlateAppearances.Where(p => p.IsOfficialPlateAppearance && p.BatterPcode == b.Pcode && p.BattingTeamCode == b.TeamCode).ToArray();
            var opponent = b.TeamCode == game.HomeTeam.TeamCode ? game.AwayTeam.TeamCode : game.HomeTeam.TeamCode;
            int Final(int? supplied, int? final, int eventCount) => supplied.HasValue ? final ?? eventCount : eventCount;
            var box = b.OfficialStats;
            if (rows.Length != 1 || rows[0].Opponent != opponent || !SameBattingLine(rows[0],
                Final(box?.PlateAppearances,b.PlateAppearances,pa.Length),
                Final(box?.AtBats,b.AtBats,pa.Count(p=>p.Outcome.CountsAsAtBat)),
                Final(box?.Hits,b.Hits,pa.Count(p=>p.Outcome.IsHit)),
                Final(box?.HomeRuns,b.HomeRuns,pa.Count(p=>p.Outcome.ResultType==BattingResultType.HomeRun)),
                Final(box?.Walks,b.Walks,pa.Count(p=>p.Outcome.IsWalk)),
                Final(box?.HitByPitch,b.HitByPitch,pa.Count(p=>p.Outcome.ResultType==BattingResultType.HitByPitch)),
                Final(box?.Strikeouts,b.Strikeouts,pa.Count(p=>p.Outcome.IsStrikeout)))) continue;
            if (b.RunsBattedIn != rows[0].RBI)
            {
                game.Diagnostics.Add(new ParserDiagnostic { GameId=game.GameId,Severity=DiagnosticSeverity.Info,Code="KBO_RBI_APPLIED",Message=$"{b.Name}: 타점 {b.RunsBattedIn}→{rows[0].RBI}, 저장된 공식 일자별 기록 적용" });
                b.RunsBattedIn = rows[0].RBI;
            }
        }
    }
}
