using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

namespace NaverSabermetrics.Web;

/// <summary>Local CLI checks only. Never mapped to an HTTP endpoint.</summary>
public static class Verification
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented=true };
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    public static async Task CreateSampleDatabaseAsync(string inputZip, string outputDb)
    {
        inputZip=Path.GetFullPath(inputZip); outputDb=Path.GetFullPath(outputDb);
        if (!File.Exists(inputZip)) throw new FileNotFoundException("Sample ZIP not found", inputZip);
        if (File.Exists(outputDb)) throw new IOException("Sample target already exists; choose a NEW output path.");
        var db=new DatabaseCacheService(outputDb);
        await db.InitializeAsync();
        var games = new List<NormalizedGame>();
        using var zip = ZipFile.OpenRead(inputZip);
        foreach(var entry in zip.Entries.Where(e=>e.Name.EndsWith(".json",StringComparison.OrdinalIgnoreCase)))
        {
            using var reader=new StreamReader(entry.Open());
            var game=RelayParser.ParseJson(await reader.ReadToEndAsync());
            var document=new InputDocument { Id=entry.FullName,Kind=InputDocumentKind.ZipEntry,ContainerPath=inputZip,EntryName=entry.FullName,Length=entry.Length };
            await db.SaveGameAndSourceAsync(game,document);
            games.Add(game);
        }
        var failures=KnownSampleValidator.Validate(games);
        Require(failures.Count==0,"7-game parser baseline: " + string.Join("; ",failures));
        var league=await db.GetLeagueReferenceAsync();
        Require(league.GameCount>0 && league.PitchingInnings>0,"sample league environment");
        SqliteConnection.ClearAllPools();
        SealSnapshot(outputDb);
        Console.WriteLine("SAMPLE_DB="+outputDb);
    }

    public static void SealSnapshot(string path)
    {
        using var con=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Pooling=false }.ToString());
        con.Open(); using var cmd=con.CreateCommand();
        cmd.CommandText="PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
        cmd.ExecuteNonQuery();
    }

    public static async Task AuditDatabaseAsync(string path)
    {
        var db = new DatabaseCacheService(path, webReadOnly:true);
        await db.ValidateWebSchemaAsync();
        var catalog=await db.GetCatalogAsync();
        Console.WriteLine($"Games={catalog.GameCount}; Years={string.Join(',',catalog.Years)}; Dates={catalog.MinGameDate:yyyy-MM-dd}..{catalog.MaxGameDate:yyyy-MM-dd}");
        using var con=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());
        con.Open();
        using (var cmd=con.CreateCommand())
        {
            cmd.CommandTimeout=600;cmd.CommandText="PRAGMA quick_check";
            using var r=cmd.ExecuteReader();while(r.Read())Require(r.GetString(0)=="ok","SQLite quick_check");
        }
        using (var cmd=con.CreateCommand())
        {
            cmd.CommandText="PRAGMA foreign_key_check";cmd.CommandTimeout=600;
            using var r=cmd.ExecuteReader();Require(!r.Read(),"SQLite foreign_key_check");
        }
        foreach(var table in new[]{"Games","PlateAppearances","Pitches","Players","BatterGameStats","PitcherGameStats"})
        {
            using var cmd=con.CreateCommand(); cmd.CommandText="SELECT COUNT(*) FROM "+table;
            Console.WriteLine(table+"="+cmd.ExecuteScalar());
        }
        using (var cmd=con.CreateCommand())
        {
            cmd.CommandText="EXPLAIN QUERY PLAN SELECT PitchEventId FROM Pitches WHERE PlateAppearanceId=$pa ORDER BY ActualPitchIndex DESC, SourceOptionIndex DESC, PitchEventId DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$pa","__verification__");
            using var r=cmd.ExecuteReader();while(r.Read())Console.WriteLine("PLAN: "+r.GetString(3));
        }
    }

    /// <summary>Verifies adapter parity, not independent validation of sabermetric definitions.</summary>
    public static async Task CompareDesktopAndWebAsync(string path)
    {
        var db=new DatabaseCacheService(path,webReadOnly:true);
        await db.InitializeAsync();
        var catalog=await db.GetCatalogAsync();
        Require(catalog.GameCount>0,"DB has games");
        var league=await db.GetLeagueReferenceAsync();
        var analytics=new DatabaseAnalyticsService(db);
        var year=catalog.Years.Count>0 ? catalog.Years.Max() : (int?)null;
        var queries=new[]{new GameQuery { SeasonYear=year },new GameQuery { Grouping=AnalyticsGrouping.PlayerCareer },
            new GameQuery { SeasonYear=year,Grouping=AnalyticsGrouping.Team },new GameQuery { SeasonYear=year,OutsBefore=2,BallsBefore=1,StrikesBefore=1 }};
        foreach(var q in queries)
        {
            var watch=Stopwatch.StartNew();
            var reference=await analytics.GetSnapshotAsync(q,league);
            var b=await analytics.GetWebRoleSnapshotAsync(q,league,false);
            var p=await analytics.GetWebRoleSnapshotAsync(q,league,true);
            Same(reference.BatterClassic,b.BatterClassic,"batting "+q.CacheKey);
            Same(reference.BatterSabermetrics,b.BatterSabermetrics,"wRC+ "+q.CacheKey);
            Same(reference.PitcherClassic,p.PitcherClassic,"pitching "+q.CacheKey);
            Same(reference.PitcherValues,p.PitcherValues,"WAR v3 "+q.CacheKey);
            Console.WriteLine("PARITY_MS="+watch.ElapsedMilliseconds);
        }
        var service=new RecordService(db,new SiteOptions{ShowWar=true});
        var request=new RecordRequest{Role="pitcher",View="basic",Year=year,PageSize=50,SortBy="War"};
        var expected=PitcherRecordRoomRowFactory.BuildBasic(await analytics.GetSnapshotAsync(new GameQuery{SeasonYear=year},league));
        var page=await service.QueryAsync(request,CancellationToken.None);
        var prop=typeof(PitcherBasicRecordRow).GetProperty("War")!;
        foreach(var row in page.Rows)
        {
            var match=expected.First(x=>x.Pcode==row.EntityCode && (x.TeamCode??"-")==row.Cells["TeamCode"]);
            Require(row.Cells["War"]==ViewRegistry.Display(prop,prop.GetValue(match)),"HTTP DTO WAR formatting parity "+row.EntityCode);
        }
        var again=await service.QueryAsync(request,CancellationToken.None);
        Require(again.Cached,"repeat-result cache hit");
        Require(!page.Warnings.Any(w=>w.Contains("소스 미확인")),"no legacy WAR placeholder");
        Console.WriteLine("PARITY_ALL_PASS (same-source equivalence; NOT independent baseball-math certification)");
    }

    private static void Same<T>(T expected,T actual,string name) => Require(JsonSerializer.Serialize(expected,Json)==JsonSerializer.Serialize(actual,Json),name);

    public static async Task SelfTestAsync(string path)
    {
        // Intended for a sealed copy or generated sample, never a live desktop file.
        var before=FileHash(path);
        await CompareDesktopAndWebAsync(path);
        Require(FileHash(path)==before,"read-only query leaves database bytes unchanged");
        var war=typeof(NaverRelay.Infrastructure.Sqlite.DatabaseAnalyticsService).Assembly.GetType("NaverRelay.Infrastructure.Sqlite.KboPitcherWarMath")!;
        var target=(double)war.GetMethod("ComputeTargetPitcherWar")!.Invoke(null,new object[]{720,0.294,0.43})!;
        Require(Math.Abs(target-127.5552)<1e-9,"720-game target arithmetic (uploaded policy)");
        var dir=Path.Combine(Path.GetTempPath(),"saber-quota-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var opt=new SiteOptions{StateDirectory=dir,DailyQueries=1,DailyRows=50};
            new QuotaStore(opt).Consume("127.0.0.1",50);
            bool blocked=false;
            try { new QuotaStore(opt).Consume("127.0.0.1",1); }
            catch(RequestError e) { blocked=e.Code=="DAILY_QUOTA"; }
            Require(blocked,"quota survives store re-open");
        }
        finally { SqliteConnection.ClearAllPools(); if(Directory.Exists(dir))Directory.Delete(dir,true); }
        var gateOptions=new SiteOptions{ConcurrentQueries=1,QuerySeconds=1};
        using var gate=new QueryGate(gateOptions);
        bool timedOut=false;
        try { await gate.RunAsync(async t=>{await Task.Delay(5000,t);return 1;},CancellationToken.None); }
        catch(RequestError e){timedOut=e.Code=="QUERY_TIMEOUT";}
        Require(timedOut,"query gate timeout");
        var ip=typeof(PitcherBasicRecordRow).GetProperty("InningsPitched")!;
        Require(Math.Abs(ViewRegistry.Threshold(ip,6.2)-(6+2.0/3))<1e-9,"innings threshold 6.2 means 20 outs");
        Console.WriteLine("SELF_TEST_ALL_PASS");
    }

    private static string FileHash(string path)
    {
        using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));
    }

    public static async Task HttpCheckAsync(string baseUrl,bool rateOnly=false)
    {
        var uri=new Uri(baseUrl);
        if(!uri.IsLoopback) throw new ArgumentException("Automated smoke checks are limited to loopback hosts.");
        using var handler=new HttpClientHandler { CookieContainer=new CookieContainer(),AllowAutoRedirect=false };
        using var client=new HttpClient(handler){BaseAddress=uri,Timeout=TimeSpan.FromMinutes(2)};
        string token="";
        async Task<JsonElement> Session()
        {
            using var response=await client.GetAsync("/api/session");response.EnsureSuccessStatusCode();
            var json=await response.Content.ReadFromJsonAsync<JsonElement>();token=json.GetProperty("csrfToken").GetString()!;return json;
        }
        async Task<HttpResponseMessage> Post(string route,object body,bool csrf=true,string? origin=null)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(body)};
            if(csrf)request.Headers.Add("X-CSRF-TOKEN",token);
            if(origin is not null)request.Headers.Add("Origin",origin);
            return await client.SendAsync(request);
        }
        await Session();
        using(var r=await client.GetAsync("/api/catalog"))Require(r.IsSuccessStatusCode,"public catalog accessible");
        if(rateOnly)
        {
            bool limited=false;
            for(int i=0;i<12;i++) { using var r=await client.GetAsync("/api/schema/batter/basic");if((int)r.StatusCode==429){limited=true;break;} }
            Require(limited,"IP rate limit returns 429");Console.WriteLine("RATE_TEST_ALL_PASS");return;
        }
        using(var r=await client.GetAsync("/"))
        {
            Require(r.IsSuccessStatusCode,"frontend index served");
            Require((await r.Content.ReadAsStringAsync()).Contains("시즌기록실"),"frontend record-room shell");
            Require(r.Headers.Contains("Content-Security-Policy"),"CSP header");
        }
        // Check the actual public assets, not just /api/health. This catches a
        // missing CSS/JS bundle even when the API starts successfully.
        foreach (var asset in new[] { "index.html", "app.css", "app.js" })
        {
            var expectedPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", asset);
            Require(File.Exists(expectedPath), "local frontend file exists " + asset);
            using var response = await client.GetAsync("/" + asset);
            Require(response.IsSuccessStatusCode, "frontend asset served " + asset);
            var body = await response.Content.ReadAsByteArrayAsync();
            var expected = await File.ReadAllBytesAsync(expectedPath);
            Require(SHA256.HashData(body).SequenceEqual(SHA256.HashData(expected)), "frontend asset content matches build " + asset);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            Require(asset switch
            {
                "index.html" => mediaType == "text/html",
                "app.css" => mediaType == "text/css",
                "app.js" => mediaType is "text/javascript" or "application/javascript",
                _ => false
            }, "frontend content type " + asset);
        }
        JsonElement catalog;
        using(var r=await client.GetAsync("/api/catalog")){r.EnsureSuccessStatusCode();catalog=await r.Content.ReadFromJsonAsync<JsonElement>();}
        var year=catalog.GetProperty("years").EnumerateArray().First().GetInt32();
        var request=new RecordRequest { Year=year,PageSize=25 };
        using(var r=await Post("/api/query",request,false))Require((int)r.StatusCode==400,"missing CSRF = 400");
        using(var r=await Post("/api/query",request,true,"https://untrusted.invalid"))Require((int)r.StatusCode==403,"cross-origin POST = 403");
        using(var r=await Post("/api/query",request with{PageSize=101}))Require((int)r.StatusCode==400,"server enforces page-size cap");
        using(var r=await Post("/api/query",request with{SortBy="Name;DROP TABLE Games"}))Require((int)r.StatusCode==400,"sort column allow-list");
        foreach(var route in new[]{"/appsettings.json","/web-readonly.db","/data/web-readonly.db","/server/src/NaverSabermetrics.Web/Program.cs","/api/stats/batters"})
        { using var r=await client.GetAsync(route);Require((int)r.StatusCode==404,"private/legacy path not exposed "+route); }
        var metrics=new List<object>();
        foreach(var v in ViewRegistry.Views)
        {
            var q=request with{Role=v.Role=="constants"?"batter":v.Role,View=v.Key,Room=v.Role=="constants"?"constants":"season"};
            using var r=await Post("/api/query",q);
            var payload=await r.Content.ReadAsStringAsync();
            Require(r.IsSuccessStatusCode,$"HTTP {v.Role}/{v.Key}: {(int)r.StatusCode} {(!r.IsSuccessStatusCode?payload:"")}");
            using var doc=JsonDocument.Parse(payload); var page=doc.RootElement;
            Require(page.GetProperty("rows").GetArrayLength()<=25,"page bound "+v.Key);
            Require(page.GetProperty("formulaVersion").GetString()==RecordService.FormulaVersion,"formula identity "+v.Key);
            metrics.Add(new{role=v.Role,view=v.Key,elapsedMs=page.GetProperty("elapsedMs").GetInt64(),rows=page.GetProperty("rows").GetArrayLength()});
        }
        using(var r=await Post("/api/query",request with{Outs=2,Balls=1,Strikes=1}))Require(r.IsSuccessStatusCode,"situation query");
        using(var r=await Post("/api/query",request with{Role="pitcher",View="value",Outs=2}))Require((int)r.StatusCode==400,"undefined situation WAR rejected");
        using(var r=await Post("/api/query",request with{Conditions=new(){new("PA","gte",10)},SortBy="PA",Descending=true}))
        {
            r.EnsureSuccessStatusCode();var page=await r.Content.ReadFromJsonAsync<JsonElement>();
            double previous=double.PositiveInfinity;
            foreach(var row in page.GetProperty("rows").EnumerateArray())
            { var pa=double.Parse(row.GetProperty("cells").GetProperty("PA").GetString()!,CultureInfo.InvariantCulture);Require(pa>=10 && pa<=previous,"server condition + descending sort");previous=pa; }
        }
        Console.WriteLine("HTTP_TIMINGS="+JsonSerializer.Serialize(metrics,Json));
        Console.WriteLine("HTTP_TEST_ALL_PASS");
    }
}
