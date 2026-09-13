namespace NaverSabermetrics.Web;

public sealed partial class AnalysisWebService
{
    private async Task<AnalysisResult> VelocityAsync(AnalysisRequest r, CancellationToken ct)
    {
        if (r.Code == "") return Selection(r, "구속별 대응");

        // OutcomeSource finds the global final pitch before type/stance/count or speed filters.
        // Every observed pitch can contribute to swing rates, but only that final pitch owns a PA.
        var prefix = OutcomeSource(r) + $"""
            , filtered AS (
              SELECT p.*,CASE WHEN p.SpeedKmh BETWEEN 40 AND 180 THEN 1 ELSE 0 END ValidSpeed
              FROM source p WHERE {PitchFilter}
            ), binned AS (
              SELECT *,CASE WHEN SpeedKmh<120 THEN 0 WHEN SpeedKmh<135 THEN 1
                WHEN SpeedKmh<150 THEN 2 ELSE 3 END Band
              FROM filtered WHERE ValidSpeed=1
            )
            """;

        var coverage = (await SqlAsync(prefix + $"""
            SELECT COUNT(*) Pitches,
              COALESCE(SUM(ValidSpeed),0) SpeedPitches,
              COALESCE(SUM(SpeedKmh IS NULL),0) MissingSpeedPitches,
              COALESCE(SUM(SpeedKmh IS NOT NULL AND ValidSpeed=0),0) InvalidSpeedPitches,
              COALESCE(SUM(Terminal),0) TerminalPA,
              COALESCE(SUM(Terminal=1 AND ValidSpeed=1),0) SpeedPA,
              COALESCE(SUM(Terminal=1 AND SpeedKmh IS NULL),0) MissingSpeedPA,
              COALESCE(SUM(Terminal=1 AND SpeedKmh IS NOT NULL AND ValidSpeed=0),0) InvalidSpeedPA,
              COALESCE(SUM(Terminal=1 AND ValidSpeed=1 AND PaKnown=1),0) OutcomePA,
              COALESCE(SUM(Terminal=1 AND ValidSpeed=1 AND COALESCE(PaKnown,0)<>1),0) UnknownOutcomePA,
              (SELECT COUNT(*) FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId
                WHERE {PaScope(r)} AND NOT EXISTS (
                  SELECT 1 FROM Pitches unfiltered WHERE unfiltered.PlateAppearanceId=pa.PlateAppearanceId)) NoPitchPA
            FROM filtered
            """, r, ct))[0];

        var rows = await SqlAsync(prefix + """
            , bins(Band,Label) AS (
              VALUES (0,'120 미만'),(1,'120 이상 135 미만'),(2,'135 이상 150 미만'),(3,'150 이상')
            ), aggregates AS (
              SELECT Band,COUNT(*) N,SUM(IsSwing) Swings,SUM(IsWhiff) Whiffs,SUM(Terminal) PA,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN 1 ELSE 0 END) OutcomePA,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN PaAB ELSE 0 END) AB,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN PaH ELSE 0 END) H,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN PaTB ELSE 0 END) TB,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN PaBB ELSE 0 END) BB,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN PaHBP ELSE 0 END) HBP,
                SUM(CASE WHEN Terminal=1 AND PaKnown=1 THEN PaSF ELSE 0 END) SF
              FROM binned GROUP BY Band
            ), stats AS (
              SELECT b.Band,b.Label,COALESCE(a.N,0) N,COALESCE(a.Swings,0) Swings,
                COALESCE(a.Whiffs,0) Whiffs,COALESCE(a.PA,0) PA,COALESCE(a.OutcomePA,0) OutcomePA,
                COALESCE(a.AB,0) AB,COALESCE(a.H,0) H,COALESCE(a.TB,0) TB,COALESCE(a.BB,0) BB,
                COALESCE(a.HBP,0) HBP,COALESCE(a.SF,0) SF
              FROM bins b LEFT JOIN aggregates a ON a.Band=b.Band
            ), rates AS (
              SELECT *,PA-OutcomePA UnknownOutcomePA,AB+BB+HBP+SF ObpN,
                1.0*H/NULLIF(AB,0) AVG,1.0*(H+BB+HBP)/NULLIF(AB+BB+HBP+SF,0) OBP,
                1.0*TB/NULLIF(AB,0) SLG,100.0*Swings/NULLIF(N,0) SwingPct,
                100.0*Whiffs/NULLIF(Swings,0) WhiffPct
              FROM stats
            )
            SELECT *,OBP+SLG OPS FROM rates ORDER BY Band
            """, r, ct);

        return new(r.Section, "구속별 대응", [SeasonNote,
            "구속은 수집된 투구 구속 관측값이며 40~180km/h만 사용합니다. 구간은 120 미만, 120 이상 135 미만, 135 이상 150 미만, 150 이상입니다. 180km/h는 포함합니다.",
            "구종·타자 좌우·투구 전 카운트는 개별 투구에 적용합니다. 타석 결과는 조건을 적용하기 전 확인한 실제 마지막 투구가 조건을 만족할 때만 해당 공의 구속 구간에 한 번 배정합니다. 마지막 공의 구속이 없거나 범위를 벗어나면 앞선 공으로 대체하지 않습니다.",
            r.Role == "pitcher" ? "투수 화면은 마지막 투구 기준 상대 타자 성적입니다. 공식 기록의 투수 책임 배분과 다를 수 있습니다." : "타격 결과는 구속이 확인된 마지막 투구로 끝난 공식 완료 타석의 성적입니다. 모든 시즌 타석을 포함한 공식 타격 성적과 다를 수 있습니다.",
            "AVG=H/AB, OBP=(H+BB+HBP)/(AB+BB+HBP+SF), SLG=TB/AB, OPS=OBP+SLG입니다. BB는 고의4구를 한 번 포함합니다. 결과 미분류 PA는 PA에는 세고 결과 확인 PA·AB·비율 계산에서는 제외합니다.",
            "스윙률=스윙/유효 구속 투구 N, 헛스윙률=헛스윙/스윙입니다. 타석에 연결되지 않은 투구도 투구 지표에 포함합니다. 결과 확인 PA 30 미만 표시는 작은 표본을 알리는 안내이며 통계적 유의성 기준이 아닙니다.",
            "무투구 PA는 시즌·날짜·소속 팀 범위의 공식 완료 타석 중 연결된 투구가 없는 건수입니다. 구종·타자 좌우·카운트는 확인할 수 없어 이 건수에 적용하지 않으며 구속 구간에도 배정하지 않습니다.", SampleNote],
            Metrics(coverage, ("Pitches", "조건 내 투구"), ("SpeedPitches", "유효 구속 투구"),
                ("MissingSpeedPitches", "구속 누락 투구"), ("InvalidSpeedPitches", "구속 범위 밖 투구"),
                ("SpeedPA", "구속 분류 PA"), ("OutcomePA", "결과 확인 PA"),
                ("UnknownOutcomePA", "결과 미분류 PA"), ("NoPitchPA", "무투구 PA · 투구조건 전")),
            [new("velocityBands", "구속 구간별 결과 · km/h", [C("Label", "구속 구간", "text"), C("N", "투구 N"),
                C("PA", "종료 PA"), C("OutcomePA", "결과 확인 PA"), C("AB", "AB"), C("ObpN", "OBP 분모"),
                C("AVG", "AVG", "rate"), C("OBP", "OBP", "rate"), C("SLG", "SLG", "rate"), C("OPS", "OPS", "rate"),
                C("Swings", "스윙"), C("Whiffs", "헛스윙"), C("SwingPct", "스윙%", "percent"), C("WhiffPct", "헛스윙%", "percent"),
                C("H", "H"), C("TB", "TB"), C("BB", "BB · IBB 포함"), C("HBP", "HBP"), C("SF", "SF"), C("UnknownOutcomePA", "결과 미분류 PA")], rows),
             new("velocityCoverage", "구속 자료 포함·제외 내역", [C("Pitches", "조건 내 투구"), C("SpeedPitches", "유효 구속 투구"),
                C("MissingSpeedPitches", "구속 누락 투구"), C("InvalidSpeedPitches", "구속 범위 밖 투구"),
                C("TerminalPA", "조건 내 종료 PA"), C("SpeedPA", "구속 분류 PA"), C("MissingSpeedPA", "종료구 구속 누락 PA"),
                C("InvalidSpeedPA", "종료구 구속 범위 밖 PA"), C("OutcomePA", "결과 확인 PA"), C("UnknownOutcomePA", "결과 미분류 PA"),
                C("NoPitchPA", "무투구 PA · 투구조건 전")], [coverage])],
            Chart: new { role = r.Role, unit = "km/h", smallSamplePa = 30 });
    }
}
