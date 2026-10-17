using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverSabermetrics.Web;

var path = Path.Combine(Path.GetTempPath(), "plus-stats-" + Guid.NewGuid() + ".db");
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
            Insert("BatterGameStats",new() { ["GameId"]="20261001LGHH",["Pcode"]=code,["Name"]="선수"+i,["TeamCode"]="LG",["PA"]=4*i,["AB"]=4*i,["H"]=i,["Singles"]=i,["TB"]=i,["Runs"]=i,["OutsRecorded"]=3*i,["DhPa"]=4*i });
            Insert("PitcherGameStats",new() { ["GameId"]="20261001LGHH",["Pcode"]=code,["Name"]="선수"+i,["TeamCode"]="LG",["TBF"]=4*i,["HasFinalLine"]=1,["InningsOuts"]=3*i,["FinalSO"]=i,["EarnedRuns"]=i,["RunsAllowed"]=i,["IsStarter"]=1,["PaOuts"]=3*i,["PaSO"]=i });
        }
        Insert("Games", new() { ["GameId"]="20261002LGHH", ["SeasonYear"]=2026, ["GameDate"]="2026-10-02", ["RoundCode"]="kbo_r", ["HomeTeamCode"]="LG", ["AwayTeamCode"]="HH", ["HomeScore"]=4, ["AwayScore"]=2 });
        Insert("PitcherGameStats",new() { ["GameId"]="20261002LGHH",["Pcode"]="2",["Name"]="선수2",["TeamCode"]="LG",["TBF"]=12,["HasFinalLine"]=1,["InningsOuts"]=9,["FinalSO"]=10,["EarnedRuns"]=0,["RunsAllowed"]=0,["IsReliever"]=1,["PaOuts"]=9,["PaSO"]=10 });
    }

    void Check(bool ok,string name) { if(!ok)throw new Exception(name); Console.WriteLine("PASS "+name); }
    bool Near(double? x,double y)=>x.HasValue && Math.Abs(x.Value-y)<1e-10;
    Check(Near(PlusStatsMath.Index(.3,.25),120),"index uses a ratio, not percentage points");
    Check(PlusStatsMath.Index(0,.25)==0 && PlusStatsMath.Index(.3,0)==null,"zero numerator versus zero denominator");
    Check(Near(PlusStatsMath.EraMinus(4,4,110),90),"hitter park lowers ERA-");
    Check(Near(PlusStatsMath.EraMinus(4,4,90),110),"pitcher park raises ERA-");
    Check(PlusStatsMath.EraMinus(4,4,null)==null,"missing park is not silently neutral");
    var selected=new GameQuery {SeasonYear=2026, TeamCode="LG",OpponentCode="HH",InningFilter="9",SelectedPlayerCodes=["2"],PitcherStartsOnly=true};
    var reference=DatabaseAnalyticsService.PlusReferenceQuery(selected);
    Check(reference.SeasonYear==2026 && reference.TeamCode==null && reference.OpponentCode==null && !reference.HasSituationFilters && reference.SelectedPlayerCodes==null && reference.PitcherStartsOnly==null,"reference ignores team, opponent, player, situation and starting role");
    var analytics=new DatabaseAnalyticsService(db);
    var league=await db.GetLeagueReferenceAsync();
    var q=new GameQuery{SeasonYear=2026};
    var full=await analytics.GetWebRoleSnapshotAsync(q,league,false,includePlusStats:true);
    Check(full.BatterSabermetrics.Count==3 && full.BatterSabermetrics.All(x=>Near(x.AveragePlus,100)),"all hitters at league AVG produce 100");
    Check(full.BatterSabermetrics.All(x=>x.IsoPlus is null),"league zero ISO produces null");
    var filtered=await analytics.GetWebRoleSnapshotAsync(q with {SelectedPlayerCodes=["2"]},league,false,includePlusStats:true);
    Check(filtered.BatterSabermetrics.Count==1 && Near(filtered.BatterSabermetrics[0].AveragePlus,100),"player filter preserves league baseline");
    var cached=await analytics.GetWebRoleSnapshotAsync(q,league,false,includePlusStats:true);
    Check(cached.BatterSabermetrics.All(x=>Near(x.AveragePlus,100)),"cached snapshot preserves plus values");
    var pitchers=await analytics.GetWebRoleSnapshotAsync(q,league,true,includePlusStats:true);
    var avgK=16.0/36;
    var p2=pitchers.PitcherSabermetrics.Single(x=>x.Pcode=="2");
    Check(Near(p2.StrikeoutRatePlus,100*(12.0/20)/avgK),"pitcher K%+ uses pooled league TBF and PA strikeouts");
    var service=new RecordService(db,new SiteOptions());
    var table=await service.QueryAsync(new RecordRequest{Room="season",Role="batter",View="advanced",Year=2026},default);
    Check(table.Rows.Count==3 && table.Rows.All(x=>x.Cells["AveragePlus"]=="100"),"HTTP-facing table renders AVG+ as integer");
    var pitching=await service.QueryAsync(new RecordRequest{Room="season",Role="pitcher",View="advanced",Year=2026},default);
    Check(pitching.Rows.Single(x=>x.EntityCode=="2").Cells["StrikeoutRatePlus"]=="135","HTTP-facing pitching plus rendered correctly");
    Check(pitching.Rows.All(x=>x.Cells["EraMinus"]=="-"),"missing park produces dash in table");
    using(var update=new SqliteConnection($"Data Source={path}"))
    {
        update.Open(); using var cmd=update.CreateCommand();
        cmd.CommandText="UPDATE Games SET Stadium='test-park'";cmd.ExecuteNonQuery();
    }
    await db.SaveComputedAsync("plus-run-parks-v1",new List<ParkFactorByHitTypeRow>{new(){Year=2026,Stadium="test-park",RunParkFactor=110}});
    var adjusted=await analytics.GetWebRoleSnapshotAsync(q with {EndDate=new DateTime(2026,12,31)},league,true,includePlusStats:true);
    Check(Near(adjusted.PitcherSabermetrics.Single(x=>x.Pcode=="2").EraMinus,54),"ERA- integrates official ER/outs, period league ERA and actual park exposure");
    foreach(var label in new[]{"WalkRatePlus","StrikeoutRatePlus"})
        Check(ViewRegistry.Kind(typeof(BatterAdvancedRecordRow).GetProperty(label)!)=="integer","percent-plus index is not formatted as percent: "+label);
}
finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
