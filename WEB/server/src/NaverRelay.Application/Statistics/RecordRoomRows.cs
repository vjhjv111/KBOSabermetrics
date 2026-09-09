using System.ComponentModel;
using NaverRelay.Application.Queries;

namespace NaverRelay.Application.Statistics;

public sealed class BatterBasicRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("Pos.")] public string? PrimaryPosition { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("WAR")] public double? War { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("ePA")] public int EffectivePA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("R")] public int Runs { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("TB")] public int TotalBases { get; init; }
    [DisplayName("RBI")] public int RunsBattedIn { get; init; }
    [DisplayName("SB")] public int StolenBases { get; init; }
    [DisplayName("CS")] public int CaughtStealing { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("IBB")] public int IntentionalWalks { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("GDP")] public int DoublePlays { get; init; }
    [DisplayName("SH")] public int SacrificeBunts { get; init; }
    [DisplayName("SF")] public int SacrificeFlies { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("R/ePA*")] public double? RunsPerEffectivePa { get; init; }
    [DisplayName("wRC+*")] public double? WrcPlus { get; init; }
}

public sealed class BatterAdvancedRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("BB/K")] public double? WalkToStrikeout { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
    [DisplayName("IsoP")] public double? IsoP { get; init; }
    [DisplayName("IsoD")] public double? IsoD { get; init; }
    [DisplayName("R/ePA*")] public double? RunsPerEffectivePa { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
    [DisplayName("wRC*")] public double? Wrc { get; init; }
    [DisplayName("RC27*")] public double? RunsCreated27 { get; init; }
    [DisplayName("wRC+*")] public double? WrcPlus { get; init; }
    [DisplayName("OPS+")] public double? OpsPlus { get; init; }
}

public sealed class BatterValueRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("타격 RAA*")] public double? BattingRuns { get; init; }
    [DisplayName("주루 RAA*")] public double? RunningRuns { get; init; }
    [DisplayName("포지션 보정(FG)*")] public double? PositionRuns { get; init; }
    [DisplayName("공격 Runs*")] public double? OffensiveRuns { get; init; }
    [DisplayName("대체 Runs*")] public double? ReplacementRuns { get; init; }
    [DisplayName("RAR*")] public double? RunsAboveReplacement { get; init; }
    [DisplayName("RPW*")] public double? RunsPerWin { get; init; }
    [DisplayName("WAR*")] public double? War { get; init; }
}

public sealed class BatterExtendedRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("PSN")] public double? PowerSpeedNumber { get; init; }
    [DisplayName("TotA")] public double? TotalAverage { get; init; }
    [DisplayName("SecA")] public double? SecondaryAverage { get; init; }
    [DisplayName("RC*")] public double? RunsCreated { get; init; }
    [DisplayName("RC27*")] public double? RunsCreated27 { get; init; }
    [DisplayName("BB/K")] public double? WalkToStrikeout { get; init; }
    [DisplayName("ISO")] public double? IsoP { get; init; }
}

public sealed class BatterPowerRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("XBH")] public int ExtraBaseHits { get; init; }
    [DisplayName("XBH/H")] public double? ExtraBaseHitRate { get; init; }
    [DisplayName("HR/XBH")] public double? HomeRunPerExtraBaseHit { get; init; }
    [DisplayName("PA/HR")] public double? PlateAppearancesPerHomeRun { get; init; }
    [DisplayName("AB/HR")] public double? AtBatsPerHomeRun { get; init; }
    [DisplayName("IsoP")] public double? IsoP { get; init; }
    [DisplayName("SLG/AVG")] public double? SluggingToAverage { get; init; }
}

public sealed class BatterTeamBattingRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("R")] public int Runs { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("TB")] public int TotalBases { get; init; }
    [DisplayName("RBI")] public int RunsBattedIn { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("GDP")] public int DoublePlays { get; init; }
    [DisplayName("SH")] public int SacrificeBunts { get; init; }
    [DisplayName("SF")] public int SacrificeFlies { get; init; }
    [DisplayName("RBI/PA")] public double? RbiPerPa { get; init; }
    [DisplayName("R/PA")] public double? RunsPerPa { get; init; }
}

