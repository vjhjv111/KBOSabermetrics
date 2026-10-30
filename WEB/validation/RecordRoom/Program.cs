using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverSabermetrics.Web;

var path = Path.Combine(Path.GetTempPath(), "record-room-" + Guid.NewGuid() + ".db");
try
{
    var db = new DatabaseCacheService(path);
    await db.InitializeAsync();
    using (var con = new SqliteConnection($"Data Source={path}"))
    {
        con.Open();
        void Insert(string table, Dictionary<string,object> values)
        {
            using var schema = con.CreateCommand();
            schema.CommandText = $"PRAGMA table_info({table})";
            using (var r = schema.ExecuteReader())
                while(r.Read())
                    if(r.GetInt32(3)==1 && r.IsDBNull(4) && !values.ContainsKey(r.GetString(1)))
                        values[r.GetString(1)] = r.GetString(2).Contains("TEXT") ? "" : 0;
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"INSERT INTO {table} ({string.Join(',',values.Keys)}) VALUES ({string.Join(',',values.Keys.Select((_,i)=>"$p"+i))})";
            foreach(var (v,i) in values.Values.Select((v,i)=>(v,i))) cmd.Parameters.AddWithValue("$p"+i,v);
            cmd.ExecuteNonQuery();
        }
        Insert("Games", new() { ["GameId"]="20261001LGHH", ["SeasonYear"]=2026, ["GameDate"]="2026-10-01", ["RoundCode"]="kbo_r", ["HomeTeamCode"]="LG", ["AwayTeamCode"]="HH", ["HomeScore"]=4, ["AwayScore"]=2 });
        using var profiles = con.CreateCommand();
        profiles.CommandText = "CREATE TABLE IF NOT EXISTS OfficialPlayerProfiles (Pcode TEXT PRIMARY KEY, DraftText TEXT)";
        profiles.ExecuteNonQuery();
        for(var i=1;i<=3;i++)
        {
            var code=i.ToString();
            Insert("Players", new() { ["Pcode"]=code,["Name"]="선수"+i,["LatestTeam"]="LG" });
            Insert("OfficialPlayerProfiles",new() { ["Pcode"]=code,["DraftText"]=i==1?"신인 지명":i==2?"자유선발":"아시아쿼터" });
            Insert("PlateAppearances",new() { ["PlateAppearanceId"]="pa"+i,["GameId"]="20261001LGHH",["BatterPcode"]=code,["PitcherPcode"]="2",["BattingTeamCode"]="LG",["FieldingTeamCode"]="HH",["Inning"]=i,["SequenceNumber"]=i,["BeforeOuts"]=0,["BeforeHomeScore"]=0,["AfterHomeScore"]=1,["RunsScored"]=1,["WasRecognized"]=1,["IsOfficial"]=1,["IsHit"]=1,["CountsAsAtBat"]=1,["TotalBases"]=1,["ResultType"]=1 });
            Insert("BatterGameStats",new() { ["GameId"]="20261001LGHH",["Pcode"]=code,["Name"]="선수"+i,["TeamCode"]="LG",["PA"]=4*i,["AB"]=4*i,["H"]=i,["Singles"]=i,["TB"]=i,["Runs"]=i,["OutsRecorded"]=3*i,["DhPa"]=4*i });
            Insert("PitcherGameStats",new() { ["GameId"]="20261001LGHH",["Pcode"]=code,["Name"]="선수"+i,["TeamCode"]="LG",["TBF"]=4*i,["HasFinalLine"]=1,["InningsOuts"]=3*i,["FinalSO"]=i,["EarnedRuns"]=i,["RunsAllowed"]=i,["IsStarter"]=1,["PaOuts"]=3*i,["PaSO"]=i });
        }
        profiles.CommandText="UPDATE BatterGameStats SET H=H*H, Singles=Singles*Singles, TB=TB*TB";
        profiles.ExecuteNonQuery();
        Insert("Games", new() { ["GameId"]="20261002LGHH", ["SeasonYear"]=2026, ["GameDate"]="2026-10-02", ["RoundCode"]="kbo_r", ["HomeTeamCode"]="LG", ["AwayTeamCode"]="HH", ["HomeScore"]=4, ["AwayScore"]=2 });
        Insert("PitcherGameStats",new() { ["GameId"]="20261002LGHH",["Pcode"]="2",["Name"]="선수2",["TeamCode"]="LG",["TBF"]=12,["HasFinalLine"]=1,["InningsOuts"]=9,["FinalSO"]=10,["EarnedRuns"]=0,["RunsAllowed"]=0,["IsReliever"]=1,["PaOuts"]=9,["PaSO"]=10 });
    }
    var service = new RecordService(db,new SiteOptions());
    var request = new RecordRequest { Room="team", Year=2026, Team="LG" };
    void Check(bool ok,string label) { if(!ok)throw new Exception(label); Console.WriteLine("PASS "+label); }
    async Task<TablePage> Query(RecordRequest r) => await service.QueryAsync(r,default);
    foreach(var (nationality,pa,so) in new[] { ("",24,16),("국내",4,1),("외국인",8,12),("아시아쿼터",12,3),("외국인+아쿼",20,15) })
    {
        var batting = await Query(request with { Nationality=nationality });
        var pitching = await Query(request with { Role="pitcher",Nationality=nationality });
        Check(batting.Rows.Single().Cells["PA"]==pa.ToString(),"team PA filtered: "+nationality);
        Check(pitching.Rows.Single().Cells["Strikeouts"]==so.ToString(),"team SO filtered: "+nationality);
        var combined = await Query(request with { Role="combined",Nationality=nationality });
        var row=combined.Rows.Single().Cells;
        double Number(string key)=>double.Parse(row[key],CultureInfo.InvariantCulture);
        Check(Math.Abs(Number("BatterWar")+Number("PitcherWar")-Number("TotalWar"))<=0.011,"combined sum: "+nationality);
    }
    var players = await Query(request with { Room="season",Role="combined" });
    Check(players.Rows.Count==3,"two-way players merge into one row");
    var foreign = await Query(request with { Room="season",Role="combined",Nationality="외국인" });
    Check(foreign.Rows.Single().EntityCode=="2","season combined nationality");
    var hidden = await new RecordService(db,new SiteOptions { ShowWar=false }).QueryAsync(request with { Role="combined" },default);
    Check(hidden.Rows.Single().Cells["TotalWar"]=="-","WAR visibility policy");
    foreach (var alias in new[] { "WrcPlus", "wrcplus", "wrc+", "WRC+", "WrcPlusParkAdjusted" })
    foreach (var descending in new[] { true, false })
    {
        var botRequest = request with { Room="season",SortBy=BotRecordCompatibility.NormalizeStat("batter",alias),Descending=descending,QualificationPercent=50 };
        Check(botRequest.SortBy=="WrcPlusParkAdjusted","bot normalizes wRC+ alias: "+alias);
        var webPage = await Query(botRequest);
        var botPage = BotRecordCompatibility.AddCompatibilityCells(webPage, alias);
        Check(botPage.Rows.All(x=>x.Cells["WrcPlus"]!="-" && x.Cells["WrcPlus"]==x.Cells["WrcPlusParkAdjusted"] && x.Cells[alias]==x.Cells["WrcPlus"]),"bot legacy cell returns adjusted numeric wRC+");
        Check(webPage.Rows.All(x=>!x.Cells.ContainsKey("WrcPlus")),"bot alias does not mutate website response");
        var values = botPage.Rows.Select(x=>double.Parse(x.Cells["WrcPlus"],CultureInfo.InvariantCulture)).ToArray();
        Check(values.Distinct().Count()==3,"wRC+ fixture has distinct player values");
        Check(botPage.Rows.Select(x=>x.EntityCode).SequenceEqual(descending?new[] { "3","2","1" }:new[] { "1","2","3" }),"bot ranks players by adjusted wRC+");
        Check(values.SequenceEqual(descending?values.OrderByDescending(x=>x):values.OrderBy(x=>x)),"bot value and sort agree");
        using var json=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(botPage,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Check(json.RootElement.GetProperty("rows")[0].GetProperty("cells").GetProperty("WrcPlus").GetString()!="-","serialized legacy cell survives JSON naming policy");
    }
    Check(BotRecordCompatibility.NormalizeStat("batter","ops")=="OPS","bot accepts case insensitive stat keys");
    var questionSite=new SiteOptions { DatabasePath=path,StateDirectory=path+".state",QuerySeconds=120 };
    var questionPlanner=new WrcQuestionPlanner();
    using (var questionGate=new QueryGate(questionSite))
    {
        var questions=new RecordQuestionService(new RecordQuestionOptions { Enabled=true },questionSite,db,service,questionGate,questionPlanner);
        foreach(var alias in new[] { "우르크","wrc+","wRC＋","wrc plus","wrc플러스" })
        {
            var result=await questions.AskAsync($"2026년 LG 타석수 무관 {alias} 상위 3명","local",default,authenticatedBot:true);
            Check(questionPlanner.LastQuestion==$"2026년 LG 타석수 무관 WrcPlusParkAdjusted 상위 3명","natural-language planner receives known metric before support decision");
            var reply=BotQuestionEndpoint.Format(result);
            Check(reply.Contains("wRC+") && reply.Contains("선수3") && !reply.Contains("미지원"),"bot natural-language reply contains wRC+ records: "+alias);
        }
        questionPlanner.Supported=false;
        var rejected=System.Text.Json.JsonSerializer.SerializeToElement(await questions.AskAsync("2026년 LG 우르크 비교해줘","local",default,authenticatedBot:true));
        Check(rejected.GetProperty("clarification").GetBoolean(),"unsupported natural-language constraints remain rejected");
    }
    Directory.Delete(questionSite.StateDirectory,true);
    var analytics = new DatabaseAnalyticsService(db);
    var league = await db.GetLeagueReferenceAsync();
    foreach(var room in new[] { "season", "career", "team" })
    foreach(var nationality in room=="team" ? new[] { "", "외국인" } : new[] { "" })
    {
        var scope = request with { Room=room,Role="pitcher",Nationality=nationality };
        var q=scope.ToQuery(await db.GetCatalogAsync());
        if(nationality!="") q=q with { SelectedPlayerCodes=["2"],SelectedPlayersArePitchers=true };
        foreach(var starts in new[] { true,false })
        {
            var expectedSnapshot=await analytics.GetWebRoleSnapshotAsync(q with { PitcherStartsOnly=starts },league,true);
            var expected=PitcherRecordRoomRowFactory.BuildValue(expectedSnapshot,league)
                .Single(x=>room=="team" || x.Pcode=="2");
            var result=await Query(scope with { View=starts?"starter":"reliever" });
            var actual=result.Rows.Single(x=>room=="team" || x.EntityCode=="2").Cells[starts?"StarterWar":"ReliefWar"];
            Check(actual==expected.FanGraphsWar!.Value.ToString("0.00",CultureInfo.InvariantCulture),$"{room} {nationality} {(starts?"starter":"relief")} WAR uses selected appearances");
            var full=await Query(scope with { View="value" });
            Check(actual!=full.Rows.Single(x=>room=="team" || x.EntityCode=="2").Cells["FanGraphsWar"],"mixed-role WAR differs from full WAR");
        }
    }
}
finally
{
    SqliteConnection.ClearAllPools();
    File.Delete(path);
    File.Delete(path+"-wal");
    File.Delete(path+"-shm");
}

sealed class WrcQuestionPlanner : IRecordQuestionPlanner
{
    public bool Ready=>true;
    public bool Supported=true;
    public string LastQuestion="";
    public Task<QuestionPlan> PlanAsync(string question,int[] years,CancellationToken ct)
    {
        LastQuestion=question;
        return Task.FromResult(new QuestionPlan {
            Supported=Supported && question.Contains("WrcPlusParkAdjusted"),Clarification="지원하지 않는 조건입니다.",
            Role="batter",Year=2026,StartDate=null,EndDate=null,Team="LG",Player=null,
            Metric="우르크",Descending=true,MinimumVolume=0,Inning=null,Filters=[],Limit=3
        });
    }
}
