using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverSabermetrics.Web;

public sealed record StatCondition(string Stat, string Operator, double Value);

public sealed record RecordRequest
{
    public string Room { get; init; } = "season";
    public string Role { get; init; } = "batter";
    public string View { get; init; } = "basic";
    public int? Year { get; init; }
    public string Competition { get; init; } = "정규시즌";
    public string? Team { get; init; }
    public string? Position { get; init; }
    public double QualificationPercent { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public int? RecentGames { get; init; }
    public int? RecentDays { get; init; }
    public string? Weekday { get; init; }
    public string? Venue { get; init; }
    public string? Opponent { get; init; }
    public string? Stadium { get; init; }
    public string? PlayerName { get; init; }
    public string? PlayerCode { get; init; }
    public string? Inning { get; init; }
    public int? Outs { get; init; }
    public string? Runners { get; init; }
    public string? Score { get; init; }
    public int? Balls { get; init; }
    public int? Strikes { get; init; }
    public int? BatOrder { get; init; }
    public List<StatCondition> Conditions { get; init; } = new();
    public string? SortBy { get; init; }
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;

    public void Validate(SiteOptions options)
    {
        if (!new[] { "season", "career", "team", "constants" }.Contains(Room)) throw new RequestError("기록실 구분이 잘못되었습니다.");
        if (Room != "season" && !string.IsNullOrEmpty(Position)) throw new RequestError("포지션 조건은 시즌 선수 기록에서만 사용합니다.");
        if (Role == "pitcher" && !string.IsNullOrEmpty(Position)) throw new RequestError("투수 조회에서는 포지션 조건을 해제하세요.");
        if (Role is not ("batter" or "pitcher")) throw new RequestError("타자/투수를 선택하세요.");
        if (Year is < 1900 or > 2200) throw new RequestError("연도 범위가 잘못되었습니다.");
        if (!new[] { "정규시즌", "전체", "시범경기", "포스트시즌", "올스타전" }.Contains(Competition)) throw new RequestError("경기 구분이 잘못되었습니다.");
        if (Page < 1 || PageSize < 1 || PageSize > options.MaxPageSize || (long)(Page - 1) * PageSize >= options.MaxAccessibleRows)
            throw new RequestError("조회 가능한 페이지 또는 출력 건수를 초과했습니다.");
        if (!double.IsFinite(QualificationPercent) || QualificationPercent < 0 || QualificationPercent > 100)
            throw new RequestError("규정 비율은 0~100입니다.");
        if (QualificationPercent > 0 && Room != "season") throw new RequestError("규정 비율은 시즌기록실에서만 사용합니다.");
        if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date > EndDate.Value.Date) throw new RequestError("시작일이 종료일보다 늦습니다.");
        if (StartDate?.Year is < 1900 or > 2200 || EndDate?.Year is < 1900 or > 2200) throw new RequestError("날짜 범위가 잘못되었습니다.");
        if (RecentDays.HasValue && RecentGames.HasValue || (RecentDays.HasValue || RecentGames.HasValue) && (StartDate.HasValue || EndDate.HasValue)) throw new RequestError("기간 조건은 하나의 방식으로 지정하세요.");
        if (RecentGames.HasValue && !new[] { 5,10,20,30 }.Contains(RecentGames.Value)) throw new RequestError("최근 경기 수가 잘못되었습니다.");
        if (RecentDays.HasValue && !new[] { 7,14,30,60,90 }.Contains(RecentDays.Value)) throw new RequestError("최근 일수가 잘못되었습니다.");
        Check(Weekday, ["월", "화", "수", "목", "금", "토", "일"], "요일");
        Check(Venue, ["홈", "원정"], "홈/원정");
        Check(Position, ["C","1B","2B","3B","SS","LF","CF","RF","DH"], "포지션");
        Check(Inning, ["1~3회","4~6회","7~9회","연장","1회","2회","3회","4회","5회","6회","7회","8회","9회"], "이닝");
        Check(Runners, ["주자 없음","1루","2루","3루","1·2루","1·3루","2·3루","만루","득점권"], "주자");
        Check(Score, ["동점","리드","열세","1점 리드","2점 리드","3점 이상 리드","1점 열세","2점 열세","3점 이상 열세","1점차 이내","2점차 이내","3점차 이내"], "점수");
        if (Outs is < 0 or > 2 || Balls is < 0 or > 3 || Strikes is < 0 or > 2 || BatOrder is < 1 or > 9) throw new RequestError("상황 범위가 잘못되었습니다.");
        if (Balls.HasValue != Strikes.HasValue) throw new RequestError("볼/스트라이크를 함께 선택하세요.");
        foreach (var s in new[] { Team, Opponent, Stadium, PlayerName, PlayerCode, SortBy, View })
            if (s is not null && (s.Length > 80 || s.Any(char.IsControl))) throw new RequestError("필터 값이 너무 길거나 유효하지 않습니다.");
        if ((!string.IsNullOrWhiteSpace(Venue) || !string.IsNullOrWhiteSpace(Opponent)) && string.IsNullOrWhiteSpace(Team))
            throw new RequestError("홈/원정 또는 VS를 사용할 때 기준 팀을 선택하세요.");
        if (!string.IsNullOrEmpty(Team) && Team == Opponent) throw new RequestError("기준 팀과 상대 팀은 달라야 합니다.");
        if (Conditions is null || Conditions.Count > 2) throw new RequestError("스탯 조건은 최대 2개입니다.");
        foreach (var c in Conditions)
            if (c is null || string.IsNullOrEmpty(c.Stat) || c.Stat.Length > 80 || !double.IsFinite(c.Value) || !new[] { "gte", "gt", "lte", "lt", "eq" }.Contains(c.Operator))
                throw new RequestError("스탯 조건이 잘못되었습니다.");
    }