public sealed class BatterStealRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("SB")] public int StolenBases { get; init; }
    [DisplayName("CS")] public int CaughtStealing { get; init; }
    [DisplayName("SBA")] public int Attempts { get; init; }
    [DisplayName("SB%")] public double? SuccessRate { get; init; }
    [DisplayName("도루 RAA*")] public double? RunningRuns { get; init; }
}

public sealed class BatterBaserunningRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("R")] public int Runs { get; init; }
    [DisplayName("SB")] public int StolenBases { get; init; }
    [DisplayName("CS")] public int CaughtStealing { get; init; }
    [DisplayName("주루 Runs*")] public double? RunningRuns { get; init; }
    [DisplayName("R/G")] public double? RunsPerGame { get; init; }
}

public sealed class BatterPitchProfileRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("Pitches")] public int Pitches { get; init; }
    [DisplayName("P/PA")] public double? PitchesPerPa { get; init; }
    [DisplayName("Swing%")] public double? SwingRate { get; init; }
    [DisplayName("Contact%")] public double? ContactRate { get; init; }
    [DisplayName("Whiff%")] public double? WhiffRate { get; init; }
    [DisplayName("CSW%")] public double? CswRate { get; init; }
    [DisplayName("Z-Swing%")] public double? ZoneSwingRate { get; init; }
    [DisplayName("O-Swing%")] public double? ChaseRate { get; init; }
    [DisplayName("Z-Contact%")] public double? ZoneContactRate { get; init; }
    [DisplayName("O-Contact%")] public double? OutZoneContactRate { get; init; }
    [DisplayName("1st Swing%")] public double? FirstPitchSwingRate { get; init; }
}

public sealed class BatterClutchRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("후반/접전 PA")] public int LateClosePA { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("pLI*")] public double? AverageLeverageIndex { get; init; }
    [DisplayName("WPA+")] public double? PositiveWpa { get; init; }
    [DisplayName("WPA-")] public double? NegativeWpa { get; init; }
    [DisplayName("WPA")] public double? Wpa { get; init; }
    [DisplayName("WPA/LI*")] public double? WpaPerLi { get; init; }
}

public sealed class BatterBattedBallRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("BIP")] public int BallsInPlay { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
    [DisplayName("GB%")] public double? GroundBallRate { get; init; }
    [DisplayName("FB%")] public double? FlyBallRate { get; init; }
    [DisplayName("LD%")] public double? LineDriveRate { get; init; }
    [DisplayName("IFFB%")] public double? InfieldFlyRate { get; init; }
    [DisplayName("GB/FB")] public double? GroundBallToFlyBall { get; init; }
    [DisplayName("HR/FB")] public double? HomeRunPerFlyBall { get; init; }
    [DisplayName("ROE")] public int ReachedOnError { get; init; }
    [DisplayName("번트 시도")] public int BuntAttempts { get; init; }
    [DisplayName("번트 안타")] public int BuntHits { get; init; }
    [DisplayName("번트 AVG")] public double? BuntAverage { get; init; }
}

public sealed class BatterDirectionRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("방향 타구")] public int DirectionalBallsInPlay { get; init; }
    [DisplayName("좌%")] public double? LeftRate { get; init; }
    [DisplayName("좌중%")] public double? LeftCenterRate { get; init; }
    [DisplayName("중%")] public double? CenterRate { get; init; }
    [DisplayName("우중%")] public double? RightCenterRate { get; init; }
    [DisplayName("우%")] public double? RightRate { get; init; }
    [DisplayName("내야%")] public double? InfieldRate { get; init; }
    [DisplayName("좌 H")] public int LeftHits { get; init; }
    [DisplayName("좌중 H")] public int LeftCenterHits { get; init; }
    [DisplayName("중 H")] public int CenterHits { get; init; }
    [DisplayName("우중 H")] public int RightCenterHits { get; init; }
    [DisplayName("우 H")] public int RightHits { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
}

