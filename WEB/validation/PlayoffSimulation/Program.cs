using System.Text.Json;
using NaverSabermetrics.Web;

int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
void Reject(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception(label);}
var codes=new[]{"HH","HT","KT","LG","LT","NC","OB","SK","SS","WO"};
var full=new int[10,10];int total=0;
for(int h=0;h<10;h++)for(int a=0;a<10;a++){
    full[h,a]=PlayoffSchedule2026.HomeGames(codes[h],codes[a]);total+=full[h,a];
    Check(h==a?full[h,a]==0:full[h,a] is 7 or 9,"Official directed quota");
    if(h!=a)Check(full[h,a]+PlayoffSchedule2026.HomeGames(codes[a],codes[h])==16,"Pair quota is16");
}
Check(total==720,"720game season");
for(int h=0;h<10;h++)Check(Enumerable.Range(0,10).Sum(a=>full[h,a]) is 71 or 73,"71/73 home allocation");
var complete=codes.Select((c,i)=>new ForecastTeam(c,72,0,72,600,600)).ToArray();
var odds=PlayoffModel.SimulateCalibrated(complete,full,default);
Check(odds.All(x=>Math.Abs(x-.5)<1e-10),"Ten tied teams share five places");
var decisive=codes.Select((c,i)=>new ForecastTeam(c,100-i*5,0,44+i*5,600,600)).ToArray();
odds=PlayoffModel.SimulateCalibrated(decisive,full,default);
Check(odds.Take(5).All(x=>x==1)&&odds.Skip(5).All(x=>x==0),"Complete season takes topfive");
var boundary=codes.Select((c,i)=>new ForecastTeam(c,i<4?90:i<7?72:48,0,i<4?54:i<7?72:96,600,600)).ToArray();
odds=PlayoffModel.SimulateCalibrated(boundary,full,default);
Check(odds.Take(4).All(x=>x==1)&&odds.Skip(4).Take(3).All(x=>Math.Abs(x-1.0/3)<1e-10),"Three teams split fifthplace");
var partial=new int[10,10];for(int h=0;h<10;h++)for(int a=0;a<10;a++)partial[h,a]=h==a?0:2;
var teams=codes.Select(c=>new ForecastTeam(c,17,2,17,160,160)).ToArray();
var unchanged=(int[,])partial.Clone();
odds=PlayoffModel.SimulateCalibrated(teams,partial,default);
Check(odds.All(x=>x>=0&&x<=1)&&Math.Abs(odds.Sum()-5)<1e-9,"Bounded odds sumtofive");
Check(odds.SequenceEqual(PlayoffModel.SimulateCalibrated(teams,partial,default)),"Deterministic seed");
Check(partial.Cast<int>().SequenceEqual(unchanged.Cast<int>()),"Input matrix preserved");
var invalid=(int[,])partial.Clone();invalid[0,1]=10;
var badTeams=teams.ToArray();badTeams[0]=badTeams[0] with{L=25};badTeams[1]=badTeams[1] with{L=25};
Reject(()=>PlayoffModel.SimulateCalibrated(badTeams,invalid,default),"Reject quota overflow despite pairtotalbelow16");
Reject(()=>PlayoffModel.SimulateCalibrated(teams,new int[10,10],default),"Reject inconsistent completed counts");
using(var cancelled=new CancellationTokenSource()){
    cancelled.Cancel();try{PlayoffModel.SimulateCalibrated(teams,partial,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){checks++;}
}
if(args.Length>0){
    var input=JsonDocument.Parse(File.ReadAllText(args[0]));
    var games=input.RootElement.EnumerateArray().Where(g=>g.GetProperty("year").GetInt32()==2026).ToArray();
    var real=codes.Select(c=>new ForecastTeam(c,0,0,0,0,0)).ToArray();var directed=new int[10,10];var unordered=new int[10,10];
    foreach(var game in games){
        int h=Array.IndexOf(codes,game.GetProperty("home").GetString()),a=Array.IndexOf(codes,game.GetProperty("away").GetString());
        int hs=game.GetProperty("hs").GetInt32(),aws=game.GetProperty("as").GetInt32();directed[h,a]++;unordered[Math.Min(h,a),Math.Max(h,a)]++;
        foreach(var (i,rf,ra) in new[]{(h,hs,aws),(a,aws,hs)}){var t=real[i];real[i]=t with{W=t.W+(rf>ra?1:0),D=t.D+(rf==ra?1:0),L=t.L+(rf<ra?1:0),RF=t.RF+rf,RA=t.RA+ra};}
    }
    var watch=System.Diagnostics.Stopwatch.StartNew();
    var described=PlayoffModel.ComputeCalibrated(real,directed,default);var calibrated=described.Odds;watch.Stop();
    Check(described.Teams.Select(x=>x.Team).SequenceEqual(codes),"Explanation preserves team order");
    Check(Math.Abs(described.Teams.Sum(x=>x.Rating))<1e-10,"Explanation strengths are mean centered");
    for(int i=0;i<real.Length;i++){
        var detail=described.Teams[i];var record=real[i];
        Check(detail.G==record.G&&detail.RF==record.RF&&detail.RA==record.RA,"Explanation shows actual season inputs");
        Check(Math.Abs(detail.Pyth-record.Pyth!.Value)<1e-12,"Explanation Pyth matches displayed standings");
        Check(Math.Abs(detail.RegressionWeight-record.G/(record.G+40.0))<1e-12,"Explanation shows selected regression weight");
        Check(Math.Abs(detail.RawLogOdds-Math.Log(detail.Pyth/(1-detail.Pyth)))<1e-12,"Explanation shows raw log odds");
    }
    var baseline=PlayoffModel.Simulate(real,unordered,default);
    var allThree=PlayoffModel.SimulateCalibrated(real,directed,default,PlayoffModel.AllThreeCandidate);
    Check(Math.Abs(calibrated.Sum()-5)<1e-9,"Real2026odds sumtofive");
    Console.WriteLine(JsonSerializer.Serialize(new{games=games.Length,milliseconds=watch.ElapsedMilliseconds,rows=codes.Select((c,i)=>new{team=c,baseline=baseline[i],calibrated=calibrated[i],allThree=allThree[i]})}));
}
Console.WriteLine($"PASS {checks} simulation checks");
