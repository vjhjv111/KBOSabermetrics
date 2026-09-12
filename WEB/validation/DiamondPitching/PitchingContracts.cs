using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverSabermetrics.Web;

internal static class PitchingContracts
{
    public static void Run(string lib, string output)
    {
        var checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidDataException(name); }
        void Near(double wanted, double actual, string name) => Check(Math.Abs(wanted-actual)<1e-8, $"{name}: expected {wanted:R}, got {actual:R}");
        string Json(object? value) => JsonSerializer.Serialize(value, DiamondJson.Options);
        JsonElement Body(object value) => JsonSerializer.SerializeToElement(value, DiamondJson.Options);
        var directory = Path.Combine(output,"contracts-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var sourceDb=Path.Combine(directory,"synthetic.db"); RosterFixture.Create(sourceDb); AddMeasuredPitches(sourceDb);
        var roster = new DiamondRosterService(sourceDb); var profile=roster.SelectPitching(2026,"2026:201");
        Check(profile.Source=="observed", "measured source");
        Check(profile.TotalPitchCount==36 && profile.MeasuredPitchCount==35 && profile.UsablePitchCount==33,"full/measured/usable counts include unsupported and incomplete pitches correctly");
        Check(profile.Bins.Sum(x=>x.Count)==33 && profile.Bins.Sum(x=>x.HitByPitchCount)==1,"joint bins preserve mass and terminal HBP");
        Near(1d/36, profile.HitByPitchRate!.Value,"official HBP divided by all pitches");
        Check(profile.HitByPitchCount==1,"one official HBP");
        var ordinary=profile.Bins.Single(x=>x.Type=="fastball" && x.HitByPitchCount==0);
        Near(.5,ordinary.X,"raw feet conversion without mirroring"); Near(0,ordinary.Y,"height normalized using observed zone");
        Near((20*140.5+280)/22,ordinary.Velocity,"joint bin average speed"); Check(ordinary.Count==22,"normal final/cross-pitcher PA pitches count");
        var wide=profile.Bins.Single(x=>x.Type=="slider"); Near(3,wide.X,"wide measured pitches never clamp to 2");
        Check(wide.BatterHand=="L" && wide.Balls==3 && wide.Strikes==2 && wide.Count==10,"stance and pre-pitch count retained");
        var hbp=profile.Bins.Single(x=>x.HitByPitchCount>0); Near(-3,hbp.X,"HBP coordinate preserved with sign");
        Check(hbp.Count==1,"only final pitch belongs to HBP bin");
        var sparse=roster.SelectPitching(2026,"2026:202");
        Check(sparse.Source=="default" && sparse.Bins.Count==0 && sparse.UsablePitchCount==1,"small sample has explicit fallback");
        Check(profile.Revision==roster.Get(2026).Revision,"private profile and public roster revision consistent");
        if(profile.Bins is DiamondPitchLocationBin[] detachedBins)detachedBins[0]=detachedBins[0] with {Count=999999};
        Check(roster.SelectPitching(2026,"2026:201").Bins.Sum(x=>x.Count)==33,"cached profile has an independent bin array");
        try { roster.SelectPitching(2026,"2025:201"); throw new InvalidDataException("wrong season accepted"); }
        catch(DiamondInputError e) { Check(e.Status==400,"invalid pitcher season status"); }
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { roster.SelectPitching(2026,"2026:201",cancelled.Token); throw new InvalidDataException("cancelled profile accepted"); }
            catch(OperationCanceledException) { checks++; }
        }

