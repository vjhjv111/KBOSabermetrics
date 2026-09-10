using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class ParkFactorDiagnosticSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구장 수")] public int Stadiums { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("리그 R/G")] public double? LeagueRunsPerGame { get; init; }
    [DisplayName("FIP PF 가중평균")] public double? WeightedFipParkFactor { get; init; }
    [DisplayName("FIP PF 최소")] public double? MinFipParkFactor { get; init; }
    [DisplayName("FIP PF P10")] public double? P10FipParkFactor { get; init; }
    [DisplayName("FIP PF 중앙값")] public double? MedianFipParkFactor { get; init; }
    [DisplayName("FIP PF P90")] public double? P90FipParkFactor { get; init; }
    [DisplayName("FIP PF 최대")] public double? MaxFipParkFactor { get; init; }
    [DisplayName("득점 PF 가중평균")] public double? WeightedRunParkFactor { get; init; }
    [DisplayName("득점 PF 최소")] public double? MinRunParkFactor { get; init; }
    [DisplayName("득점 PF 중앙값")] public double? MedianRunParkFactor { get; init; }
    [DisplayName("득점 PF 최대")] public double? MaxRunParkFactor { get; init; }
    [DisplayName("현재 사용 PF 가중평균")] public double? WeightedUsedParkFactor { get; init; }
    [DisplayName("|FIP PF-100| 평균")] public double? MeanAbsoluteFipDeviation { get; init; }
    [DisplayName("PF<80 구장")] public int Under80Count { get; init; }
    [DisplayName("PF>120 구장")] public int Over120Count { get; init; }
}

public sealed class ParkFactorDiagnosticStadiumRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구장")] public string Stadium { get; init; } = string.Empty;
    [DisplayName("주 홈팀")] public string HomeClub { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double Innings { get; init; }
    [DisplayName("R")] public int Runs { get; init; }
    [DisplayName("ER")] public int EarnedRuns { get; init; }
    [DisplayName("R/G")] public double? RunsPerGame { get; init; }
    [DisplayName("RA9")] public double? Ra9 { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitBatters { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("IFFB")] public int InfieldFlies { get; init; }
    [DisplayName("구장 FIP 성분/9")] public double? StadiumFipComponent { get; init; }
    [DisplayName("타구장 FIP 성분/9")] public double? OtherParksFipComponent { get; init; }
    [DisplayName("연도별 FIP PF")] public double? AnnualFipParkFactor { get; init; }
    [DisplayName("현재 사용 FIP PF")] public double? CurrentUsedFipParkFactor { get; init; }
    [DisplayName("사용PF-연도PF")] public double? UsedMinusAnnual { get; init; }
    [DisplayName("홈 R/G")] public double? HomeRunsPerGame { get; init; }
    [DisplayName("홈팀 원정 R/G")] public double? HomeClubRoadRunsPerGame { get; init; }
    [DisplayName("단순 득점 PF")] public double? SimpleRunParkFactor { get; init; }
    [DisplayName("FIP PF-득점 PF")] public double? FipMinusRunParkFactor { get; init; }
    [DisplayName("표본 경고")] public string Warning { get; init; } = string.Empty;
}

public sealed class ParkFactorDiagnosticBundle
{
    public IReadOnlyList<ParkFactorDiagnosticSeasonRow> Seasons { get; init; } = Array.Empty<ParkFactorDiagnosticSeasonRow>();
    public IReadOnlyList<ParkFactorDiagnosticStadiumRow> Stadiums { get; init; } = Array.Empty<ParkFactorDiagnosticStadiumRow>();
}