public sealed class BatterPitchTypeRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("구종 (최종구)")] public string PitchType { get; init; } = string.Empty;
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("1B")] public int Singles { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
    [Browsable(false)] public int SacrificeFlies { get; init; }
    [Browsable(false)] public int TotalBasesRaw { get; init; }
}



public sealed class BatterPitchTypeMatrixRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }

    [DisplayName("직구 AVG")] public double? FastballAVG { get; init; }
    [DisplayName("직구 OBP")] public double? FastballOBP { get; init; }
    [DisplayName("직구 SLG")] public double? FastballSLG { get; init; }
    [DisplayName("직구 OPS")] public double? FastballOPS { get; init; }
    [DisplayName("투심 AVG")] public double? TwoSeamAVG { get; init; }
    [DisplayName("투심 OBP")] public double? TwoSeamOBP { get; init; }
    [DisplayName("투심 SLG")] public double? TwoSeamSLG { get; init; }
    [DisplayName("투심 OPS")] public double? TwoSeamOPS { get; init; }
    [DisplayName("커터 AVG")] public double? CutterAVG { get; init; }
    [DisplayName("커터 OBP")] public double? CutterOBP { get; init; }
    [DisplayName("커터 SLG")] public double? CutterSLG { get; init; }
    [DisplayName("커터 OPS")] public double? CutterOPS { get; init; }
    [DisplayName("커브 AVG")] public double? CurveAVG { get; init; }
    [DisplayName("커브 OBP")] public double? CurveOBP { get; init; }
    [DisplayName("커브 SLG")] public double? CurveSLG { get; init; }
    [DisplayName("커브 OPS")] public double? CurveOPS { get; init; }
    [DisplayName("슬라이더 AVG")] public double? SliderAVG { get; init; }
    [DisplayName("슬라이더 OBP")] public double? SliderOBP { get; init; }
    [DisplayName("슬라이더 SLG")] public double? SliderSLG { get; init; }
    [DisplayName("슬라이더 OPS")] public double? SliderOPS { get; init; }
    [DisplayName("체인지업 AVG")] public double? ChangeupAVG { get; init; }
    [DisplayName("체인지업 OBP")] public double? ChangeupOBP { get; init; }
    [DisplayName("체인지업 SLG")] public double? ChangeupSLG { get; init; }
    [DisplayName("체인지업 OPS")] public double? ChangeupOPS { get; init; }
    [DisplayName("싱커 AVG")] public double? SinkerAVG { get; init; }
    [DisplayName("싱커 OBP")] public double? SinkerOBP { get; init; }
    [DisplayName("싱커 SLG")] public double? SinkerSLG { get; init; }
    [DisplayName("싱커 OPS")] public double? SinkerOPS { get; init; }
    [DisplayName("포크 AVG")] public double? ForkAVG { get; init; }
    [DisplayName("포크 OBP")] public double? ForkOBP { get; init; }
    [DisplayName("포크 SLG")] public double? ForkSLG { get; init; }
    [DisplayName("포크 OPS")] public double? ForkOPS { get; init; }
    [DisplayName("너클 AVG")] public double? KnuckleAVG { get; init; }
    [DisplayName("너클 OBP")] public double? KnuckleOBP { get; init; }
    [DisplayName("너클 SLG")] public double? KnuckleSLG { get; init; }
    [DisplayName("너클 OPS")] public double? KnuckleOPS { get; init; }
    [DisplayName("기타 AVG")] public double? OtherAVG { get; init; }
    [DisplayName("기타 OBP")] public double? OtherOBP { get; init; }
    [DisplayName("기타 SLG")] public double? OtherSLG { get; init; }
    [DisplayName("기타 OPS")] public double? OtherOPS { get; init; }
}

public static class BatterPitchTypeMatrixFactory
{
    private sealed class Acc
    {
        public int PA, AB, H, BB, HBP, SF, TB;
        public void Add(BatterPitchTypeRecordRow x)
        {
            PA += x.PA; AB += x.AB; H += x.Hits; BB += x.Walks; HBP += x.HitByPitch; SF += x.SacrificeFlies;
            TB += x.TotalBasesRaw;
        }
        public double? AVG => Div(H, AB);
        public double? OBP => Div(H + BB + HBP, AB + BB + HBP + SF);
        public double? SLG => Div(TB, AB);
        public double? OPS => OBP.HasValue && SLG.HasValue ? OBP.Value + SLG.Value : null;
        private static double? Div(double a, double b) => Math.Abs(b) < 0.0000001 ? null : a / b;
    }