        var legacy=new DiamondData(lib); uint seed=123456789;
        double Random() { seed=unchecked(seed*1664525+1013904223); return seed/4294967296d; }
        DiamondRosterSelection Selection(string hand) => new(2026,"2026-09-11","fixture",
            new DiamondBatter {Id="b"+hand,PlayerId="b"+hand,Pa=500,Ab=450,H=135,So=80,Avg=.3,Slg=.5,Profile=new(){Bats=hand,Throws="R"}},
            new DiamondPitcher {Id="p",PlayerId="p",Tbf=500,Bb=40,So=130,Profile=new(){Throws="R",Bats="R"},Arsenal=[new("fastball",145,50),new("curve",120,10),new("slider",132,20),new("cutter",138,10),new("changeup",125,10)]});
        DiamondPitchingProfile Profile(params DiamondPitchLocationBin[] bins) => new(){Season=2026,PitcherId="p",Source="observed",TotalPitchCount=4000,MeasuredPitchCount=4000,UsablePitchCount=4000,HitByPitchCount=0,HitByPitchRate=0,Bins=bins,Revision="fixture"};
        var joint=Profile(new("fastball",-.25,.1,145,"R",0,0,900,0),new("curve",.2,-.5,120,"R",0,0,100,0),
            new("slider",.35,-.15,132,"L",0,0,1000,0),new("cutter",-.1,.5,138,"R",3,2,1000,0),new("changeup",.25,-.3,125,"L",3,2,1000,0));
        foreach(var (hand,balls,strikes,wanted) in new[]{("R",0,0,"weighted"),("L",0,0,"slider"),("R",3,2,"cutter"),("L",3,2,"changeup")})
        {
            var selection=Selection(hand); var data=legacy.ForRoster(selection); var engine=new DiamondEngine(data,Random);
            var game=new DiamondGame {Batter=selection.Batter.Id,Pitcher="p",Roster=selection,PitchingProfile=joint,Mode="ai",HostRole="batter",Balls=balls,Strikes=strikes};
            var accepted=0; var fastballs=0;
            for(var i=0;i<1000;i++)
            {
                var pitch=engine.CreateAiPitch(game,1700000000000);
                if(pitch.BodyHit!=null) continue;
                accepted++; if(pitch.Type=="fastball")fastballs++;
                if(wanted!="weighted") Check(pitch.Type==wanted,"exact hand/count joint pitch type");
                var bin=joint.Bins.Single(x=>x.Type==pitch.Type && x.BatterHand==hand && x.Balls==balls);
                Near(bin.X,pitch.Target.X,"measured X has no added scatter"); Near(bin.Y,pitch.Target.Y,"measured Y has no added scatter"); Near(bin.Velocity,pitch.Velocity,"measured velocity stays joint with location");
            }
            Check(accepted>990,"rare HBP does not dominate common bins");
            if(wanted=="weighted")Check((double)fastballs/accepted is > .86 and < .94,"count-weighted 90:10 distribution");
        }
        var right=Selection("R"); var rightData=legacy.ForRoster(right); var rightEngine=new DiamondEngine(rightData,Random);
        var wideGame=new DiamondGame {Batter=right.Batter.Id,Pitcher="p",Roster=right,PitchingProfile=Profile(new DiamondPitchLocationBin("fastball",3.1,-.4,149,"R",0,0,4000,0))};
        for(var i=0;i<25;i++)
        { var pitch=rightEngine.CreateAiPitch(wideGame,1700000000000); if(pitch.BodyHit==null) { Near(3.1,pitch.Target.X,"wide safe endpoint is not clamped");Near(149,pitch.Velocity,"wide pitch measured speed"); } }
        var rateProfile=new DiamondPitchingProfile {Season=2026,PitcherId="p",Source="observed",TotalPitchCount=10000,MeasuredPitchCount=10000,UsablePitchCount=10000,HitByPitchCount=38,HitByPitchRate=.0038,
            Bins=[new("fastball",-.2,0,145,"R",0,0,9962,0),new("fastball",-2.7,.2,145,"R",0,0,38,38)]};
        var rateGame=new DiamondGame {Batter=right.Batter.Id,Pitcher="p",Roster=right,PitchingProfile=rateProfile};
        var hbpCount=0; var zoneHits=0;
        for(var i=0;i<10000;i++)
        {
            rateGame.Pitch=rightEngine.CreateAiPitch(rateGame,1700000000000); var pitch=rateGame.Pitch;
            var result=rightEngine.EvaluatePitch(rateGame,null,(long)(pitch.ReleaseAt+pitch.FlightMs+1000));
            if(result.Outcome=="HBP") { hbpCount++; Check(pitch.BodyHit!=null,"HBP requires actual swept collision"); Check(Math.Abs(pitch.Target.X)>1||Math.Abs(pitch.Target.Y)>1,"HBP outside strike zone"); }
            if(pitch.BodyHit!=null&&Math.Abs(pitch.Target.X)<=1&&Math.Abs(pitch.Target.Y)<=1)zoneHits++;
        }
        Check(hbpCount is >=15 and <=75,$"per-pitch .38% HBP distribution, observed {hbpCount}/10000");
        Check(zoneHits==0,"ordinary measured AI pitches never hit body inside zone");

