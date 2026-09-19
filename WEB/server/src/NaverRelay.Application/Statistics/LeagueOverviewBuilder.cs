using System.Globalization;

namespace NaverRelay.Application.Statistics;

public sealed record LeagueOverviewMetric(string Label, string Value, bool Emphasis = false);
public sealed record LeagueOverview(string Title, IReadOnlyList<LeagueOverviewMetric> Metrics);

public static class LeagueOverviewBuilder
{
    public static LeagueOverview FromBatters(IEnumerable<BatterBasicRecordRow> source, bool fullLeague)
    {
        var rows = source.ToList();
        var pa = rows.Sum(x => x.PA); var ab = rows.Sum(x => x.AB); var h = rows.Sum(x => x.Hits);
        var bb = rows.Sum(x => x.Walks); var hbp = rows.Sum(x => x.HitByPitch); var sf = rows.Sum(x => x.SacrificeFlies); var tb = rows.Sum(x => x.TotalBases);
        var avg = D(h, ab); var obp = D(h + bb + hbp, ab + bb + hbp + sf); var slg = D(tb, ab); var ops = obp.HasValue && slg.HasValue ? obp + slg : null;
        var war = rows.Where(x => x.War.HasValue).Sum(x => x.War!.Value);
        var games = fullLeague ? rows.Sum(x => x.Games) / 2.0 : rows.Sum(x => x.Games);
        return new LeagueOverview(fullLeague ? "리그 전체" : "선택 조건 합계", new[]
        {
            M("G", games, "0"), M("PA", pa), M("AB", ab), M("R", rows.Sum(x=>x.Runs)), M("H", h), M("2B", rows.Sum(x=>x.Doubles)), M("3B", rows.Sum(x=>x.Triples)), M("HR", rows.Sum(x=>x.HomeRuns)),
            M("BB", bb), M("SO", rows.Sum(x=>x.Strikeouts)), M("AVG", avg, "0.000"), M("OBP", obp, "0.000"), M("SLG", slg, "0.000"), M("OPS", ops, "0.000", true), M("WAR", war, "0.00", true),
        });
    }

    // teamShutouts: 필터링된 경기에서 상대를 무실점으로 막은 경기 수(합작 완봉 포함).
    // 개인 완봉(한 투수가 혼자 던진 완봉) 합계로 대신 계산하면 안 됩니다 — 여러 투수가
    // 이어 던져 막은 경기가 통째로 누락됩니다. 호출부(RecordService)에서 Games 테이블
    // 기준으로 따로 집계해 전달합니다.
    public static LeagueOverview FromPitchers(IEnumerable<PitcherBasicRecordRow> source, bool fullLeague, int teamShutouts)
    {
        var rows = source.ToList();
        var ip = rows.Where(x=>x.InningsPitched.HasValue).Sum(x=>x.InningsPitched!.Value); var er = rows.Sum(x=>x.EarnedRuns); var r = rows.Sum(x=>x.RunsAllowed); var h = rows.Sum(x=>x.HitsAllowed); var bb = rows.Sum(x=>x.Walks);
        var era = ip > 0 ? er * 9.0 / ip : (double?)null; var ra9 = ip > 0 ? r * 9.0 / ip : (double?)null; var whip = ip > 0 ? (h + bb) / ip : (double?)null;
        var fipRows = rows.Where(x=>x.Fip.HasValue && x.InningsPitched.HasValue && x.InningsPitched > 0).ToList(); var fipWeight = fipRows.Sum(x=>x.InningsPitched!.Value);
        var fip = fipWeight > 0 ? fipRows.Sum(x=>x.Fip!.Value*x.InningsPitched!.Value)/fipWeight : (double?)null;
        var games = fullLeague ? rows.Sum(x => x.Games) / 2.0 : rows.Sum(x => x.Games);
        return new LeagueOverview(fullLeague ? "리그 전체" : "선택 조건 합계", new[]
        {
            M("G", games, "0"), M("IP", ip, "0.0"), M("ER", er), M("R", r), M("H", h), M("HR", rows.Sum(x=>x.HomeRunsAllowed)), M("BB", bb), M("SO", rows.Sum(x=>x.Strikeouts)),
            M("ERA", era, "0.00", true), M("RA9", ra9, "0.00"), M("FIP", fip, "0.00"), M("WHIP", whip, "0.00"), M("완봉", teamShutouts), M("WAR", rows.Where(x=>x.FanGraphsWar.HasValue).Sum(x=>x.FanGraphsWar!.Value), "0.00", true),
            M("RA9-WAR", rows.Where(x=>x.Ra9War.HasValue).Sum(x=>x.Ra9War!.Value), "0.00"), M("Blend WAR", rows.Where(x=>x.BlendWar.HasValue).Sum(x=>x.BlendWar!.Value), "0.00"),
        });
    }

    private static double? D(double n, double d) => Math.Abs(d) < 1e-12 ? null : n/d;
    private static LeagueOverviewMetric M(string label, int value, bool emphasis=false) => new(label, value.ToString("N0", CultureInfo.InvariantCulture), emphasis);
    private static LeagueOverviewMetric M(string label, double? value, string format, bool emphasis=false) => new(label, value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-", emphasis);
}
