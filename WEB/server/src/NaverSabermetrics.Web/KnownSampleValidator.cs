using NaverRelay.Parsing;

internal static class KnownSampleValidator
{
    private sealed record ExpectedGame(
        int RawRelayGroups,
        int RawEvents,
        int CompletedPlateAppearances,
        int InterruptedPlateAppearances,
        int PrePlateSubstitutionGroups,
        int InningMarkerGroups,
        int GameSummaryGroups,
        int Pitches,
        int PtsMatched,
        int PtsMissing,
        int RunnerEvents,
        int PlayerChanges,
        int AdministrativeEvents,
        int DuplicateSeqNoOccurrences);

    private static readonly IReadOnlyDictionary<string, ExpectedGame> ExpectedGames =
        new Dictionary<string, ExpectedGame>(StringComparer.Ordinal)
        {
            ["20260626HTOB02026"] = new(92, 537, 73, 0, 1, 17, 1, 302, 302, 0, 27, 16, 26, 1),
            ["20260626KTSS02026"] = new(97, 544, 74, 0, 5, 17, 1, 310, 310, 0, 25, 21, 16, 5),
            ["20260626LGLT02026"] = new(88, 437, 66, 1, 3, 17, 1, 233, 233, 0, 18, 15, 16, 3),
            ["20260626WONC02026"] = new(100, 535, 79, 0, 3, 17, 1, 275, 275, 0, 41, 22, 17, 3),
            ["20260627HHSK02026"] = new(99, 533, 80, 0, 0, 18, 1, 305, 305, 0, 22, 10, 16, 0),
            ["20260627HTOB02026"] = new(96, 542, 75, 0, 3, 17, 1, 286, 285, 1, 32, 23, 29, 3),
            ["20260627KTSS02026"] = new(94, 518, 75, 0, 1, 17, 1, 286, 286, 0, 28, 13, 21, 1),
        };

