using KboRelayDownloader;

namespace NaverSabermetrics.Web;

public sealed record BotLineupGame(string Id, string? Time, string Opponent, string Status,
    string? Pitcher, ProbableStarterFetcher.LineupPlayer[] Players, DateTimeOffset? UpdatedAt, bool FetchFailed);
public sealed record BotLineupResponse(string Date, string Team, string TimeZone, bool Available,
    bool Stale, int RefreshSeconds, BotLineupGame[] Games, string Text);

public sealed partial class BotStartersService
{
    public static string NormalizeLineupTeam(string? input)
    {
        var key = string.Concat((input ?? "").Where(c => !char.IsWhiteSpace(c))).TrimStart('@').ToUpperInvariant();
        return key switch {
            "한화" or "한화이글스" or "HH" => "한화",
            "KIA" or "기아" or "KIA타이거즈" or "기아타이거즈" or "HT" => "KIA",
            "KT" or "KT위즈" or "케이티" or "케이티위즈" => "KT",
            "LG" or "LG트윈스" or "엘지" or "엘지트윈스" => "LG",
            "롯데" or "롯데자이언츠" or "LT" => "롯데",
            "NC" or "NC다이노스" or "엔씨" or "엔씨다이노스" => "NC",
            "두산" or "두산베어스" or "OB" => "두산",
            "SSG" or "SSG랜더스" or "SK" or "쓱" or "쓱랜더스" or "에스에스지" or "에스에스지랜더스" => "SSG",
            "삼성" or "삼성라이온즈" or "SS" => "삼성",
            "키움" or "키움히어로즈" or "WO" => "키움",
            _ => throw new RequestError("팀명을 확인해 주세요. 예: /라인업 한화")
        };
    }

    public BotLineupResponse QueryLineup(string? team)
    {
        var name = NormalizeLineupTeam(team);
        var date = BotGamesService.ParseDate(null).ToString("yyyy-MM-dd");
        return FormatLineup(date, name, Get(date), DateTimeOffset.UtcNow);
    }

    public static BotLineupResponse FormatLineup(string date, string team, StarterDay? day, DateTimeOffset now)
    {
        static bool Matches(string value, string team) {
            try { return NormalizeLineupTeam(value) == team; } catch (RequestError) { return false; }
        }
        var games = (day?.Games ?? []).Where(g => Matches(g.Home, team) || Matches(g.Away, team))
            .Select(g => {
                var home = Matches(g.Home, team);
                return new BotLineupGame(g.Id, g.Time, home ? g.Away : g.Home, g.Status,
                    home ? g.HomeStarter : g.AwayStarter, home ? g.HomeLineup : g.AwayLineup,
                    g.LineupCheckedAt, g.FetchFailed);
            }).ToArray();
        var stale = day is null || now - day.UpdatedAt > TimeSpan.FromMinutes(15)
            || games.Any(g => g.Status != "CANCEL" && (g.FetchFailed || !g.UpdatedAt.HasValue || now - g.UpdatedAt > TimeSpan.FromMinutes(15)));
        var lines = new List<string>();
        if (day is null) lines.Add("라인업 정보를 아직 수집하지 못했습니다. 잠시 후 다시 확인해 주세요.");
        else if (games.Length == 0) lines.Add($"{date} {team}: 예정된 경기가 없습니다.");
        foreach (var g in games)
        {
            var time = DateTime.TryParse(g.Time, out var parsed) ? parsed.ToString("HH:mm") : "";
            lines.Add($"{date} {team} vs {g.Opponent} {time}".TrimEnd());
            if (g.Status == "CANCEL") { lines.Add("취소된 경기입니다."); continue; }
            if (g.Status is "RESULT" or "ENDED") lines.Add("종료 경기 · 선발 라인업");
            if (g.Players.Length != 9)
                lines.Add(g.FetchFailed ? "라인업 수집이 지연되고 있습니다."
                    : g.Status is "READY" or "BEFORE" ? "선발 라인업 미발표"
                    : "원본 선발 명단이 불완전하여 라인업을 확인할 수 없습니다.");
            else {
                lines.AddRange(g.Players.Select(p => $"{p.Order}. {p.Name} ({(p.Position.Length > 0 ? p.Position : "포지션 미제공")})"));
                if (g.Pitcher is not null) lines.Add($"선발투수 {g.Pitcher}");
            }
        }
        if (stale && day is not null) lines.Add("수집 지연 · 이전 정보일 수 있습니다.");
        return new(date, team, "Asia/Seoul", day is not null, stale, 300, games, string.Join('\n', lines));
    }
}
