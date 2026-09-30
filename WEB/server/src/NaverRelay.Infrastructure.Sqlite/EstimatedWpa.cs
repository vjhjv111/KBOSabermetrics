using System.Text.Json.Serialization;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

// Exact unrounded home-WE cells returned by FanGraphs' public WPA Inquirer.
// DB WPA is percentage points (the standard decimal WPA is this value / 100).
public sealed class EstimatedWpaModel
{
    public const string Version = "fangraphs-we-4.5-a32732a0eee4";
    public const double RunEnvironment = 4.5;
    public const string Source = "https://www.fangraphs.com/tools/wpa-inquirer";
    public int Year { get; set; }
    public string TableVersion { get; set; } = Version;
    public string TableSha256 { get; set; } = FanGraphsWeTable.ExpectedSha256;
    [JsonIgnore] public IReadOnlyDictionary<string,double>? Cells { get; init; }

    public static string? StateKey(int inning, bool home, int outs, int bases, int lead, out double? terminal)
    {
        terminal = null;
        if (inning < 1 || outs is < 0 or > 3 || bases is < 0 or > 7) return null;
        if (inning >= 9 && home && lead > 0) { terminal = 1; return null; }
        if (outs == 3)
        {
            if (inning >= 9 && lead > 0) { terminal = 1; return null; }
            if (inning >= 9 && home && lead < 0) { terminal = 0; return null; }
            if (home) inning++;
            home = !home; outs = 0; bases = 0;
        }
        // Do not clamp score differentials or synthesize values outside the tool's table.
        if (lead is < -10 or > 10) return null;
        return $"{Math.Min(inning,9)}:{(home ? "A" : "H")}:{outs}:{bases}:{lead}";
    }

    public bool TryHomeExpectation(int inning, bool home, int outs, int bases, int lead, out double value)
    {
        var key = StateKey(inning,home,outs,bases,lead,out var terminal);
        value = terminal ?? 0;
        if (terminal.HasValue) return true;
        if (key is null) return false;
        // A missing bundled cell must abort the game transaction, not erase existing WPA.
        if (!(Cells ?? FanGraphsWeTable.Cells).TryGetValue(key,out value)) throw new InvalidOperationException("FanGraphs WE cell not loaded: " + key);
        return true;
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

    public static List<EstimatedWpaValue> Calculate(NormalizedGame game, EstimatedWpaModel model, bool replaceExisting = false)
    {
        var result = new List<EstimatedWpaValue>();
        if (game.SeasonYear is < 2016 or > 2023 || game.StatusCode is not ("RESULT" or "ENDED") ||
            game.SeasonYear != model.Year || game.HomeTeam.FinalScore is not int finalHome || game.AwayTeam.FinalScore is not int finalAway)
            return result;
        var official = game.PlateAppearances.Where(p => p.IsOfficialPlateAppearance).OrderBy(p => p.SequenceNumber).ToArray();
        if (official.Length == 0) return result;
        var last = official[^1];
        // A caught stealing / wild pitch can end the game after the last official PA.
        // Validate the final relay; do not attach that later event's WPA to the preceding PA.
        var finalRelay = game.RelayGroups.OrderBy(g=>g.ChronologicalIndex).LastOrDefault(g=>
            g.Inning is >0 && g.BattingSide is TeamSide.Home or TeamSide.Away &&
            g.StateAfter?.Outs is >=0 and <=3 && g.StateAfter.HomeScore is not null && g.StateAfter.AwayScore is not null);
        var finalState = finalRelay?.StateAfter ?? last.StateAfter;
        var endInning = finalRelay?.Inning ?? last.Inning;
        var endSide = finalRelay?.BattingSide ?? last.BattingSide;
        if (endInning is null or <9 || endSide is not (TeamSide.Home or TeamSide.Away) ||
            finalState?.HomeScore!=finalHome || finalState.AwayScore!=finalAway ||
            (endSide==TeamSide.Home ? finalHome<=finalAway && finalState.Outs!=3 : finalHome<=finalAway || finalState.Outs!=3)) return result;
        foreach (var pa in official)
        {
            if ((!replaceExisting && pa.WpaByPlate.HasValue) || pa.Inning is not int inning || inning < 1 ||
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
            if (home && inning >= 9 && bh > ba) continue; // A game already won cannot start another PA.
            if (!model.TryHomeExpectation(inning, home, bo, BaseMask(before)!.Value, bh - ba, out var pre)) continue;
            double post;
            var endsGame = pa == last && inning==endInning && pa.BattingSide==endSide && ah==finalHome && aa==finalAway &&
                ((home && ah>aa) || (ao==3 && finalState.Outs==3));
            if (endsGame)
            {
                // The MLB WE table has no draw outcome; do not invent 0.5 for a KBO draw.
                if (finalHome == finalAway) continue;
                post = finalHome > finalAway ? 1 : 0;
            }
            else if (!model.TryHomeExpectation(inning, home, ao, BaseMask(after)!.Value, ah - aa, out post)) continue;
            result.Add(new(pa.PlateAppearanceId, pa.RelayGroupId, pre * 100, post * 100,
                (post - pre) * (home ? 100 : -100), model.Year));
        }
        return result;
    }
}
