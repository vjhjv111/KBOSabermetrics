using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace KboRelayDownloader;

// One row of KBO's pre-game lineup/box-score table, kept as a generic column->value map
// (same shape as BoxScoreParser.StatTable rows) so callers can read "타순"/"포지션"/etc.
// without this parser hardcoding every column name.
public sealed record LineupPreviewPlayer(int RowOrder, string Name, IReadOnlyDictionary<string, string> Row);

public sealed record TeamLineupPreview(string TeamCode, string? TeamName,
    IReadOnlyList<LineupPreviewPlayer> Batters, LineupPreviewPlayer? StartingPitcher);

public sealed record LineupPreview(DateTimeOffset DownloadedAt, TeamLineupPreview? Away, TeamLineupPreview? Home);

/// <summary>
/// Reads the SAME "#scrollLineup1_content"/"#scrollLineup2_content" 타자/투수 tables that
/// BoxScoreParser reads after a game ends, but tolerantly: before KBO announces the day's
/// lineup, these tables are simply absent or empty, and that is not an error here (unlike
/// BoxScoreParser.Parse, which is strict because it expects a finished game). Returns null
/// wherever nothing usable was found, so callers can fall back to an estimate.
/// </summary>
public static class LineupPreviewParser
{
    private static string Text(IElement e) => Regex.Replace(e.TextContent, @"\s+", " ").Trim();

    public static LineupPreview? TryParse(GameRequest game, string relayHtml)
    {
        var doc = new HtmlParser().ParseDocument(relayHtml);

        TeamLineupPreview? Team(int index)
        {
            var tables = doc.QuerySelectorAll($"#scrollLineup{index + 1}_content table");
            var battingTable = tables.FirstOrDefault(t => t.QuerySelector("thead th")?.TextContent.Trim() == "타자");
            var pitchingTable = tables.FirstOrDefault(t => t.QuerySelector("thead th")?.TextContent.Trim() == "투수");
            if (battingTable is null && pitchingTable is null) return null;

            var batters = ReadRows(battingTable, "타자");
            var pitchers = ReadRows(pitchingTable, "투수");
            if (batters.Count == 0 && pitchers.Count == 0) return null;

            var teamCode = game.GameId.Length >= 12 ? game.GameId.Substring(8 + index * 2, 2) : "";
            var teamName = doc.QuerySelector($"#lineup{index + 1} h3")?.TextContent?.Replace(" 라인업", "").Trim();
            return new(teamCode, teamName, batters, pitchers.FirstOrDefault());
        }

        var away = Team(0);
        var home = Team(1);
        return away is null && home is null ? null : new(DateTimeOffset.UtcNow, away, home);
    }

    private static List<LineupPreviewPlayer> ReadRows(IElement? table, string nameColumn)
    {
        var list = new List<LineupPreviewPlayer>();
        if (table is null) return list;
        var columns = table.QuerySelectorAll("thead tr th").Select(Text).ToArray();
        if (columns.Length == 0 || !columns.Contains(nameColumn)) return list;

        var rowOrder = 0;
        foreach (var tr in table.QuerySelectorAll("tbody tr"))
        {
            rowOrder++;
            var cells = tr.Children.Where(e => e.LocalName is "td" or "th").Select(Text).ToArray();
            if (cells.Length != columns.Length) continue;
            var row = columns.Select((key, i) => (key, value: cells[i]))
                .ToDictionary(p => p.key, p => p.value);
            if (!row.TryGetValue(nameColumn, out var name) || string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new(rowOrder, name, row));
        }
        return list;
    }
}
