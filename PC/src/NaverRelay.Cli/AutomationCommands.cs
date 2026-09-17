using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

internal static class AutomationCommands
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = args.Skip(3).ToHashSet(StringComparer.Ordinal);
        var allowedOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "--all-years",
            "--allow-reconciliation-pending",
            "--structural-only"
        };
        if (args.Length < 3 || options.Any(option => !allowedOptions.Contains(option)) || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: --reconcile|--verify <existing-db> <report.json> [--all-years] [--allow-reconciliation-pending] [--structural-only]");
            return 2;
        }
        var failures = new List<string>();
        var messages = new List<string>();
        string? dataVersion = null;
        try
        {
            if (args[0] == "--reconcile")
            {
                var db = new DatabaseCacheService(args[1]);
                await db.InitializeAsync();
                var years = (await db.GetRegularSeasonYearsAsync()).Where(y => y >= 2022);
                // Scheduled collection covers recent games. Match the GUI fallback and
                // avoid re-fetching every historic player's records every scheduled run.
                if (!args.Contains("--all-years")) years = years.Take(1);
                foreach (var year in years)
                {
                    // Same three reconciliation operations as the PC GUI workflow.
                    await Check($"{year} 공식 정정", async () =>
                    {
                        var r = await db.SyncKboCorrectionsAsync(year);
                        messages.AddRange(r.Details);
                        return (r.Pending, r.Message);
                    });
                    await Check($"{year} 공식 타점", async () =>
                    {
                        var r = await db.SyncOfficialRbiAsync(year);
                        return (r.Pending, r.Message);
                    });
                    await Check($"{year} 팀 자책점", async () =>
                    {
                        var r = await db.SyncOfficialTeamPitchingAsync(year);
                        return (r.Pending, r.Message);
                    });
                }
            }
            else
            {
                await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.GetFullPath(args[1]), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                await connection.OpenAsync();
                async Task Read(string sql, Action<SqliteDataReader> consume)
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) consume(reader);
                }
                await Read("PRAGMA integrity_check", r => { if (r.GetString(0) != "ok") failures.Add(r.GetString(0)); });
                await Read("PRAGMA foreign_key_check", r => failures.Add($"외래키 오류: {r.GetValue(0)} / {r.GetValue(1)}"));
                await Read("SELECT COUNT(*) FROM Games", r => messages.Add($"DB 경기 수: {r.GetInt64(0)}"));
                await Read("SELECT COALESCE(SUM(UPPER(COALESCE(StatusCode,''))='RESULT'),0),COALESCE(SUM(UPPER(COALESCE(StatusCode,''))='ENDED'),0) FROM Games",
                    r => messages.Add($"종료 상태 경기: RESULT {r.GetInt64(0)} / ENDED {r.GetInt64(1)}"));
                await Read("SELECT MetaValue FROM Metadata WHERE MetaKey='DataVersion'", r => dataVersion = r.GetString(0));
                if (options.Contains("--structural-only"))
                {
                    messages.Add("진단 레코드 검사는 임시 건너뜀; SQLite 무결성·외래키·DB 버전만 확인했습니다.");
                }
                else
                {
                    var allowReconciliationPending = options.Contains("--allow-reconciliation-pending");
                    var diagnosticsWhere = allowReconciliationPending
                        ? $"Severity={(int)DiagnosticSeverity.Error} AND Code NOT IN ('KBO_CORRECTION_PENDING','KBO_RBI_PENDING','KBO_BOX_PA_DIFFERENCE')"
                        : $"Severity={(int)DiagnosticSeverity.Error} OR Code IN ('KBO_CORRECTION_PENDING','KBO_RBI_PENDING','KBO_BOX_PA_DIFFERENCE')";
                    await Read($"SELECT GameId,Code,Message FROM Diagnostics WHERE {diagnosticsWhere} ORDER BY GameId,Code",
                        r => failures.Add($"{r.GetString(0)} [{r.GetString(1)}] {r.GetString(2)}"));
                }
            }
        }
        catch (Exception ex) { failures.Add(ex.Message); }
        var report = new { checkedAt = DateTimeOffset.UtcNow, command = args[0], database = Path.GetFullPath(args[1]),
            passed = failures.Count == 0, dataVersion, messages, failures };
        var path = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        foreach (var message in messages) Console.WriteLine(message);
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        Console.WriteLine($"{args[0]}: {(report.passed ? "PASS" : "FAIL")} ({path})");
        return report.passed ? 0 : 1;

        async Task Check(string name, Func<Task<(int Pending, string Message)>> action)
        {
            try
            {
                var result = await action();
                messages.Add(result.Message);
                if (result.Pending > 0) failures.Add($"{name}: {result.Pending}건 검토/재시도 필요. {result.Message}");
            }
            catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
        }
    }
}