    public static IReadOnlyList<BatterPitchTypeMatrixRow> Build(IEnumerable<BatterPitchTypeRecordRow> source)
    {
        var groups = source
            .GroupBy(x => $"{x.Pcode ?? string.Empty}\u001f{x.TeamCode ?? string.Empty}", StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                var byType = new Dictionary<string, Acc>(StringComparer.Ordinal);
                var totalPa = 0;
                foreach (var x in g)
                {
                    totalPa += x.PA;
                    var bucket = Normalize(x.PitchType);
                    if (!byType.TryGetValue(bucket, out var acc)) byType[bucket] = acc = new Acc();
                    acc.Add(x);
                }
                Acc A(string name) => byType.TryGetValue(name, out var a) ? a : new Acc();
                var fb=A("직구"); var ts=A("투심"); var ct=A("커터"); var cv=A("커브"); var sl=A("슬라이더");
                var ch=A("체인지업"); var si=A("싱커"); var fk=A("포크"); var kn=A("너클"); var ot=A("기타");
                return (Row: new BatterPitchTypeMatrixRow
                {
                    Pcode=first.Pcode, Name=first.Name, TeamCode=first.TeamCode,
                    FastballAVG=fb.AVG, FastballOBP=fb.OBP, FastballSLG=fb.SLG, FastballOPS=fb.OPS,
                    TwoSeamAVG=ts.AVG, TwoSeamOBP=ts.OBP, TwoSeamSLG=ts.SLG, TwoSeamOPS=ts.OPS,
                    CutterAVG=ct.AVG, CutterOBP=ct.OBP, CutterSLG=ct.SLG, CutterOPS=ct.OPS,
                    CurveAVG=cv.AVG, CurveOBP=cv.OBP, CurveSLG=cv.SLG, CurveOPS=cv.OPS,
                    SliderAVG=sl.AVG, SliderOBP=sl.OBP, SliderSLG=sl.SLG, SliderOPS=sl.OPS,
                    ChangeupAVG=ch.AVG, ChangeupOBP=ch.OBP, ChangeupSLG=ch.SLG, ChangeupOPS=ch.OPS,
                    SinkerAVG=si.AVG, SinkerOBP=si.OBP, SinkerSLG=si.SLG, SinkerOPS=si.OPS,
                    ForkAVG=fk.AVG, ForkOBP=fk.OBP, ForkSLG=fk.SLG, ForkOPS=fk.OPS,
                    KnuckleAVG=kn.AVG, KnuckleOBP=kn.OBP, KnuckleSLG=kn.SLG, KnuckleOPS=kn.OPS,
                    OtherAVG=ot.AVG, OtherOBP=ot.OBP, OtherSLG=ot.SLG, OtherOPS=ot.OPS,
                }, TotalPa: totalPa);
            })
            .OrderByDescending(x => x.TotalPa)
            .ThenBy(x => x.Row.Name, StringComparer.CurrentCulture)
            .ToList();
        for (var i=0;i<groups.Count;i++) groups[i].Row.Rank=i+1;
        return groups.Select(x=>x.Row).ToList();
    }

    private static string Normalize(string? value)
    {
        var s=(value ?? string.Empty).Trim();
        var lower=s.ToLowerInvariant();
        if (s.Contains("투심", StringComparison.Ordinal)) return "투심";
        if (s.Contains("포심", StringComparison.Ordinal) || s.Contains("직구", StringComparison.Ordinal) || lower.Contains("four") || lower.Contains("4-seam")) return "직구";
        if (s.Contains("커터", StringComparison.Ordinal) || s.Contains("컷", StringComparison.Ordinal)) return "커터";
        if (s.Contains("커브", StringComparison.Ordinal)) return "커브";
        if (s.Contains("슬라이더", StringComparison.Ordinal) || s.Contains("슬라", StringComparison.Ordinal)) return "슬라이더";
        if (s.Contains("체인지", StringComparison.Ordinal)) return "체인지업";
        if (s.Contains("싱커", StringComparison.Ordinal)) return "싱커";
        if (s.Contains("포크", StringComparison.Ordinal) || s.Contains("스플리터", StringComparison.Ordinal) || s.Contains("스플릿", StringComparison.Ordinal)) return "포크";
        if (s.Contains("너클", StringComparison.Ordinal)) return "너클";
        return "기타";
    }
}