    public static IReadOnlyList<string> Validate(IReadOnlyCollection<NormalizedGame> games)
    {
        var failures = new List<string>();
        var byId = games
            .Where(game => !string.IsNullOrWhiteSpace(game.GameId))
            .GroupBy(game => game.GameId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        Check(failures, "game count", ExpectedGames.Count, games.Count);
        foreach (var pair in ExpectedGames)
        {
            if (!byId.TryGetValue(pair.Key, out var matches) || matches.Count == 0)
            {
                failures.Add($"missing game: {pair.Key}");
                continue;
            }

            if (matches.Count > 1)
            {
                failures.Add($"duplicate normalized game: {pair.Key} ({matches.Count})");
                continue;
            }

            ValidateGame(failures, matches[0], pair.Value);
        }

        var summaries = games.Select(game => game.Summary).ToList();
        Check(failures, "aggregate raw relay groups", 666, summaries.Sum(s => s.RawRelayGroupCount));
        Check(failures, "aggregate raw events", 3646, summaries.Sum(s => s.RawEventCount));
        Check(failures, "aggregate completed PA", 522, summaries.Sum(s => s.CompletedPlateAppearanceCount));
        Check(failures, "aggregate interrupted PA", 1, summaries.Sum(s => s.InterruptedPlateAppearanceCount));
        Check(failures, "aggregate pre-PA substitution groups", 16, summaries.Sum(s => s.PrePlateSubstitutionGroupCount));
        Check(failures, "aggregate inning marker groups", 120, summaries.Sum(s => s.InningMarkerGroupCount));
        Check(failures, "aggregate game summary groups", 7, summaries.Sum(s => s.GameSummaryGroupCount));
        Check(failures, "aggregate pitches", 1997, summaries.Sum(s => s.PitchEventCount));
        Check(failures, "aggregate PTS matched", 1996, summaries.Sum(s => s.PtsMatchedPitchCount));
        Check(failures, "aggregate PTS missing", 1, summaries.Sum(s => s.PtsMissingPitchCount));
        Check(failures, "aggregate runner events", 193, summaries.Sum(s => s.RunnerEventCount));
        Check(failures, "aggregate player changes", 120, summaries.Sum(s => s.PlayerChangeEventCount));
        Check(failures, "aggregate administrative events", 141, summaries.Sum(s => s.AdministrativeEventCount));
        Check(failures, "aggregate duplicate source seqno occurrences", 16,
            summaries.Sum(s => s.DuplicateSourceSeqNoOccurrenceCount));
        Check(failures, "aggregate unknown relay groups", 0, summaries.Sum(s => s.UnknownRelayGroupCount));
        Check(failures, "aggregate unknown raw event types", 0, summaries.Sum(s => s.UnknownRawEventTypeCount));
        Check(failures, "aggregate unknown batting results", 0, summaries.Sum(s => s.UnknownBattingResultCount));
        Check(failures, "aggregate unparsed runner events", 0, summaries.Sum(s => s.UnparsedRunnerEventCount));
        Check(failures, "aggregate unparsed player changes", 0, summaries.Sum(s => s.UnparsedPlayerChangeCount));
        Check(failures, "aggregate unknown administrative events", 0,
            summaries.Sum(s => s.UnknownAdministrativeEventCount));
        Check(failures, "aggregate PTS calculation failures", 0,
            summaries.Sum(s => s.PtsCalculationFailureCount));
        Check(failures, "aggregate final batting line mismatches", 0,
            summaries.Sum(s => s.FinalLineBattingMismatchCount));
        Check(failures, "aggregate parser errors", 0, summaries.Sum(s => s.ErrorCount));
        Check(failures, "aggregate parser warnings", 1, summaries.Sum(s => s.WarningCount));

        return failures;
    }

    private static void ValidateGame(
        ICollection<string> failures,
        NormalizedGame game,
        ExpectedGame expected)
    {
        var s = game.Summary;
        Check(failures, $"{game.GameId} raw relay groups", expected.RawRelayGroups, s.RawRelayGroupCount);
        Check(failures, $"{game.GameId} raw events", expected.RawEvents, s.RawEventCount);
        Check(failures, $"{game.GameId} completed PA", expected.CompletedPlateAppearances, s.CompletedPlateAppearanceCount);
        Check(failures, $"{game.GameId} interrupted PA", expected.InterruptedPlateAppearances, s.InterruptedPlateAppearanceCount);
        Check(failures, $"{game.GameId} pre-PA substitution groups", expected.PrePlateSubstitutionGroups, s.PrePlateSubstitutionGroupCount);
        Check(failures, $"{game.GameId} inning marker groups", expected.InningMarkerGroups, s.InningMarkerGroupCount);
        Check(failures, $"{game.GameId} game summary groups", expected.GameSummaryGroups, s.GameSummaryGroupCount);
        Check(failures, $"{game.GameId} pitches", expected.Pitches, s.PitchEventCount);
        Check(failures, $"{game.GameId} PTS matched", expected.PtsMatched, s.PtsMatchedPitchCount);
        Check(failures, $"{game.GameId} PTS missing", expected.PtsMissing, s.PtsMissingPitchCount);
        Check(failures, $"{game.GameId} runner events", expected.RunnerEvents, s.RunnerEventCount);
        Check(failures, $"{game.GameId} player changes", expected.PlayerChanges, s.PlayerChangeEventCount);
        Check(failures, $"{game.GameId} administrative events", expected.AdministrativeEvents, s.AdministrativeEventCount);
        Check(failures, $"{game.GameId} duplicate source seqno occurrences",
            expected.DuplicateSeqNoOccurrences, s.DuplicateSourceSeqNoOccurrenceCount);
        Check(failures, $"{game.GameId} unknown relay groups", 0, s.UnknownRelayGroupCount);
        Check(failures, $"{game.GameId} unknown raw event types", 0, s.UnknownRawEventTypeCount);
        Check(failures, $"{game.GameId} unknown batting results", 0, s.UnknownBattingResultCount);
        Check(failures, $"{game.GameId} unparsed runner events", 0, s.UnparsedRunnerEventCount);
        Check(failures, $"{game.GameId} unparsed player changes", 0, s.UnparsedPlayerChangeCount);
        Check(failures, $"{game.GameId} unknown administrative events", 0, s.UnknownAdministrativeEventCount);
        Check(failures, $"{game.GameId} PTS calculation failures", 0, s.PtsCalculationFailureCount);
        Check(failures, $"{game.GameId} final batting line mismatches", 0, s.FinalLineBattingMismatchCount);
        Check(failures, $"{game.GameId} parser errors", 0, s.ErrorCount);
    }

    private static void Check(ICollection<string> failures, string label, int expected, int actual)
    {
        if (expected != actual)
        {
            failures.Add($"{label}: expected {expected}, actual {actual}");
        }
    }
}
