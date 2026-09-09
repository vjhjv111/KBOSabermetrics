using System.Collections.Generic;

namespace NaverRelay.Parsing
{
    public enum GameCompetitionType
    {
        RegularSeason,
        Preseason,
        Postseason,
        AllStar,
        Futures,
        Other,
    }

    public sealed class NormalizedGame
    {
        public string GameId { get; set; } = string.Empty;
        public int? SeasonYear { get; set; }
        public string? SuperCategoryId { get; set; }
        public string? UpperCategoryId { get; set; }
        public string? UpperCategoryName { get; set; }
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? RoundCode { get; set; }
        public GameCompetitionType CompetitionType { get; set; } = GameCompetitionType.Other;
        public bool IsRegularSeason =>
            string.Equals(RoundCode?.Trim(), "kbo_r", System.StringComparison.OrdinalIgnoreCase);
        public string? GameDate { get; set; }
        public string? GameDateTime { get; set; }
        public string? Stadium { get; set; }
        public string? StatusCode { get; set; }
        public string? Winner { get; set; }
        public TeamMetadata AwayTeam { get; set; } = new();
        public TeamMetadata HomeTeam { get; set; } = new();
        public GameStateSnapshot? FinalRelayState { get; set; }
        public double? LastValidHomeWinRate { get; set; }
        public double? LastValidAwayWinRate { get; set; }
        public double? LastValidWpaByPlate { get; set; }

        public List<RelayGroup> RelayGroups { get; set; } = new();
        public List<NormalizedEvent> Events { get; set; } = new();
        public List<PlateAppearance> PlateAppearances { get; set; } = new();
        public List<PitchEvent> PitchEvents { get; set; } = new();
        public List<RunnerEvent> RunnerEvents { get; set; } = new();
        public List<PlayerChangeEvent> PlayerChanges { get; set; } = new();
        public List<AdministrativeEvent> AdministrativeEvents { get; set; } = new();
        public List<GamePlayerBattingLine> BattingLines { get; set; } = new();
        public List<GamePlayerPitchingLine> PitchingLines { get; set; } = new();
        public List<ParserDiagnostic> Diagnostics { get; set; } = new();
        public ParserSummary Summary { get; set; } = new();
    }

    public sealed class TeamMetadata
    {
        public TeamSide Side { get; set; } = TeamSide.Unknown;
        public string? TeamCode { get; set; }
        public string? TeamName { get; set; }
        public int? FinalScore { get; set; }
        public int? FinalHits { get; set; }
        public int? FinalErrors { get; set; }
        public int? FinalWalks { get; set; }
    }

    public sealed class PlayerIdentity
    {
        public string? Pcode { get; set; }
        public string? Name { get; set; }
        public TeamSide TeamSide { get; set; } = TeamSide.Unknown;
        public string? TeamCode { get; set; }
        public int? BatOrder { get; set; }
        public int? LineupSequence { get; set; }
        public string? Position { get; set; }
        public string? HitType { get; set; }
        public string? PitchingStyle { get; set; }
    }

    public sealed class GameStateSnapshot
    {
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public int? HomeHits { get; set; }
        public int? AwayHits { get; set; }
        public int? HomeWalks { get; set; }
        public int? AwayWalks { get; set; }
        public int? HomeErrors { get; set; }
        public int? AwayErrors { get; set; }
        public string? PitcherPcode { get; set; }
        public string? PitcherName { get; set; }
        public string? BatterPcode { get; set; }
        public string? BatterName { get; set; }
        public int? Balls { get; set; }
        public int? Strikes { get; set; }
        public int? Outs { get; set; }
        public int? FirstBaseSlot { get; set; }
        public int? SecondBaseSlot { get; set; }
        public int? ThirdBaseSlot { get; set; }
        public string? FirstBaseRunnerPcode { get; set; }
        public string? FirstBaseRunnerName { get; set; }
        public string? SecondBaseRunnerPcode { get; set; }
        public string? SecondBaseRunnerName { get; set; }
        public string? ThirdBaseRunnerPcode { get; set; }
        public string? ThirdBaseRunnerName { get; set; }
    }

