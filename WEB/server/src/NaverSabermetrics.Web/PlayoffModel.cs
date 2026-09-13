namespace NaverSabermetrics.Web;

public sealed record ForecastTeam(string Code,int W,int D,int L,double RF,double RA)
{
    public int G=>W+D+L;
    public double? Pct=>W+L>0?(double)W/(W+L):null;
    public double? Pyth=>RF+RA>0?Math.Pow(RF,1.83)/(Math.Pow(RF,1.83)+Math.Pow(RA,1.83)):null;
}
public static partial class PlayoffModel
{
    public const int Trials=10000;
    public static double[] Simulate(IReadOnlyList<ForecastTeam> teams,int[,] played,CancellationToken ct)
    {
        if(teams.Count!=10||teams.Any(t=>t.G>144||t.D>=144||t.Pyth is null))throw new ArgumentException("Invalid season");
        var remaining=new List<(int A,int B,double P)>();
        for(int a=0;a<10;a++)for(int b=a+1;b<10;b++){
            if(played[a,b] is <0 or >16)throw new ArgumentException("Invalid matchup");
            var pa=Math.Clamp(teams[a].Pyth!.Value,0.000001,0.999999);var pb=Math.Clamp(teams[b].Pyth!.Value,0.000001,0.999999);var p=pa*(1-pb)/(pa*(1-pb)+pb*(1-pa));
            for(int g=played[a,b];g<16;g++)remaining.Add((a,b,p));
        }
        for(int a=0;a<10;a++){int count=0;for(int b=0;b<10;b++)count+=played[Math.Min(a,b),Math.Max(a,b)];if(count!=teams[a].G)throw new ArgumentException("Incomplete matchups");}
        var random=new Random(20260912);var qualified=new double[10];var wins=new int[10];
        for(int trial=0;trial<Trials;trial++){
            if(trial%100==0)ct.ThrowIfCancellationRequested();for(int i=0;i<10;i++)wins[i]=teams[i].W;
            foreach(var match in remaining)wins[random.NextDouble()<match.P?match.A:match.B]++;
            var values=Enumerable.Range(0,10).Select(i=>(Index:i,Pct:(double)wins[i]/(144-teams[i].D))).OrderByDescending(x=>x.Pct).ToArray();
            var cutoff=values[4].Pct;var above=values.Count(x=>x.Pct>cutoff+1e-12);var tied=values.Count(x=>Math.Abs(x.Pct-cutoff)<1e-12);
            foreach(var x in values)qualified[x.Index]+=x.Pct>cutoff+1e-12?1:Math.Abs(x.Pct-cutoff)<1e-12?(5.0-above)/tied:0;
        }
        return qualified.Select(x=>x/Trials).ToArray();
    }
}
