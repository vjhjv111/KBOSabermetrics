using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NaverSabermetrics.Web;

try
{
    if (args.Length < 3) throw new ArgumentException("Usage: <game-lib> <readonly-baseball-db> <scratch-output-directory> [sample-count=10000]");
    var lib = Path.GetFullPath(args[0]); var db = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(output); var count = args.Length > 3 ? int.Parse(args[3]) : 10000;
    var mode = args.Length > 4 ? args[4] : "baseline";
    if (mode == "contracts") { PitchingContracts.Run(lib, output); return; }
    var legacy = new DiamondData(lib); var rosterService = new DiamondRosterService(db); var roster = rosterService.Get(null);
    var observedMethod = typeof(DiamondEngine).GetMethod("CreateAiPitch", [typeof(DiamondGame), typeof(long)]);
    var profileMethod = typeof(DiamondRosterService).GetMethod("SelectPitching", [typeof(int), typeof(string), typeof(CancellationToken)]);
    var profileProperty = typeof(DiamondGame).GetProperty("PitchingProfile");
    if (mode == "observed" && (observedMethod == null || profileMethod == null || profileProperty == null))
        throw new InvalidOperationException("Observed pitching production API has not been added yet.");
    var profiles = new Dictionary<string, object>();
    uint seed = 314159265;
    double Random() { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296d; }
    var results = new Dictionary<string, Counts>(); var collisions = new Dictionary<string, int>();
    var engines = new Dictionary<(string Batter, string Pitcher), (DiamondData Data, DiamondEngine Engine)>();
    var observations = new List<object>();
    var groups = new[] { ("R", "R", false), ("L", "R", false), ("R", "L", false), ("L", "L", false), ("R", "R", true), ("L", "R", true) };
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < count; i++)
    {
        var group = groups[i % groups.Length]; var local = i / groups.Length;
        var batters = roster.Batters.Where(b => b.Profile?.Bats == group.Item1 && b.Pa >= 100).Take(20).ToArray();
        var pitchers = roster.Pitchers.Where(p => p.Profile?.Throws == group.Item2 && (p.Profile?.Delivery == "underhand") == group.Item3 && p.Tbf >= 100).Take(20).ToArray();
        if (batters.Length == 0 || pitchers.Length == 0) continue;
        var batter = batters[(local / pitchers.Length) % batters.Length]; var pitcher = pitchers[local % pitchers.Length];
        if (!engines.TryGetValue((batter.Id, pitcher.Id), out var pair))
        {
            var source = legacy.ForRoster(new(roster.Season, roster.AsOf, roster.Revision, batter, pitcher));
            engines[(batter.Id, pitcher.Id)] = pair = (source, new DiamondEngine(source, Random));
        }
        var game = new DiamondGame { Mode = "ai", HostRole = "batter", Batter = batter.Id, Pitcher = pitcher.Id, Pace = "full" };
        string type; bool inside; DiamondVec aim;
        if (mode == "observed")
        {
            if (!profiles.TryGetValue(pitcher.Id, out var profile)) profiles[pitcher.Id] = profile = profileMethod!.Invoke(rosterService, [roster.Season, pitcher.Id, CancellationToken.None])!;
            profileProperty!.SetValue(game, profile);
            game.Pitch = (DiamondPitch)observedMethod!.Invoke(pair.Engine, [game, 1700000000000L])!;
            type = game.Pitch.Type; aim = game.Pitch.Target; inside = Math.Abs(aim.X)<=1 && Math.Abs(aim.Y)<=1;
        }
        else
        {
            // Baseline policy copied from the pre-change AI-ready handler. Keep this policy named explicitly
            // so future changes can be compared with the same original placement distribution.
            type = pair.Engine.PickAiPitch(game.Pitcher); var quality = .65 + pair.Engine.Rand() * .3;
            inside = pair.Engine.Rand() < (pair.Data.Discipline(game.Pitcher, "pitcher")?.ZonePitchRate ?? .48);
            if (inside) aim = new((pair.Engine.Rand() - .5) * 1.65, (pair.Engine.Rand() - .5) * 1.65);
            else if (pair.Engine.Rand() < .5) aim = new((pair.Engine.Rand() < .5 ? -1 : 1) * (1.05 + pair.Engine.Rand() * .5), (pair.Engine.Rand() - .5) * 1.9);
            else aim = new((pair.Engine.Rand() - .5) * 1.9, (pair.Engine.Rand() < .5 ? -1 : 1) * (1.05 + pair.Engine.Rand() * .5));
            game.Pitch = pair.Engine.CreatePitch(game, type, aim, quality, 1700000000000);
        }
        var result = pair.Engine.EvaluatePitch(game, null, (long)(game.Pitch.ReleaseAt + game.Pitch.FlightMs + 1000));
        var label = $"batter{group.Item1}-pitcher{group.Item2}" + (group.Item3 ? "-underhand" : "");
        if (!results.TryGetValue(label, out var counter)) results[label] = counter = new();
        var zone = Math.Abs(game.Pitch.Target.X) <= 1 && Math.Abs(game.Pitch.Target.Y) <= 1;
        counter.Total++; if (zone) counter.Zone++; if (game.Pitch.BodyHit != null) counter.BodyHits++;
        if (result.Outcome == "HBP") counter.Hbp++; if (zone && game.Pitch.BodyHit != null) counter.ZoneBodyHits++;
        if (inside) { counter.AimedZone++; if (result.Outcome == "HBP") counter.AimedZoneHbp++; }
        if (game.Pitch.BodyHit is { } hit)
        {
            var capsules = pair.Data.Colliders(pair.Data.BatsLeft(batter.Id, pitcher.Id));
            for (var j = 0; j < capsules.Count; j++)
            {
                if (!DiamondEngine.SegmentTouchesBody(DiamondEngine.BallPosition(game.Pitch, hit.At - .01), hit.Position, [capsules[j]])) continue;
                var bodyPart = j switch { 0 or 1 => "torso", >= 2 and <= 5 => "arms", >= 6 and <= 9 => "legs", >= 10 and <= 14 => "head", _ => "hands" };
                var key = (result.Outcome == "HBP" ? "hbp:" : "zone-contact:") + bodyPart;
                collisions[key] = collisions.GetValueOrDefault(key) + 1; break;
            }
        }
        observations.Add(new { Group=label, Batter=batter.Id, Pitcher=pitcher.Id, Pitch=type, Aim=aim, Target=game.Pitch.Target, Velocity=game.Pitch.Velocity, Zone=zone, Hbp=result.Outcome=="HBP", BodyHit=game.Pitch.BodyHit });
        if ((i+1)%2500==0) Console.WriteLine($"Progress {i+1}/{count}; elapsed {watch.Elapsed.TotalSeconds:F1}s");
    }
    watch.Stop(); var total = new Counts(); foreach(var item in results.Values) total.Add(item);
    var summary = new { Policy=mode == "observed" ? "measured joint profile AI" : "original AI-ready placement", Seed=314159265, roster.Season, roster.AsOf, Sample=total.Summary(), Groups=results.ToDictionary(x=>x.Key,x=>x.Value.Summary()), Collisions=collisions, ElapsedSeconds=watch.Elapsed.TotalSeconds };
    var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"); var summaryPath = Path.Combine(output,$"{mode}-{stamp}.json");
    File.WriteAllText(summaryPath,JsonSerializer.Serialize(summary,new JsonSerializerOptions(DiamondJson.Options){WriteIndented=true}));
    File.WriteAllText(Path.Combine(output,$"pitches-{stamp}.json"),JsonSerializer.Serialize(observations,DiamondJson.Options));
    Console.WriteLine(JsonSerializer.Serialize(summary,DiamondJson.Options)); Console.WriteLine("Summary: " + summaryPath);
}
catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }

sealed class Counts
{
    public int Total,Zone,BodyHits,Hbp,ZoneBodyHits,AimedZone,AimedZoneHbp;
    public void Add(Counts x) { Total+=x.Total;Zone+=x.Zone;BodyHits+=x.BodyHits;Hbp+=x.Hbp;ZoneBodyHits+=x.ZoneBodyHits;AimedZone+=x.AimedZone;AimedZoneHbp+=x.AimedZoneHbp; }
    public object Summary()=>new{Total,Zone,ZoneRate=(double)Zone/Total,BodyHits,BodyHitRate=(double)BodyHits/Total,Hbp,HbpRate=(double)Hbp/Total,ZoneBodyHits,AimedZone,AimedZoneHbp};
}