    public sealed class RelayGroup
    {
        public string RelayGroupId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public int ChronologicalIndex { get; set; }
        public int? SourceRelayNo { get; set; }
        public string? Title { get; set; }
        public string? TitleStyle { get; set; }
        public int? Inning { get; set; }
        public string? RawHomeOrAway { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public string? BattingTeamCode { get; set; }
        public int? SourceStatusCode { get; set; }
        public RelayGroupType GroupType { get; set; }
        public string? PlateAppearanceId { get; set; }
        public GameStateSnapshot? StateBefore { get; set; }
        public GameStateSnapshot? StateAfter { get; set; }
        public double? HomeWinRateAfter { get; set; }
        public double? AwayWinRateAfter { get; set; }
        public double? WpaByPlate { get; set; }
        public List<string> EventIds { get; set; } = new();
    }

    public sealed class NormalizedEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string RelayGroupId { get; set; } = string.Empty;
        public string? PlateAppearanceId { get; set; }
        public int ChronologicalIndex { get; set; }
        public int? SourceRelayNo { get; set; }
        public int SourceOptionIndex { get; set; }
        public int? SourceSeqNo { get; set; }
        public bool IsDuplicateSourceSeqNo { get; set; }
        public int? RawType { get; set; }
        public NormalizedEventType EventType { get; set; }
        public string? RawText { get; set; }
        public string? RawStuff { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public int? Inning { get; set; }
        public GameStateSnapshot? StateBefore { get; set; }
        public GameStateSnapshot? StateAfter { get; set; }
    }

