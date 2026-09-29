using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace KboRelayDownloader;

// KBO 공식 홈페이지 "전체 등록 현황"(Player/RegisterAll.aspx)에서 그날 각 팀에 실제 등록되어 있는
// 선수 전체 명단(선발 후보 포함)을 가져옵니다. 이 페이지는 그날 날짜 기준으로 매일 갱신되는 KBO
// 자체의 공식 시즌 등록 데이터이므로, 이 값 자체가 곧 "그 팀의 현재 로스터"입니다 — 경기별로
// "공식/추정"을 나눌 필요 없이, 이걸로 매일 그대로 덮어써서 최적 선발 라인업 계산의 입력으로
// 씁니다.
//
// 페이지에 실제 렌더링되는 <table class="tData tDays"> (팀당 1개)에서 감독/코치/투수/포수/
// 내야수/외야수 열마다 "이름(등번호)" 목록을 읽습니다. 이 표에는 pcode가 없어서(이름+등번호만),
// pcode는 이 앱 DB의 Players 테이블(Name+LatestTeam으로 색인)에서 별도로 찾습니다 — 페이지에
// 같이 실려 있는 __VIEWSTATE(ASP.NET DataTable 직렬화 diffgram)에서 pcode를 직접 읽는 방법도
// 시도했지만, 대용량 바이너리 블롭을 통째로 UTF-8로 디코딩하는 방식이라 이름 수백 건 중 절반
// 이상이 깨져서 신뢰할 수 없었습니다(실측: 186/333건 매칭 실패). DB 조회가 더 안정적입니다.
public static class RegisterAllRosterFetcher
{
    /// <summary>구단 열 텍스트를 이 앱 전역에서 쓰는 2글자 팀 코드로 맞춥니다(GameId의 원정/홈
    /// 2글자 코드, 예: 20260923HTOB02026 → 원정 HT, 홈 OB, 와 동일한 체계).</summary>
    private static readonly Dictionary<string, string> TeamNameToCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LG"] = "LG",
        ["KT"] = "KT", ["kt"] = "KT",
        ["SSG"] = "SK", ["SK"] = "SK",
        ["두산"] = "OB", ["OB"] = "OB",
        ["롯데"] = "LT", ["LT"] = "LT",
        ["삼성"] = "SS", ["SS"] = "SS",
        ["한화"] = "HH", ["HH"] = "HH",
        ["KIA"] = "HT", ["기아"] = "HT", ["HT"] = "HT",
        ["NC"] = "NC",
        ["키움"] = "WO", ["WO"] = "WO",
    };

    // "구단" 열 텍스트가 팀 코드와 정확히 일치하지 않을 수 있어(예: "KT44명") 포함 여부로도 판정합니다.
    private static readonly (string Code, string[] Keywords)[] TeamKeywords =
    {
        ("SK", new[] { "SSG", "SK" }),
        ("LG", new[] { "LG" }),
        ("KT", new[] { "KT", "kt" }),
        ("OB", new[] { "두산", "Doosan", "OB" }),
        ("LT", new[] { "롯데", "Lotte", "LT" }),
        ("SS", new[] { "삼성", "Samsung", "SS" }),
        ("HH", new[] { "한화", "Hanwha", "HH" }),
        ("HT", new[] { "KIA", "기아", "HT" }),
        ("NC", new[] { "NC" }),
        ("WO", new[] { "키움", "Kiwoom", "WO" }),
    };

    private static readonly string[] PlayerPositionColumns = { "투수", "포수", "내야수", "외야수" };

    public sealed record RosterPlayer(string Name, string? BackNo, string Position);

    public static async Task<string> FetchHtmlAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.koreabaseball.com/Player/RegisterAll.aspx");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>팀 코드 → 그 팀의 현재 등록 선수 전체 명단(투수/포수/내야수/외야수만; 감독/코치 제외).
    /// 구조가 바뀌어 못 읽으면 InvalidDataException을 던져 잘못된 값으로 기존 값을 덮어쓰지 않습니다
    /// (호출자가 실패를 잡아 이번 주기는 건너뜁니다).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<RosterPlayer>> ParseRoster(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var tables = doc.QuerySelectorAll("table.tData.tDays");
        if (tables.Length == 0) throw new InvalidDataException("등록 현황 표(table.tData.tDays)를 찾지 못했습니다. 페이지 구조가 변경되었을 수 있습니다.");

        var result = new Dictionary<string, List<RosterPlayer>>();
        foreach (var table in tables)
        {
            var headers = table.QuerySelectorAll("thead th")
                .Select(h => Regex.Replace(h.TextContent, @"\s+", " ").Trim()).ToArray();
            var bodyRow = table.QuerySelector("tbody tr");
            if (bodyRow is null) continue;
            // 첫 열(구단)은 <td>가 아니라 <th scope="row">일 수 있으므로 td/th를 모두 포함합니다.
            var cells = bodyRow.Children.Where(e => e.LocalName is "td" or "th").ToArray();
            if (cells.Length == 0 || cells.Length != headers.Length) continue;

            var teamRaw = ExtractTeamText(cells[0]);
            var teamCode = ResolveTeamCode(teamRaw);
            if (teamCode is null) continue;

            for (var i = 1; i < headers.Length; i++)
            {
                var columnLabel = PlayerPositionColumns.FirstOrDefault(p => headers[i].StartsWith(p, StringComparison.Ordinal));
                if (columnLabel is null) continue; // 감독/코치 열은 건너뜁니다.

                foreach (var li in cells[i].QuerySelectorAll("li"))
                {
                    var text = Regex.Replace(li.TextContent, @"\s+", " ").Trim();
                    var itemMatch = Regex.Match(text, @"^(?<name>.+?)\((?<no>[^()]*)\)$");
                    if (!itemMatch.Success) continue;
                    if (!result.TryGetValue(teamCode, out var teamList)) result[teamCode] = teamList = new();
                    teamList.Add(new RosterPlayer(itemMatch.Groups["name"].Value.Trim(), itemMatch.Groups["no"].Value.Trim(), columnLabel));
                }
            }
        }
        if (result.Count == 0) throw new InvalidDataException("등록 현황 표에서 선수 명단을 찾지 못했습니다.");
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RosterPlayer>)kv.Value);
    }

    /// <summary>구단 열의 텍스트를 뽑습니다. 텍스트가 비어 있으면(팀 로고 이미지만 있는 경우 등)
    /// img alt/title 속성에서 팀명을 대신 찾습니다.</summary>
    private static string? ExtractTeamText(IElement cell)
    {
        var text = Regex.Replace(cell.TextContent, @"\s+", " ").Trim();
        if (!string.IsNullOrWhiteSpace(text)) return text;
        var alt = cell.QuerySelector("img")?.GetAttribute("alt") ?? cell.QuerySelector("[title]")?.GetAttribute("title");
        return string.IsNullOrWhiteSpace(alt) ? null : Regex.Replace(alt, @"\s+", " ").Trim();
    }

    private static string? ResolveTeamCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (TeamNameToCode.TryGetValue(raw, out var exact)) return exact;
        foreach (var (code, keywords) in TeamKeywords)
            if (keywords.Any(k => raw.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return code;
        return null;
    }
}
