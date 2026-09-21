using System.Text.RegularExpressions;

namespace KboRelayDownloader;

/// <summary>
/// A best-effort, box-score-independent lookup of a game's decision (승리/패전/세이브투수) from
/// KBO's own play-by-play text. Used only to patch a past-season Naver game whose "game" metadata
/// never flipped from a live status (e.g. STARTED) to RESULT/ENDED even though its own relay data
/// is otherwise structurally complete — see FullGameCollector.CollectResultAsync's stuck-game
/// override. This fetches and parses only the LiveTextView2.aspx play-by-play page (via
/// RelayClient.DownloadLogsOnlyAsync), never the scoreboard/box-score page, so it never touches
/// BoxScoreParser or IdentityMapper and carries none of the fragility that
/// UnifiedDocumentStore.RequiresKboOfficial ("past season = Naver-only") exists to avoid. Any
/// failure here — network, parsing, missing lines — just means the caller leaves the game exactly
/// as unresolved as it was before this lookup existed.
/// </summary>
public sealed record KboDecision(string? WinPitcherName, string? LosePitcherName, string? SavePitcherName, bool GameOverConfirmed);

public static class KboDecisionLookup
{
    // Same pattern GameWebService.Decisions already trusts to scan stored play text for these
    // labels (승리투수/패전투수/패배투수/홀드/세이브, half- or full-width colon) — reused here
    // rather than a plain prefix match so this lookup is at least as tolerant as the display path.
    private static readonly Regex DecisionLine = new(@"^\s*(승리투수|패전투수|패배투수|세이브(?:투수)?)\s*[:：]\s*(.{1,120})\s*$");

    public static async Task<KboDecision?> TryFetchAsync(RelayClient client, GameRequest game, CancellationToken ct)
    {
        try
        {
            var document = await client.DownloadLogsOnlyAsync(game, ct);
            string? win = null, lose = null, save = null;
            foreach (var text in document.Logs.Select(l => l.Text))
            {
                var m = DecisionLine.Match(text);
                if (!m.Success) continue;
                var name = m.Groups[2].Value.Trim();
                switch (m.Groups[1].Value)
                {
                    case "승리투수": win ??= name; break;
                    case "패전투수" or "패배투수": lose ??= name; break;
                    default: save ??= name; break; // 세이브 / 세이브투수
                }
            }
            // "경기종료" is the same completion marker RelayDocument.IsComplete already relies on
            // (see RelayClient.ParseHtml) — require it here too so a lookup on a genuinely
            // still-in-progress or malformed page can never manufacture a false "it's over".
            var gameOver = document.Logs.Any(l => l.Text == "경기종료");
            if (!gameOver || string.IsNullOrWhiteSpace(win)) return null;
            return new(win, lose, save, gameOver);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }
}
