namespace NaverRelay.Parsing
{
    public enum TeamSide
    {
        Unknown = -1,
        Away = 0,
        Home = 1,
    }

    public enum RelayGroupType
    {
        Unknown = 0,
        InningMarker,
        CompletedPlateAppearance,
        InterruptedPlateAppearance,
        PrePlateSubstitution,
        PlayerChangeOnly,
        AdministrativeOnly,
        GameSummary,
        IncompletePlateAppearance,
    }

    public enum NormalizedEventType
    {
        Unknown = 0,
        InningMarker,
        Pitch,
        PlayerChange,
        Administrative,
        PlateAppearanceStart,
        BatterResult,
        RunnerResult,
        GameSummary,
    }

    public enum PlateAppearanceStatus
    {
        Completed = 0,
        InterruptedByRunnerOut,
        IncompleteUnknown,
    }

    public enum BattingResultType
    {
        Unknown = 0,
        Single,
        InfieldSingle,
        BuntSingle,
        Double,
        Triple,
        HomeRun,
        Walk,
        IntentionalWalk,
        HitByPitch,
        Strikeout,
        GroundOut,
        FlyOut,
        LineOut,
        InfieldFlyOut,
        FoulFlyOut,
        BuntOut,
        SacrificeFly,
        SacrificeBunt,
        GroundedIntoDoublePlay,
        ReachedOnError,
        FieldersChoice,
        OtherOut,
    }

    public enum BattedBallType
    {
        Unknown = 0,
        GroundBall,
        FlyBall,
        LineDrive,
        PopUp,
        Bunt,
    }

    public enum FieldDirection
    {
        Unknown = 0,
        Left,
        LeftCenter,
        Center,
        RightCenter,
        Right,
        Pitcher,
        Catcher,
        FirstBase,
        SecondBase,
        ThirdBase,
        Shortstop,
    }

    public enum PitchResultType
    {
        Unknown = 0,
        Ball,
        Foul,
        InPlay,
        SwingingStrike,
        CalledStrike,
        BuntFoul,
    }

    public enum RunnerEventType
    {
        Unknown = 0,
        Advance,
        Scored,
        ForceOut,
        TagOut,
        CaughtStealing,
        Pickoff,
    }

    public enum RunnerAdvanceReason
    {
        Unknown = 0,
        BatterPlay,
        StolenBase,
        CaughtStealing,
        WildPitch,
        Error,
        OtherRunnerPlay,
        Pickoff,
        Obstruction,
        BaserunningPlay,
        ForceOut,
        TagOut,
    }

    public enum PlayerChangeType
    {
        Unknown = 0,
        Substitution,
        PositionShift,
        TextOnly,
    }

    public enum AdministrativeEventType
    {
        Unknown = 0,
        MoundVisitByCoach,
        MoundVisitByCatcher,
        PitcherDisengagement,
        PitchClockViolation,
        VideoReview,
        Ejection,
    }

    public enum DiagnosticSeverity
    {
        Info = 0,
        Warning,
        Error,
    }
}
