using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

// FanGraphs' WPA definition, with an independently estimated KBO run distribution.
// Stored in percentage points to match Naver WpaByPlate (standard WPA = value / 100).
public sealed class EstimatedWpaModel
{
    public const string Version = "fanzai-kbo-remainder-v1";
    public int Year { get; set; }
    public int TrainingGames { get; set; }
    public int[] Samples { get; set; } = [];
    public double[][] Runs { get; set; } = [];
    private readonly Dictionary<(int, bool, int, int), double> emptyCache = new();

    public double HomeExpectation(int inning, bool homeBatting, int outs, int bases, int homeLead, int lastInning = 12)
    {
        if (inning < 1 || inning > lastInning || outs is < 0 or > 3 || bases is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(inning));
        if (homeBatting && inning >= 9 && homeLead > 0) return 1;
        if (outs == 3) return AfterHalf(inning, homeBatting, homeLead, lastInning);
        // Far tails are bounded solely to keep the dynamic-programming table finite.
        if (homeLead >= 60) return 1;
        if (homeLead <= -60) return 0;
        var key = (inning, homeBatting, homeLead, lastInning);
        if (outs == 0 && bases == 0 && emptyCache.TryGetValue(key, out var cached)) return cached;
        double result = 0;
        var distribution = Runs[outs * 8 + bases];
        for (var r = 0; r < distribution.Length; r++)
            if (distribution[r] > 0)
                result += distribution[r] * AfterHalf(inning, homeBatting, homeLead + (homeBatting ? r : -r), lastInning);
        if (outs == 0 && bases == 0) emptyCache[key] = result;
        return result;
    }

    private double AfterHalf(int inning, bool home, int lead, int last)
    {
        if (inning >= 9 && lead > 0) return 1;
        if (!home) return HomeExpectation(inning, true, 0, 0, lead, last);
        if (inning >= 9 && lead < 0) return 0;
        if (inning == last) return 0.5; // Expected win credit: a draw is half a win.
        return HomeExpectation(inning + 1, false, 0, 0, lead, last);
    }
}

public sealed record EstimatedWpaValue(string PlateAppearanceId, string RelayGroupId, double HomeBefore,
    double HomeAfter, double Wpa, int Year, string ModelVersion = EstimatedWpaModel.Version);

public static class EstimatedWpa
{
    public static int? BaseMask(GameStateSnapshot? state)
    {
        if (state is null) return null;
        // Parser represents an empty base as null; slots retain occupancy even if player lookup failed.
        int Occupied(int? slot, string? code, string? name) => slot is > 0 || !string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(name) ? 1 : 0;
        return Occupied(state.FirstBaseSlot, state.FirstBaseRunnerPcode, state.FirstBaseRunnerName)
             + 2 * Occupied(state.SecondBaseSlot, state.SecondBaseRunnerPcode, state.SecondBaseRunnerName)
             + 4 * Occupied(state.ThirdBaseSlot, state.ThirdBaseRunnerPcode, state.ThirdBaseRunnerName);
    }

    public static List<EstimatedWpaValue> Calculate(NormalizedGame game, EstimatedWpaModel model)
    {
        var result = new List<EstimatedWpaValue>();
        if (game.SeasonYear is < 2016 or > 2023 || !game.IsRegularSeason || game.StatusCode is not ("RESULT" or "ENDED") ||
            game.SeasonYear != model.Year || game.HomeTeam.FinalScore is not int finalHome || game.AwayTeam.FinalScore is not int finalAway)
            return result;
        var lastInning = game.GameDate is { } date && date.StartsWith("2021") && string.CompareOrdinal(date[..10], "2021-08-10") >= 0 ? 9 : 12;
        var official = game.PlateAppearances.Where(p => p.IsOfficialPlateAppearance).OrderBy(p => p.SequenceNumber).ToArray();
        if (official.Length == 0) return result;
        var last = official[^1];
        // Rain-shortened / time-limited / incomplete games need an explicit rules model. Do not guess.
        if (last.Inning is not int endInning || endInning < 9 || endInning > lastInning ||
            last.StateAfter?.HomeScore != finalHome || last.StateAfter?.AwayScore != finalAway ||
            (finalHome == finalAway && (endInning != lastInning || last.StateAfter.Outs != 3))) return result;
        foreach (var pa in official)
        {
            if (pa.WpaByPlate.HasValue || pa.Inning is not int inning || inning < 1 || inning > lastInning ||
                pa.BattingSide is not (TeamSide.Home or TeamSide.Away)) continue;
            var before = pa.StateBefore; var after = pa.StateAfter;
            if (before?.HomeScore is not int bh || before.AwayScore is not int ba || before.Outs is not int bo || bo is < 0 or > 2 ||
                after?.HomeScore is not int ah || after.AwayScore is not int aa || after.Outs is not int ao || ao < bo || ao > 3 ||
                bh < 0 || ba < 0 || ah < bh || aa < ba || ah > finalHome || aa > finalAway) continue;
            var home = pa.BattingSide == TeamSide.Home;
            if ((home && aa != ba) || (!home && ah != bh)) continue;
            // Need an explicit PA-start snapshot and a result event, not a carried-over half-inning state.
            var start = game.Events.FirstOrDefault(e => e.EventId == pa.StartEventId);
            if (start?.RawType != 8 || pa.ResultEventId is null) continue;
            var pre = model.HomeExpectation(inning, home, bo, BaseMask(before)!.Value, bh - ba, lastInning);
            var post = model.HomeExpectation(inning, home, ao, BaseMask(after)!.Value, ah - aa, lastInning);
            if (pa == last)
            {
                var legalEnd = home ? (finalHome > finalAway || (ao == 3 && (finalHome < finalAway || inning == lastInning)))
                                    : ao == 3 && finalHome > finalAway;
                if (!legalEnd) continue;
                post = finalHome == finalAway ? 0.5 : finalHome > finalAway ? 1 : 0;
            }
            result.Add(new(pa.PlateAppearanceId, pa.RelayGroupId, pre * 100, post * 100,
                (post - pre) * (home ? 100 : -100), model.Year));
        }
        return result;
    }
}
