using NaverRelay.Parsing;

namespace NaverRelay.Gui.Models;

internal enum WorkflowStage
{
    Waiting,
    Reading,
    Parsing,
    Saving,
    Completed,
    Deferred,
    Reconciling,
    Failed,
}

internal sealed class WorkflowProgress
{
    public required string DocumentId { get; init; }
    public required string DocumentName { get; init; }
    public int CurrentIndex { get; init; }
    public int TotalCount { get; init; }
    public WorkflowStage Stage { get; init; }
    public string? Message { get; init; }
}

internal sealed class ParsingFailure
{
    public required string DocumentId { get; init; }
    public required string SourceName { get; init; }
    public required string Error { get; init; }
}

internal sealed class LightweightParsedGameSummary
{
    public string GameId { get; init; } = string.Empty;
    public string? GameDate { get; init; }
    public string? AwayTeamCode { get; init; }
    public string? HomeTeamCode { get; init; }
    public ParserSummary Summary { get; init; } = new();
}

internal sealed class ParsingWorkflowResult
{
    /// <summary>
    /// 소규모 입력(기본 25경기 이하)에서만 검증/미리보기 목적으로 보관합니다.
    /// 대규모 백필에서는 SQLite 저장 직후 해제해 메모리 사용량이 경기 수에 비례하지 않게 합니다.
    /// </summary>
    public List<NormalizedGame> Games { get; } = new();
    public List<LightweightParsedGameSummary> Summaries { get; } = new();
    public List<ParsingFailure> Failures { get; } = new();
    public List<ParsingFailure> Deferred { get; } = new();
    public List<ParsingFailure> ReviewWarnings { get; } = new();
    public string? OutputDirectory { get; set; }
    public int ParsedGameCount { get; set; }
    public long CompletedPlateAppearanceCount { get; set; }
    public long PitchCount { get; set; }
}
