using System.Globalization;
using System.Net.Http;
using NaverRelayUI.Collection;

namespace NaverRelayUI;

// Unattended entry point for Task Scheduler: never touches WinForms, so it
// runs fine even without an interactive desktop session.
//
// Usage:
//   NaverKboRelayUI.exe --collect [--from yyyy-MM-dd] [--to yyyy-MM-dd]
//                        [--output <dir>] [--log <file>]
//
// Reuses the exact same GameIdCollector/RelayCollector pipeline as the
// "날짜별 통합 수집" tab in MainForm, so this call is safe to rerun on a
// timer: already-complete games are skipped and partial/incomplete games
// (extra innings still going, a source briefly unavailable, etc.) are
// retried automatically on the next run. See RelayCollector.CollectAllAsync.
internal static class HeadlessCollector
{
    public static async Task<int> RunAsync(string[] args)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var from = GetOption(args, "--from") ?? today;
        var to = GetOption(args, "--to") ?? today;
        var outputDir = GetOption(args, "--output") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NaverKboCombined");
        var logPath = GetOption(args, "--log");

        using var writer = logPath is null ? null : new StreamWriter(logPath, append: true) { AutoFlush = true };
        void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            if (writer is null) Console.WriteLine(line);
            else writer.WriteLine(line);
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; personal-stats-project/0.1)");

            Log($"{from} ~ {to} KBO 경기 목록을 가져옵니다...");
            var games = await GameIdCollector.CollectAllKboGamesAsync(http, from, to, delayMs: 300, log: Log);
            Log($"KBO 경기 {games.Count}건 확인됨. 중계 데이터를 수집합니다 -> {outputDir}");

            var summary = await RelayCollector.CollectAllAsync(http, games, outputDir, delayMs: 500, log: Log);
            Log($"통합 완료 {summary.Complete}, 부분 저장 {summary.Partial}, 건너뜀(기존/취소) {summary.Skipped}, " +
                $"정규시즌 외 제외 {summary.Excluded}, 실패 {summary.Failed}");

            // Non-zero only for hard failures. Partial games are expected
            // (game still in progress, source briefly unavailable, etc.)
            // and simply get retried on the next scheduled run.
            return summary.Failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Log($"오류: {ex}");
            return 2;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
