using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
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
            Insert("BatterGameStats",new() { ["GameId"]="20261001LGHH",["Pcode"]=code,["Name"]="선수"+i,["TeamCode"]="LG",["PA"]=4*i,["AB"]=4*i,["H"]=i,["Singles"]=i,["TB"]=i,["Runs"]=i,["OutsRecorded"]=3*i,["DhPa"]=4*i });
            Insert("PitcherGameStats",new() { ["GameId"]="20261001LGHH",["Pcode"]=code,["Name"]="선수"+i,["TeamCode"]="LG",["TBF"]=4*i,["HasFinalLine"]=1,["InningsOuts"]=3*i,["FinalSO"]=i,["EarnedRuns"]=i,["RunsAllowed"]=i,["IsStarter"]=1,["PaOuts"]=3*i,["PaSO"]=i });
        }
    }
    var service = new RecordService(db,new SiteOptions());
    var request = new RecordRequest { Room="team", Year=2026, Team="LG" };
    void Check(bool ok,string label) { if(!ok)throw new Exception(label); Console.WriteLine("PASS "+label); }
    async Task<TablePage> Query(RecordRequest r) => await service.QueryAsync(r,default);
    foreach(var (nationality,pa,so) in new[] { ("",24,6),("국내",4,1),("외국인",8,2),("아시아쿼터",12,3),("외국인+아쿼",20,5) })
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
}
finally
{
    SqliteConnection.ClearAllPools();
    File.Delete(path);
    File.Delete(path+"-wal");
    File.Delete(path+"-shm");
}
