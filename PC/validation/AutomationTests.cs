using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

internal static class AutomationTests
{
    public static async Task Run(string cli, string fixture, string output)
    {
        output = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var input = Path.Combine(output, "input with spaces");
        Directory.CreateDirectory(input);
        var db = Path.Combine(output, "test.db");
        var report = Path.Combine(output, "verify.json");
        var source = Path.Combine(input, Path.GetFileName(fixture));
        var finalJson = await File.ReadAllTextAsync(fixture);
        var live = JsonNode.Parse(finalJson)!;
        live["collectionStatus"] = "partial";
        live["naver"]!["result"]!["game"]!["statusCode"] = "PLAY";
        await File.WriteAllTextAsync(source, live.ToJsonString());
        var result = await Invoke("--import", input, db);
        Check(result.Code == 0 && result.Text.Contains("imported=0 deferred=1 failed=0"), "live snapshot deferred without failing pipeline");
        var ended = JsonNode.Parse(finalJson)!;
        ended["naver"]!["result"]!["game"]!["statusCode"] = "ENDED";
        await File.WriteAllTextAsync(source, ended.ToJsonString());
        result = await Invoke("--import", input, db);
        Check(result.Code == 0 && result.Text.Contains("imported=1 deferred=0 failed=0"), "ENDED snapshot replaces deferred input and imports");
        result = await Invoke("--verify", db, report);
        Check(result.Code == 0 && JsonNode.Parse(await File.ReadAllTextAsync(report))!["passed"]!.GetValue<bool>(), "official combined fixture passes DB validation");
        var version = JsonNode.Parse(await File.ReadAllTextAsync(report))!["dataVersion"]!.GetValue<string>();
        result = await Invoke("--import", input, db);
        Check(result.Code == 0 && result.Text.Contains("imported=0 deferred=0 failed=0"), "same file is not reimported");
        await Invoke("--verify", db, report);
        Check(JsonNode.Parse(await File.ReadAllTextAsync(report))!["dataVersion"]!.GetValue<string>() == version, "repeat import preserves data version");
        await File.WriteAllTextAsync(Path.Combine(input, "broken.json"), "{broken");
        result = await Invoke("--import", input, db);
        Check(result.Code == 1 && result.Text.Contains("failed=1"), "invalid JSON returns failure instead of permitting publication");
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        await c.OpenAsync();
        await using var command = c.CreateCommand();
        command.CommandText = "INSERT INTO Diagnostics(GameId,Severity,Code,Message) SELECT GameId,2,'TEST_ERROR','test failure' FROM Games LIMIT 1";
        await command.ExecuteNonQueryAsync();
        result = await Invoke("--verify", db, report);
        Check(result.Code == 1 && result.Text.Contains("TEST_ERROR"), "saved diagnostic errors block publication");
        command.CommandText = "DELETE FROM Diagnostics WHERE Code='TEST_ERROR'; INSERT INTO Diagnostics(GameId,Severity,Code,Message) SELECT GameId,1,'KBO_BOX_PA_DIFFERENCE','test pending' FROM Games LIMIT 1";
        await command.ExecuteNonQueryAsync();
        result = await Invoke("--verify", db, report);
        Check(result.Code == 1 && result.Text.Contains("KBO_BOX_PA_DIFFERENCE"), "official box/PA mismatch blocks publication");

        async Task<(int Code, string Text)> Invoke(params string[] args)
        {
            var info = new ProcessStartInfo(Path.GetFullPath(cli)) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var p = Process.Start(info)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, await stdout + await stderr);
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL " + message);
            Console.WriteLine("PASS " + message);
        }
    }
}