        long now=1700000000000; var state=Path.Combine(directory,"matches");
        var games=new DiamondGameService(state,lib,()=>now,()=>.5,roster);
        var room=games.Post(Body(new{op="create",season=2026,batter="2026:101",pitcher="2026:201",mode="ai",role="batter",pace="full"}),"host","tests");
        Check(room.AiPitching?.Source=="observed","match public summary reports observed profile");
        Check(!Json(room).Contains("\"bins\"",StringComparison.Ordinal)&&!Json(room).Contains("\"pitchingProfile\"",StringComparison.Ordinal),"public view omits private pitch bins");
        string SavedProfile()
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=games.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();using var command=db.CreateCommand();
            command.CommandText="SELECT State FROM Matches WHERE Code=$code";command.Parameters.AddWithValue("$code",room.Code);
            using var json=JsonDocument.Parse((string)command.ExecuteScalar()!);return json.RootElement.GetProperty("pitchingProfile").GetRawText();
        }
        var saved=SavedProfile(); Check(JsonDocument.Parse(saved).RootElement.GetProperty("bins").GetArrayLength()>0,"private pitch profile persisted in match state");
        RosterFixture.ChangeSeason2026(sourceDb); var updated=roster.SelectPitching(2026,"2026:201"); Check(updated.Revision!=profile.Revision,"profile invalidates on DB revision");
        var restarted=new DiamondGameService(state,lib,()=>now,()=>.5,new DiamondRosterService(Path.Combine(directory,"missing-source.db")));
        var started=restarted.Post(Body(new{op="ready",code=room.Code,previousPitch=0}),"host","tests");
        Check(started.Pitch is not null,"AI match can pitch after restart without source DB"); Check(SavedProfile()==saved,"persisted pitch distribution does not change after DB update/restart");
        now=(long)(started.Pitch!.ReleaseAt+started.Pitch.FlightMs+1000);
        Check(restarted.Get(room.Code,"host").Pitch!.Resolved,"timeout evaluation uses persisted roster/profile");
        var stressDb=Path.Combine(directory,"stress.db");RosterFixture.Create(stressDb);AddMeasuredPitches(stressDb);AddStressPitches(stressDb);
        var stress=new DiamondRosterService(stressDb).SelectPitching(2026,"2026:201");
        Check(stress.TotalPitchCount==5038&&stress.MeasuredPitchCount==5035&&stress.UsablePitchCount==5033,"stress counts exclude nonfinite coordinates and invalid zones");
        Check(stress.Bins.Count<=126&&stress.GridSize==0,"extreme distribution reduces to bounded region groups");
        Check(stress.Bins.Sum(x=>x.Count)==5033,"coarsening preserves all usable sample mass");
        Check(stress.Bins.Where(x=>Math.Abs(x.X)<=1&&Math.Abs(x.Y)<=1).Sum(x=>x.Count)==22,"coarsening never averages outside regions into a strike");
        Check(stress.Bins.All(x=>double.IsFinite(x.X)&&double.IsFinite(x.Y)&&double.IsFinite(x.Velocity)),"coarsened locations remain finite");
        Check(stress.Bins.Sum(x=>x.HitByPitchCount)==1,"coarsening keeps ordinary and HBP pitches separate");
        Check(stress.Bins.All(x=>x.BatterHand==null&&x.Balls==-1&&x.Strikes==-1)&&!string.IsNullOrEmpty(stress.SampleNote),"reduced context is unknown and explained");
        Console.WriteLine($"PASS pitching contracts: {checks} checks; controlled HBP {hbpCount}/10000; synthetic artifacts {directory}");
    }

    private static void AddMeasuredPitches(string path)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());db.Open();
        using(var command=db.CreateCommand())
        {
            command.CommandText="""
                ALTER TABLE PitcherGameStats ADD FinalHBP INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE PitcherGameStats ADD PaHBP INTEGER NOT NULL DEFAULT 0;
                UPDATE PitcherGameStats SET FinalHBP=1 WHERE GameId='g26a' AND Pcode='201';
                UPDATE PitcherGameStats SET FinalHBP=1 WHERE GameId='g26b' AND Pcode='202';
                DROP TABLE Pitches;
                CREATE TABLE Pitches(GameId TEXT,PitcherPcode TEXT,PitchType TEXT,SpeedKmh REAL,
                    CrossPlateX REAL,CalculatedCrossPlateZ REAL,TopStrikeZone REAL,BottomStrikeZone REAL,
                    BatterStance TEXT,BallsBefore INTEGER,StrikesBefore INTEGER,PlateAppearanceId TEXT,ActualPitchIndex INTEGER,PitchEventId TEXT);
                CREATE TABLE PlateAppearances(PlateAppearanceId TEXT PRIMARY KEY,ResultType INTEGER,Status INTEGER,IsOfficial INTEGER);
                INSERT INTO PlateAppearances VALUES('hbp201',9,0,1),('cross',9,0,1);
                """;command.ExecuteNonQuery();
        }
        var number=0;
        void Pitch(string pitcher,string type,double? speed,double? x,double z,string hand,int balls,int strikes,string? pa=null,int index=1)
        {
            using var command=db.CreateCommand();command.CommandText="INSERT INTO Pitches VALUES('g26a',$p,$type,$speed,$x,$z,3,2,$hand,$balls,$strikes,$pa,$index,$id)";
            foreach(var (key,value) in new (string,object?)[]{("$p",pitcher),("$type",type),("$speed",speed),("$x",x),("$z",z),("$hand",hand),("$balls",balls),("$strikes",strikes),("$pa",pa),("$index",index),("$id","pitch"+(number++).ToString("D4"))}) command.Parameters.AddWithValue(key,value??DBNull.Value);
            command.ExecuteNonQuery();
        }
        for(var i=0;i<20;i++)Pitch("201","직구",140+i%2,8.5/24,2.5,"R",0,0);
        for(var i=0;i<10;i++)Pitch("201","슬라이더",130,8.5/4,2.25,"L",3,2);
        Pitch("201","스위퍼",135,0,2.5,"R",0,0);
        Pitch("201","직구",null,0,2.5,"R",0,0);
        Pitch("201","직구",146,null,2.5,"R",0,0);
        Pitch("201","직구",140,8.5/24,2.5,"R",0,0,"hbp201",1);
        Pitch("201","직구",140,-8.5/4,2.5,"R",1,0,"hbp201",2);
        Pitch("201","직구",140,8.5/24,2.5,"R",0,0,"cross",1);
        Pitch("202","직구",145,-8.5/4,2.5,"R",1,0,"cross",2);
    }

    private static void AddStressPitches(string path)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());db.Open();using var transaction=db.BeginTransaction();
        using var command=db.CreateCommand();command.Transaction=transaction;
        command.CommandText="INSERT INTO Pitches VALUES('g26a','201',$type,145,$x,$z,$top,2,$hand,$balls,$strikes,NULL,1,$id)";
        var types=new[]{"직구","슬라이더","커브","체인지업","포크","투심","커터"};
        for(var i=0;i<5002;i++)
        {
            command.Parameters.Clear();
            var x=(i%2==0?1:-1)*(10d+i*1000);
            var z=2.5+(i%3-1);
            foreach(var (key,value) in new (string,object?)[]{("$type",types[i%7]),("$x",i==5000?double.PositiveInfinity:x),("$z",z),("$top",i==5001?1:3),
                ("$hand",i%3==0?"L":i%3==1?"R":null),("$balls",i%4),("$strikes",i%3),("$id","stress"+i)})command.Parameters.AddWithValue(key,value??DBNull.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