public static class RecordRoomRowFactory
{
    public static IReadOnlyList<BatterBasicRecordRow> BuildBasic(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var saber = snapshot.BatterSabermetrics.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var values = snapshot.BatterValues.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterClassic
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                saber.TryGetValue(Key(row.Pcode, row.TeamCode), out var saberRow);
                values.TryGetValue(Key(row.Pcode, row.TeamCode), out var valueRow);
                var effectivePa = Math.Max(0, row.PA - row.IntentionalWalks - row.SacrificeBunts);
                return new BatterBasicRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    PrimaryPosition = valueRow?.PrimaryPosition ?? "-",
                    Games = row.Games,
                    War = valueRow?.War,
                    PA = row.PA,
                    EffectivePA = effectivePa,
                    AB = row.AB,
                    Runs = row.Runs,
                    Hits = row.H,
                    Doubles = row.Doubles,
                    Triples = row.Triples,
                    HomeRuns = row.HomeRuns,
                    TotalBases = row.TotalBases,
                    RunsBattedIn = row.RBI,
                    StolenBases = row.StolenBases,
                    CaughtStealing = row.CaughtStealing,
                    Walks = row.Walks,
                    HitByPitch = row.HitByPitch,
                    IntentionalWalks = row.IntentionalWalks,
                    Strikeouts = row.Strikeouts,
                    DoublePlays = row.DoublePlays,
                    SacrificeBunts = row.SacrificeBunts,
                    SacrificeFlies = row.SacrificeFlies,
                    AVG = row.AVG,
                    OBP = row.OBP,
                    SLG = row.SLG,
                    OPS = row.OPS,
                    RunsPerEffectivePa = effectivePa > 0 ? (saberRow?.Wraa ?? 0.0) / effectivePa : null,
                    WrcPlus = saberRow?.WrcPlus,
                };
            })
            .OrderByDescending(row => row.War ?? double.MinValue)
            .ThenByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterAdvancedRecordRow> BuildAdvanced(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var classic = snapshot.BatterClassic.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterSabermetrics
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                classic.TryGetValue(Key(row.Pcode, row.TeamCode), out var classicRow);
                var effectivePa = Math.Max(0, row.PA - (classicRow?.IntentionalWalks ?? 0) - (classicRow?.SacrificeBunts ?? 0));
                return new BatterAdvancedRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = row.Games,
                    PA = row.PA,
                    StrikeoutRate = row.StrikeoutRate,
                    WalkRate = row.WalkRate,
                    WalkToStrikeout = row.WalkToStrikeout,
                    BABIP = row.Babip,
                    IsoP = row.Iso,
                    IsoD = classicRow?.OBP.HasValue == true && classicRow.AVG.HasValue
                        ? classicRow.OBP.Value - classicRow.AVG.Value
                        : null,
                    RunsPerEffectivePa = effectivePa > 0 ? (row.Wraa ?? 0.0) / effectivePa : null,
                    Woba = row.Woba,
                    Wrc = row.Wrc,
                    RunsCreated27 = CalculateRunsCreated27(classicRow),
                    WrcPlus = row.WrcPlus,
                    OpsPlus = row.OpsPlus,
                };
            })
            .OrderByDescending(row => row.WrcPlus ?? double.MinValue)
            .ThenByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterValueRecordRow> BuildValue(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var rows = snapshot.BatterValues
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row => new BatterValueRecordRow
            {
                Pcode = row.Pcode,
                Name = row.Name,
                TeamCode = row.TeamCode,
                Games = row.Games,
                PA = row.PA,
                BattingRuns = row.BattingRuns,
                RunningRuns = row.RunningRuns,
                PositionRuns = row.PositionRuns,
                OffensiveRuns = (row.BattingRuns ?? 0.0) + (row.RunningRuns ?? 0.0) + (row.PositionRuns ?? 0.0),
                ReplacementRuns = row.ReplacementRuns,
                RunsAboveReplacement = row.RunsAboveReplacement,
                RunsPerWin = row.RunsPerWin,
                War = row.War,
            })
            .OrderByDescending(row => row.War ?? double.MinValue)
            .ThenByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterExtendedRecordRow> BuildExtended(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var saber = snapshot.BatterSabermetrics.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterClassic
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                saber.TryGetValue(Key(row.Pcode, row.TeamCode), out var saberRow);
                var runsCreated = CalculateRunsCreated(row);
                return new BatterExtendedRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = row.Games,
                    PA = row.PA,
                    PowerSpeedNumber = row.HomeRuns + row.StolenBases > 0
                        ? 2.0 * row.HomeRuns * row.StolenBases / (row.HomeRuns + row.StolenBases)
                        : null,
                    TotalAverage = Divide(
                        row.TotalBases + row.Walks + row.HitByPitch + row.StolenBases - row.CaughtStealing,
                        row.AB - row.H + row.CaughtStealing + row.DoublePlays),
                    SecondaryAverage = Divide(
                        row.Walks + row.TotalBases - row.H + row.StolenBases - row.CaughtStealing,
                        row.AB),
                    RunsCreated = runsCreated,
                    RunsCreated27 = CalculateRunsCreated27(row),
                    WalkToStrikeout = saberRow?.WalkToStrikeout,
                    IsoP = saberRow?.Iso,
                };
            })
            .OrderByDescending(row => row.TotalAverage ?? double.MinValue)
            .ThenByDescending(row => row.PA)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterPowerRecordRow> BuildPower(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var saber = snapshot.BatterSabermetrics.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterClassic
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                saber.TryGetValue(Key(row.Pcode, row.TeamCode), out var saberRow);
                var xbh = row.Doubles + row.Triples + row.HomeRuns;
                return new BatterPowerRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = row.Games,
                    PA = row.PA,
                    HomeRuns = row.HomeRuns,
                    ExtraBaseHits = xbh,
                    ExtraBaseHitRate = Divide(xbh, row.H),
                    HomeRunPerExtraBaseHit = Divide(row.HomeRuns, xbh),
                    PlateAppearancesPerHomeRun = Divide(row.PA, row.HomeRuns),
                    AtBatsPerHomeRun = Divide(row.AB, row.HomeRuns),
                    IsoP = saberRow?.Iso,
                    SluggingToAverage = row.AVG.HasValue && row.AVG.Value > 0 && row.SLG.HasValue
                        ? row.SLG.Value / row.AVG.Value
                        : null,
                };
            })
            .OrderByDescending(row => row.IsoP ?? double.MinValue)
            .ThenByDescending(row => row.HomeRuns)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterTeamBattingRecordRow> BuildTeamBatting(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var rows = snapshot.BatterClassic
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row => new BatterTeamBattingRecordRow
            {
                Pcode = row.Pcode,
                Name = row.Name,
                TeamCode = row.TeamCode,
                Games = row.Games,
                PA = row.PA,
                Runs = row.Runs,
                Hits = row.H,
                HomeRuns = row.HomeRuns,
                TotalBases = row.TotalBases,
                RunsBattedIn = row.RBI,
                Walks = row.Walks,
                HitByPitch = row.HitByPitch,
                Strikeouts = row.Strikeouts,
                DoublePlays = row.DoublePlays,
                SacrificeBunts = row.SacrificeBunts,
                SacrificeFlies = row.SacrificeFlies,
                RbiPerPa = Divide(row.RBI, row.PA),
                RunsPerPa = Divide(row.Runs, row.PA),
            })
            .OrderByDescending(row => row.RunsBattedIn)
            .ThenByDescending(row => row.Runs)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterStealRecordRow> BuildSteal(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var values = snapshot.BatterValues.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterClassic
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                values.TryGetValue(Key(row.Pcode, row.TeamCode), out var valueRow);
                var attempts = row.StolenBases + row.CaughtStealing;
                return new BatterStealRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = row.Games,
                    PA = row.PA,
                    StolenBases = row.StolenBases,
                    CaughtStealing = row.CaughtStealing,
                    Attempts = attempts,
                    SuccessRate = Divide(row.StolenBases, attempts),
                    RunningRuns = valueRow?.RunningRuns,
                };
            })
            .OrderByDescending(row => row.RunningRuns ?? double.MinValue)
            .ThenByDescending(row => row.StolenBases)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterBaserunningRecordRow> BuildBaserunning(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var classic = snapshot.BatterClassic.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterValues
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                classic.TryGetValue(Key(row.Pcode, row.TeamCode), out var classicRow);
                return new BatterBaserunningRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = row.Games,
                    Runs = classicRow?.Runs ?? 0,
                    StolenBases = classicRow?.StolenBases ?? 0,
                    CaughtStealing = classicRow?.CaughtStealing ?? 0,
                    RunningRuns = row.RunningRuns,
                    RunsPerGame = Divide(classicRow?.Runs ?? 0, row.Games),
                };
            })
            .OrderByDescending(row => row.RunningRuns ?? double.MinValue)
            .ThenByDescending(row => row.Runs)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<BatterPitchProfileRecordRow> BuildPitchProfile(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var classic = snapshot.BatterClassic.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.BatterDiscipline
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                classic.TryGetValue(Key(row.Pcode, row.TeamCode), out var classicRow);
                return new BatterPitchProfileRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    PA = classicRow?.PA ?? 0,
                    Pitches = row.Pitches,
                    PitchesPerPa = row.PitchesPerPa,
                    SwingRate = row.SwingRate,
                    ContactRate = row.ContactRate,
                    WhiffRate = row.WhiffRate,
                    CswRate = row.CswRate,
                    ZoneSwingRate = row.ZoneSwingRate,
                    ChaseRate = row.ChaseRate,
                    ZoneContactRate = row.ZoneContactRate,
                    OutZoneContactRate = row.OutZoneContactRate,
                    FirstPitchSwingRate = row.FirstPitchSwingRate,
                };
            })
            .OrderByDescending(row => row.CswRate ?? double.MinValue)
            .ThenByDescending(row => row.Pitches)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static string Key(string? pcode, string? teamCode) => $"{pcode ?? string.Empty}|{teamCode ?? string.Empty}";

    private static bool IsEligible(string? pcode, string? teamCode, IReadOnlySet<string>? eligibleKeys) =>
        eligibleKeys is null || eligibleKeys.Contains(Key(pcode, teamCode));

    private static double? CalculateRunsCreated(BatterSummaryGridRow row)
    {
        var denominator = row.AB + row.Walks + row.HitByPitch;
        return denominator > 0
            ? (row.H + row.Walks + row.HitByPitch) * row.TotalBases / (double)denominator
            : null;
    }

    private static double? CalculateRunsCreated27(BatterSummaryGridRow? row)
    {
        if (row is null) return null;
        var runsCreated = CalculateRunsCreated(row);
        var outs = row.AB - row.H + row.CaughtStealing + row.DoublePlays + row.SacrificeBunts + row.SacrificeFlies;
        return runsCreated.HasValue && outs > 0 ? runsCreated.Value * 27.0 / outs : null;
    }

    private static double? Divide(double numerator, double denominator) =>
        Math.Abs(denominator) < 0.0000001 ? null : numerator / denominator;

    private static void ApplyRanks<T>(IReadOnlyList<T> rows) where T : class
    {
        var rankProperty = typeof(T).GetProperty(nameof(BatterBasicRecordRow.Rank));
        if (rankProperty is null || !rankProperty.CanWrite) return;
        for (var index = 0; index < rows.Count; index++)
            rankProperty.SetValue(rows[index], index + 1);
    }
}
