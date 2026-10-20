using System.Text.Json;
using System.Text.Json.Nodes;
using KboRelayDownloader;
using NaverSabermetrics.Web;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var batters = Enumerable.Range(1, 9).Select(i => new {
    seqno = 1, batOrder = i, name = $"선발{i}", posName = "우익수", pcode = i.ToString(), cout = "true"
}).ToList();
var json = JsonSerializer.Serialize(new { result = new {
    game = new { homeStarterName = "홈선발", awayStarterName = "원정선발" },
    textRelayData = new { homeLineup = new { batter = batters }, awayLineup = new { batter = batters } }
}});
var root = JsonNode.Parse(json)!;
root["result"]!["textRelayData"]!["homeLineup"]!["batter"]!.AsArray()
    .Add(JsonNode.Parse("""{"seqno":2,"batOrder":1,"name":"대타","posName":"대타","cin":"true"}"""));
var data = ProbableStarterFetcher.Parse(root.ToJsonString());
Check(data.HomeLineup.Length == 9 && data.HomeLineup[0].Name == "선발1", "substitute exclusion, cout retained");
var now = DateTimeOffset.UtcNow;
var date = BotGamesService.ParseDate(null).ToString("yyyy-MM-dd");
var row = new StarterGame("33331020HTLG02026", date + "T18:30:00", "KIA", "LG", "READY", null, null, null, false);
row = BotStartersWorker.MergeLineups(row, null, data, now);
var corrected = data with { HomeLineup = data.HomeLineup.Select(x => x.Order == 1 ? x with { Name = "수정선발" } : x).ToArray() };
Check(BotStartersWorker.MergeLineups(row, row, corrected, now).HomeLineup[0].Name == "수정선발", "pregame correction");
Check(BotStartersWorker.MergeLineups(row with {Status="STARTED"}, row, corrected, now).HomeLineup[0].Name == "선발1", "freeze starters after start");
var frozen = BotStartersWorker.MergeLineups(row with {Status="STARTED"}, row, corrected, now);
Check(frozen.LineupFrozen && BotStartersWorker.MergeLineups(row, frozen, corrected, now).HomeLineup[0].Name == "선발1", "freeze latch survives status regression");
var absent = ProbableStarterFetcher.Parse("""{"result":{"game":{"homeStarterName":"투수"},"textRelayData":null}}""");
Check(absent.HomeLineup.Length == 0, "unannounced");
root["result"]!["textRelayData"]!["awayLineup"]!["batter"]!.AsArray().RemoveAt(0);
Check(ProbableStarterFetcher.Parse(root.ToJsonString()).AwayLineup.Length == 0, "partial lineup not confirmed");
foreach (var alias in new[] {"@한화", "한화 이글스", "hh"})
    Check(BotStartersService.NormalizeLineupTeam(alias) == "한화", "aliases");
Check(BotStartersService.NormalizeLineupTeam("기아타이거즈") == "KIA", "KIA distinct from Hanwha");
try { BotStartersService.NormalizeLineupTeam("미지정"); throw new Exception("invalid accepted"); } catch (RequestError) {}
var day = new StarterDay(date, now, [row]);
var response = BotStartersService.FormatLineup(date, "LG", day, now);
Check(response.Games.Length == 1 && response.Text.Contains("9. 선발9") && !response.Stale, "formatted lineup");
Check(BotStartersService.FormatLineup(date, "한화", day, now).Text.Contains("예정된 경기가 없습니다"), "no match");
Check(!BotStartersService.FormatLineup(date, "LG", null, now).Available, "not collected != no games");
Check(BotStartersService.FormatLineup(date, "LG", day, now.AddMinutes(16)).Stale, "stale");
var failed = day with {Games=[row with {FetchFailed=true}]};
Check(BotStartersService.FormatLineup(date, "LG", failed, now).Text.Contains("이전 정보"), "failed with previous lineup");
Check(BotStartersService.FormatLineup(date, "LG", day with {Games=[row with {Status="CANCEL"}]}, now).Text.Contains("취소"), "cancel");
Check(BotStartersService.FormatLineup(date, "LG", day with {Games=[row, row with {Id="doubleheader2"}]}, now).Games.Length == 2, "doubleheader");
var directory = Path.Combine(Path.GetTempPath(), "fanzai-lineups-" + Guid.NewGuid().ToString("N"));
var options = new SiteOptions { StateDirectory = directory };
var store = new BotStartersService(options);
await store.SaveAsync(day, CancellationToken.None);
Check(new BotStartersService(options).QueryLineup("LG").Games[0].Players.Length == 9, "persist/reload");
await store.SaveAsync(day with {Games=[frozen]}, CancellationToken.None);
var restored = new BotStartersService(options).Get(date)!.Games[0];
Check(restored.LineupFrozen && BotStartersWorker.MergeLineups(row, restored, corrected, now).HomeLineup[0].Name == "선발1",
    "freeze persists across restart");
await store.SaveAsync(day with {Games=[]}, CancellationToken.None);
Check(new BotStartersService(options).QueryLineup("LG").Games.Length == 0, "removed schedule cleared");
// Read the old snapshot schema without lineup properties.
await File.WriteAllTextAsync(Path.Combine(directory,"bot-starters.json"),
    $$$"""{"{{{date}}}":{"Date":"{{{date}}}","UpdatedAt":"{{{now:O}}}","Games":[{"Id":"old","Home":"LG","Away":"KIA","Status":"READY","FetchFailed":false}]}}""");
Check(new BotStartersService(options).QueryLineup("LG").Text.Contains("미발표"), "old snapshot compatible");
File.Delete(Path.Combine(directory, "bot-starters.json")); Directory.Delete(directory);
if (args.Length > 0) {
    var real = ProbableStarterFetcher.Parse(await File.ReadAllTextAsync(args[0]));
    Check(real.HomeLineup.Length == 9 && real.AwayLineup.Length == 0, "actual Naver fixture: missing original away slot must not use defensive replacement");
    Check(real.HomeLineup[0].Name == "홍창기" && real.HomeLineup[8].Name == "신민재", "actual original starters");
}
if (args.Length > 1) {
    var preview = ProbableStarterFetcher.ParsePreview("""{"result":{"previewData":""" + await File.ReadAllTextAsync(args[1]) + "}}");
    Check(preview.HomeLineup.Length == 9 && preview.AwayLineup.Length == 9, "actual pregame preview");
    Check(preview.HomeLineup[0].Name == "박해민", "preview batting order (pitcher excluded)");
}
Check(ProbableStarterFetcher.ParsePreview("""{"result":{"previewData":null}}""").HomeLineup.Length == 0, "preview unannounced");
Console.WriteLine("PASS lineup parsing, substitutions, correction/freeze, aliases, states, persistence, schedule removal");