    public sealed class PlateAppearance
    {
        public string PlateAppearanceId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string RelayGroupId { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public int? OfficialSequenceNumber { get; set; }
        public int? SourceRelayNo { get; set; }
        public int? Inning { get; set; }
        public string? RawHomeOrAway { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public string? BattingTeamCode { get; set; }
        public string? FieldingTeamCode { get; set; }
        public PlateAppearanceStatus Status { get; set; }
        public bool IsOfficialPlateAppearance { get; set; }
        public string? StartEventId { get; set; }
        public string? ResultEventId { get; set; }
        public string? BatterPcode { get; set; }
        public string? BatterName { get; set; }
        public int? BatOrder { get; set; }
        public string? PitcherPcode { get; set; }
        public string? PitcherName { get; set; }
        public string? FinalPitcherPcode { get; set; }
        public string? FinalPitcherName { get; set; }
        public int? ResultRawType { get; set; }
        public string? ResultText { get; set; }
        public BattingOutcome Outcome { get; set; } = new();
        public GameStateSnapshot? StateBefore { get; set; }
        public GameStateSnapshot? StateAfter { get; set; }
        public int RunsScored { get; set; }
        public int OutsRecorded { get; set; }
        public int ActualPitchCount { get; set; }
        public int? MaximumDisplayPitchNumber { get; set; }
        public double? HomeWinRateAfter { get; set; }
        public double? AwayWinRateAfter { get; set; }
        public double? WpaByPlate { get; set; }
        public List<string> PitchEventIds { get; set; } = new();
        public List<string> RunnerEventIds { get; set; } = new();
        public List<string> PlayerChangeEventIds { get; set; } = new();
        public List<string> AdministrativeEventIds { get; set; } = new();
    }

    public sealed class BattingOutcome
    {
        public BattingResultType ResultType { get; set; } = BattingResultType.Unknown;
        public string? NormalizedResultText { get; set; }
        public BattedBallType BattedBallType { get; set; } = BattedBallType.Unknown;
        public FieldDirection FieldDirection { get; set; } = FieldDirection.Unknown;
        public string? PrimaryFielder { get; set; }
        public int? HomeRunDistanceMeters { get; set; }
        public bool CountsAsAtBat { get; set; }
        public bool IsHit { get; set; }
        public bool IsOut { get; set; }
        public bool IsSacrifice { get; set; }
        public bool IsWalk { get; set; }
        public bool IsIntentionalWalk { get; set; }
        public bool IsStrikeout { get; set; }
        public bool ReachedBase { get; set; }
        public int TotalBases { get; set; }
        public bool WasRecognized { get; set; }
    }

    public sealed class PitchEvent
    {
        public string PitchEventId { get; set; } = string.Empty;
        public string SourceEventId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string RelayGroupId { get; set; } = string.Empty;
        public string? PlateAppearanceId { get; set; }
        public int? SourceRelayNo { get; set; }
        public int? SourceSeqNo { get; set; }
        public int SourceOptionIndex { get; set; }
        public int ActualPitchIndex { get; set; }
        public int? DisplayPitchNumber { get; set; }
        public int? Inning { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public string? PitcherPcode { get; set; }
        public string? PitcherName { get; set; }
        public string? BatterPcode { get; set; }
        public string? BatterName { get; set; }
        public int? BallsBefore { get; set; }
        public int? StrikesBefore { get; set; }
        public int? BallsAfter { get; set; }
        public int? StrikesAfter { get; set; }
        public int? OutsBefore { get; set; }
        public string? RawPitchResult { get; set; }
        public PitchResultType PitchResult { get; set; } = PitchResultType.Unknown;
        public string? PitchType { get; set; }
        public double? SpeedKmh { get; set; }
        public string? PtsPitchId { get; set; }
        public bool HasPtsTracking { get; set; }
        public double? CrossPlateX { get; set; }
        public double? CrossPlateY { get; set; }
        public double? CalculatedCrossPlateX { get; set; }
        public double? CalculatedCrossPlateZ { get; set; }
        public double? TimeToPlateSeconds { get; set; }
        public double? TopStrikeZone { get; set; }
        public double? BottomStrikeZone { get; set; }
        public bool? IsInNominalStrikeZone { get; set; }
        public double? X0 { get; set; }
        public double? Y0 { get; set; }
        public double? Z0 { get; set; }
        public double? Vx0 { get; set; }
        public double? Vy0 { get; set; }
        public double? Vz0 { get; set; }
        public double? Ax { get; set; }
        public double? Ay { get; set; }
        public double? Az { get; set; }
        public string? BatterStance { get; set; }
        public bool IsSwing { get; set; }
        public bool IsWhiff { get; set; }
        public bool IsContact { get; set; }
        public bool IsInPlay { get; set; }
        public bool IsCalledStrike { get; set; }
    }

    public sealed class RunnerEvent
    {
        public string RunnerEventId { get; set; } = string.Empty;
        public string SourceEventId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string RelayGroupId { get; set; } = string.Empty;
        public string? PlateAppearanceId { get; set; }
        public int? SourceRelayNo { get; set; }
        public int? SourceSeqNo { get; set; }
        public int SourceOptionIndex { get; set; }
        public int? Inning { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public string? TeamCode { get; set; }
        public string? RunnerPcode { get; set; }
        public string? RunnerName { get; set; }
        public int? FromBase { get; set; }
        public int? ToBase { get; set; }
        public RunnerEventType EventType { get; set; } = RunnerEventType.Unknown;
        public RunnerAdvanceReason Reason { get; set; } = RunnerAdvanceReason.Unknown;
        public bool IsOut { get; set; }
        public bool IsRun { get; set; }
        public bool WasParsed { get; set; }
        public string? RawText { get; set; }
        public GameStateSnapshot? StateBefore { get; set; }
        public GameStateSnapshot? StateAfter { get; set; }
    }

    public sealed class PlayerChangeEvent
    {
        public string PlayerChangeEventId { get; set; } = string.Empty;
        public string SourceEventId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string RelayGroupId { get; set; } = string.Empty;
        public string? PlateAppearanceId { get; set; }
        public int? SourceRelayNo { get; set; }
        public int? SourceSeqNo { get; set; }
        public int SourceOptionIndex { get; set; }
        public int? Inning { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public TeamSide ChangedTeamSide { get; set; } = TeamSide.Unknown;
        public string? TeamCode { get; set; }
        public PlayerChangeType ChangeType { get; set; } = PlayerChangeType.Unknown;
        public string? RawChangeType { get; set; }
        public string? RawText { get; set; }
        public string? OutPlayerPcode { get; set; }
        public string? OutPlayerName { get; set; }
        public string? OutPosition { get; set; }
        public string? InPlayerPcode { get; set; }
        public string? InPlayerName { get; set; }
        public string? InPosition { get; set; }
        public string? ShiftPlayerPcode { get; set; }
        public string? ShiftPlayerName { get; set; }
        public string? OldPosition { get; set; }
        public string? NewPosition { get; set; }
        public int? SourceOutPlayerTurn { get; set; }
        public int? BatOrder { get; set; }
        public bool IsPitcherChange { get; set; }
        public bool IsPinchHitter { get; set; }
        public bool IsPinchRunner { get; set; }
        public bool WasParsed { get; set; }
        public GameStateSnapshot? StateBefore { get; set; }
        public GameStateSnapshot? StateAfter { get; set; }
    }

    public sealed class AdministrativeEvent
    {
        public string AdministrativeEventId { get; set; } = string.Empty;
        public string SourceEventId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string RelayGroupId { get; set; } = string.Empty;
        public string? PlateAppearanceId { get; set; }
        public int? SourceRelayNo { get; set; }
        public int? SourceSeqNo { get; set; }
        public int SourceOptionIndex { get; set; }
        public int? Inning { get; set; }
        public TeamSide BattingSide { get; set; } = TeamSide.Unknown;
        public AdministrativeEventType EventType { get; set; } = AdministrativeEventType.Unknown;
        public string? RawText { get; set; }
        public int AutomaticBallDelta { get; set; }
        public int AutomaticStrikeDelta { get; set; }
        public string? ReviewOriginalCall { get; set; }
        public string? ReviewFinalCall { get; set; }
        public bool? ReviewOverturned { get; set; }
        public bool WasRecognized { get; set; }
        public GameStateSnapshot? StateBefore { get; set; }
        public GameStateSnapshot? StateAfter { get; set; }
    }

    public sealed class GamePlayerBattingLine
    {
        public string GameId { get; set; } = string.Empty;
        public TeamSide TeamSide { get; set; } = TeamSide.Unknown;
        public string? TeamCode { get; set; }
        public string? Pcode { get; set; }
        public string? Name { get; set; }
        public int? BatOrder { get; set; }
        public int? LineupSequence { get; set; }
        public string? Position { get; set; }
        public string? BirthDateRaw { get; set; }
        public string? Height { get; set; }
        public string? Weight { get; set; }
        public string? BackNumber { get; set; }
        public string? HitType { get; set; }
        public bool EnteredAsSubstitute { get; set; }
        public bool LeftGame { get; set; }
        public int? PlateAppearances { get; set; }
        public int? AtBats { get; set; }
        public int? Hits { get; set; }
        public int? HomeRuns { get; set; }
        public int? Walks { get; set; }
        public int? HitByPitch { get; set; }
        public int? Strikeouts { get; set; }
        public int? Runs { get; set; }
        public int? RunsBattedIn { get; set; }
    }

    public sealed class GamePlayerPitchingLine
    {
        public string GameId { get; set; } = string.Empty;
        public TeamSide TeamSide { get; set; } = TeamSide.Unknown;
        public string? TeamCode { get; set; }
        public string? Pcode { get; set; }
        public string? Name { get; set; }
        public string? BirthDateRaw { get; set; }
        public string? Height { get; set; }
        public string? Weight { get; set; }
        public string? BackNumber { get; set; }
        public string? HitType { get; set; }
        public int? AppearanceSequence { get; set; }
        public string? InningsDisplay { get; set; }
        public int? PitchCount { get; set; }
        public int? HitsAllowed { get; set; }
        public int? HomeRunsAllowed { get; set; }
        public int? Walks { get; set; }
        public int? HitBatters { get; set; }
        public int? Strikeouts { get; set; }
        public int? RunsAllowed { get; set; }
        public int? EarnedRuns { get; set; }
        public int? WildPitches { get; set; }
    }

    public sealed class ParserDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? GameId { get; set; }
        public string? RelayGroupId { get; set; }
        public string? EventId { get; set; }
        public int? SourceRelayNo { get; set; }
        public int? SourceSeqNo { get; set; }
    }

    public sealed class ParserSummary
    {
        public int RawRelayGroupCount { get; set; }
        public int RawEventCount { get; set; }
        public int RawPitchEventCount { get; set; }
        public int RawPtsCount { get; set; }
        public int DuplicateSourceSeqNoOccurrenceCount { get; set; }
        public int CompletedPlateAppearanceCount { get; set; }
        public int InterruptedPlateAppearanceCount { get; set; }
        public int PrePlateSubstitutionGroupCount { get; set; }
        public int InningMarkerGroupCount { get; set; }
        public int GameSummaryGroupCount { get; set; }
        public int PitchEventCount { get; set; }
        public int PtsMatchedPitchCount { get; set; }
        public int PtsMissingPitchCount { get; set; }
        public int RunnerEventCount { get; set; }
        public int PlayerChangeEventCount { get; set; }
        public int AdministrativeEventCount { get; set; }
        public int UnknownRelayGroupCount { get; set; }
        public int UnknownRawEventTypeCount { get; set; }
        public int UnknownBattingResultCount { get; set; }
        public int UnparsedRunnerEventCount { get; set; }
        public int UnparsedPlayerChangeCount { get; set; }
        public int UnknownAdministrativeEventCount { get; set; }
        public int PtsCalculationFailureCount { get; set; }
        public int FinalLineBattingMismatchCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
    }
}
