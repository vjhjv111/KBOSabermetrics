using System.ComponentModel;

namespace NaverRelay.Application.Defense;

/// <summary>Diagnostic policy, NOT a replacement for Statcast OAA, DRS or UZR.</summary>
public sealed record DefensePolicy
{
    public const string Version = "fanzai-defense-v1.0";
    public int MinimumReferencePlays { get; init; } = 40;
    public int MinimumOutcomePlays { get; init; } = 5;
    public int MinimumReStates { get; init; } = 20;
    public double PriorPlays { get; init; } = 20;
}

public sealed class DefenseRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("Year")] public int? Year { get; init; }
    [DisplayName("선수 코드")] public string Pcode { get; init; } = "";
    [DisplayName("Name")] public string Name { get; init; } = "";
    [DisplayName("Team")] public string TeamCode { get; init; } = "";
    [DisplayName("Pos.")] public string Position { get; init; } = "";
    [DisplayName("수비 G")] public int Games { get; init; }
    [DisplayName("Def Inn")] public double DefensiveInnings { get; init; }
    [DisplayName("기회 가중수")] public double Opportunities { get; init; }
    [DisplayName("처리 가중수")] public double Conversions { get; init; }
    [DisplayName("DER")] public double? DER { get; init; }
    [DisplayName("Zone 기회")] public double ZoneOpportunities { get; init; }
    [DisplayName("ZCR")] public double? ZCR { get; init; }
    [DisplayName("OOA 표본")] public double ModelOpportunities { get; init; }
    [DisplayName("기대 처리수")] public double? ExpectedOuts { get; init; }
    [DisplayName("OOA-Lite")] public double? OoaLite { get; init; }
    [DisplayName("RR 표본")] public double RunValueOpportunities { get; init; }
    [DisplayName("Range Runs")] public double? RangeRuns { get; init; }
    [DisplayName("중심화 보정")] public double? CenteringRuns { get; init; }
    [DisplayName("Defensive Runs")] public double? DefensiveRuns { get; init; }
    [DisplayName("모델 적용률")] public double? ModelCoverage { get; init; }
    [DisplayName("진단")] public string Status { get; init; } = "";
}

public sealed class DefenseAuditRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("경기 ID")] public string GameId { get; init; } = "";
    [DisplayName("Team")] public string TeamCode { get; init; } = "";
    [DisplayName("분류")] public string Code { get; init; } = "";
    [DisplayName("건수")] public int Count { get; set; }
    [DisplayName("설명")] public string Description { get; init; } = "";
}

public sealed record DefensePlayer(string Pcode, string Name, string Position, int Order = 0, int Sequence = 1);
public sealed record DefenseChange(int Order, int Inning, string BattingTeam, string Team,
    int? BeforeOuts, string OutCode, string InCode, string InName, string OldPosition,
    string NewPosition, bool IsShift, bool Parsed = true);

/// <summary>Only real recorded state is used. Missing state is null, not an empty base configuration.</summary>
public sealed record DefensePlay
{
    public string Id { get; init; } = "";
    public int Inning { get; init; }
    public string BattingTeam { get; init; } = "";
    public string FieldingTeam { get; init; } = "";
    public int? StartOrder { get; init; }
    public int? ResultOrder { get; init; }
    public bool SyntheticOrder { get; init; }
    public int? BeforeOuts { get; init; }
    public int? BeforeBases { get; init; }
    public int? BeforeHomeScore { get; init; }
    public int? BeforeAwayScore { get; init; }
    public int? AfterHomeScore { get; init; }
    public int? AfterAwayScore { get; init; }
    public int ResultType { get; init; }
    public int BallType { get; init; }
    public bool IsHit { get; init; }
    public bool IsBatterOut { get; init; }
    public int OutsRecorded { get; init; }
    public string Text { get; init; } = "";
    public string BatterHand { get; init; } = "";
}

public sealed record DefenseLooseRunnerEvent(int Order, int Inning, string BattingTeam, bool IsOut, int Reason);
public sealed class DefenseGameInput
{
    public string GameId { get; init; } = "";
    public int Year { get; init; }
    public string Date { get; init; } = "";
    public string Stadium { get; init; } = "";
    public string HomeTeam { get; init; } = "";
    public string AwayTeam { get; init; } = "";
    public int? HomeScore { get; init; }
    public int? AwayScore { get; init; }
    public bool Completed { get; init; }
    public Dictionary<string, int> PitchingOuts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<DefensePlayer>> Starters { get; init; } = new(StringComparer.Ordinal);
    public List<DefenseChange> Changes { get; init; } = new();
    public List<DefensePlay> Plays { get; init; } = new();
    public List<DefenseLooseRunnerEvent> LooseRunnerEvents { get; init; } = new();
    public List<string> InputWarnings { get; init; } = new();
}

public sealed class DefenseSeasonSnapshot
{
    public string ModelVersion { get; init; } = DefensePolicy.Version;
    public int Year { get; init; }
    public List<DefenseGameResult> Games { get; init; } = new();
}

public sealed class DefenseGameResult
{
    public string GameId { get; init; } = "";
    public int Year { get; init; }
    public string Date { get; init; } = "";
    public string Stadium { get; init; } = "";
    public string HomeTeam { get; init; } = "";
    public string AwayTeam { get; init; } = "";
    public List<DefenseContribution> Players { get; init; } = new();
    public List<DefenseTeamResult> Teams { get; init; } = new();
    public List<DefenseAuditRow> Audit { get; init; } = new();
}

public sealed class DefenseTeamResult
{
    public string TeamCode { get; init; } = "";
    public int Outs { get; set; }
    public int FairBalls { get; set; }
    public int ConvertedBalls { get; set; }
    public int LocatedBalls { get; set; }
    public int UnmappedBalls { get; set; }
}

/// <summary>Outs and counts remain additive. Unknown performance is represented by zero sample, not zero value.</summary>
public sealed class DefenseContribution
{
    public string Pcode { get; init; } = "";
    public string Name { get; init; } = "";
    public string TeamCode { get; init; } = "";
    public string Position { get; init; } = "";
    public int Outs { get; set; }
    public double Opportunities { get; set; }
    public double Conversions { get; set; }
    public double ZoneOpportunities { get; set; }
    public double ZoneConversions { get; set; }
    public double ModelOpportunities { get; set; }
    public double ExpectedOuts { get; set; }
    public double OoaLite { get; set; }
    public double RunValueOpportunities { get; set; }
    public double RangeRuns { get; set; }
    public double CenteringRuns { get; set; }
    public double DefensiveRuns => RangeRuns + CenteringRuns;
}
