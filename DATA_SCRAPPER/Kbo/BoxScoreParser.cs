using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace KboRelayDownloader;

// Keep source values as strings: pitching innings such as 0.1 mean one out,
// not one tenth of an inning. Preserve '-' instead of changing it to zero.
public sealed record StatTable(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, string>> Rows)
{
    public IReadOnlyList<RowIdentity>? RowIdentities { get; init; }
}
public sealed record InningScore(string Inning, string Runs);
public sealed record TeamBoxScore(string TeamCode, string TeamName, IReadOnlyList<InningScore> Innings,
    Dictionary<string, string> Totals, StatTable Batting, StatTable Pitching);
public sealed record BoxScore(DateTimeOffset DownloadedAt, IReadOnlyList<string> Sources, TeamBoxScore Away, TeamBoxScore Home);

public static class BoxScoreParser
{
    static string Text(IElement e) => Regex.Replace(e.TextContent, @"\s+", " ").Trim();

    public static BoxScore Parse(GameRequest game, string relayHtml, string scoreboardHtml)
    {
        var parser = new HtmlParser();
        var relay = parser.ParseDocument(relayHtml);
        var board = parser.ParseDocument(scoreboardHtml);
        var names = ReadTable(board.QuerySelector("#tblScoreBoard1"));
        var innings = ReadTable(board.QuerySelector("#tblScoreBoard2"));
        var totals = ReadTable(board.QuerySelector("#tblScoreBoard3"));
        if (names.Rows.Count != 2 || innings.Rows.Count != 2 || totals.Rows.Count != 2)
            throw new InvalidDataException("박스스코어의 양 팀 점수판을 찾지 못했습니다. 기존 파일은 유지됩니다.");
        if (!new[] { "R", "H", "E", "B" }.All(totals.Columns.Contains))
            throw new InvalidDataException("점수판 R/H/E/B 열이 변경되었습니다.");

        TeamBoxScore Team(int index)
        {
            var tables = relay.QuerySelectorAll($"#scrollLineup{index + 1}_content table");
            var batting = ReadTable(tables.FirstOrDefault(t => t.QuerySelector("thead th")?.TextContent.Trim() == "타자"));
            var pitching = ReadTable(tables.FirstOrDefault(t => t.QuerySelector("thead th")?.TextContent.Trim() == "투수"));
            var teamName = names.Rows[index][names.Columns[0]];
            var lineupName = relay.QuerySelector($"#lineup{index + 1} h3")?.TextContent.Trim();
            if (lineupName != teamName + " 라인업")
                throw new InvalidDataException("플레이로그와 점수판의 팀 이름이 일치하지 않습니다.");
            if (!batting.Columns.Contains("타수") || !pitching.Columns.Contains("이닝"))
                throw new InvalidDataException("박스스코어 타자/투수 표 구조가 변경되었습니다.");
            return new(game.GameId.Substring(8 + index * 2, 2), teamName,
                innings.Columns.Select(key => new InningScore(key, innings.Rows[index][key])).ToArray(),
                totals.Rows[index], batting, pitching);
        }

        return new(DateTimeOffset.UtcNow,
            ["https://www.koreabaseball.com/Game/LiveTextView2.aspx", "https://www.koreabaseball.com/Game/LiveTextView1.aspx"],
            Team(0), Team(1));
    }

    static StatTable ReadTable(IElement? table)
    {
        if (table is null) throw new InvalidDataException("필수 박스스코어 표가 없습니다.");
        var columns = table.QuerySelectorAll("thead tr th").Select(Text).ToArray();
        if (columns.Length == 0 || columns.Any(string.IsNullOrWhiteSpace) || columns.Distinct().Count() != columns.Length)
            throw new InvalidDataException("박스스코어 열 제목이 없거나 중복됩니다.");
        var rows = new List<Dictionary<string, string>>();
        foreach (var tr in table.QuerySelectorAll("tbody tr"))
        {
            var cells = tr.Children.Where(e => e.LocalName is "td" or "th").ToArray();
            if (cells.Length != columns.Length || cells.Any(e => (e.GetAttribute("colspan") ?? "1") != "1" || (e.GetAttribute("rowspan") ?? "1") != "1"))
                throw new InvalidDataException("박스스코어 행/열 구조가 변경되었습니다.");
            rows.Add(columns.Select((key, i) => (key, value: Text(cells[i]))).ToDictionary(p => p.key, p => p.value));
        }
        if (rows.Count == 0) throw new InvalidDataException("박스스코어에 선수/점수 행이 없습니다.");
        return new(columns, rows);
    }
}