    private static void Check(string? value, string[] allowed, string label)
    {
        if (!string.IsNullOrEmpty(value) && !allowed.Contains(value)) throw new RequestError(label + " 값이 잘못되었습니다.");
    }

    public GameQuery ToQuery(DatabaseCatalog catalog)
    {
        DateTime? end = EndDate?.Date;
        DateTime? start = StartDate?.Date;
        if (RecentDays.HasValue)
        {
            var latest = catalog.MaxGameDate?.Date ?? DateTime.Today;
            if (Year.HasValue && latest.Year > Year.Value) latest = new DateTime(Year.Value,12,31);
            end = latest;
            start = latest.AddDays(1 - RecentDays.Value);
        }
        return new GameQuery
        {
            Grouping = Room == "team" ? AnalyticsGrouping.Team : Room == "career" ? AnalyticsGrouping.PlayerCareer : AnalyticsGrouping.PlayerByTeam,
            SeasonYear = Room == "career" ? null : Year,
            Competition = Competition, TeamCode = Clean(Team), OpponentCode = Clean(Opponent), Venue = Clean(Venue),
            Stadium = Clean(Stadium), Weekday = Clean(Weekday), StartDate = start, EndDate = end,
            RecentGameCount = RecentGames, InningFilter = Clean(Inning), OutsBefore = Outs,
            RunnerState = Clean(Runners), ScoreSituation = Clean(Score), BallsBefore = Balls, StrikesBefore = Strikes, BatOrder = BatOrder
        };
    }
    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
}

public sealed record WebColumn(string Key, string Label, string Kind, bool Numeric, bool Sortable = true);
public sealed record WebRow(string? EntityCode, Dictionary<string,string> Cells);
public sealed record TablePage(
    IReadOnlyList<WebColumn> Columns, IReadOnlyList<WebRow> Rows,
    int Total, int AccessibleTotal, int Page, int PageSize, string Applied,
    IReadOnlyList<string> Warnings, long ElapsedMs, bool Cached, string FormulaVersion,
    LeagueOverview? LeagueOverview = null);
