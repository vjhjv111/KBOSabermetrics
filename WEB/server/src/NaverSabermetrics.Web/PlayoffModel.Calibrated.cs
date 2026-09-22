namespace NaverSabermetrics.Web;

public sealed record ForecastDetail(string Team, int G, double RF, double RA, double Pyth,
    double RawLogOdds, double RegressionWeight, double Rating);
public sealed record PlayoffForecastResult(double[] Odds, ForecastDetail[] Teams,
    double[] Rank1, double[] Rank2, double[] Rank3, double[] Rank4);

public static partial class PlayoffModel
{
    public const string CalibrationVersion = "pyth-sos-logit-v1-20260913";
    // Frozen using 2020–23 training and 2024 selection BEFORE opening 2025 holdout.
    // Home support is intentional; zero won the validation, not an omitted feature.
    public static ForecastParameters SelectedParameters { get; } = new(0.5, 40, 0);
    public static ForecastParameters AllThreeCandidate { get; } = new(0.5, 40, 0.06);

    public static double[] SimulateCalibrated(IReadOnlyList<ForecastTeam> teams, int[,] playedHome,
        CancellationToken ct, ForecastParameters? parameters = null)
        => ComputeCalibrated(teams, playedHome, ct, parameters).Odds;

    public static PlayoffForecastResult ComputeCalibrated(IReadOnlyList<ForecastTeam> teams, int[,] playedHome,
        CancellationToken ct, ForecastParameters? parameters = null)
    {
        parameters ??= SelectedParameters;
        if(teams.Count!=10||teams.Any(t=>t.G>144||t.D>=144))throw new ArgumentException("Invalid season");
        var strengthTeams=teams.Select(t=>new StrengthTeam(t.Code,t.W,t.D,t.L,t.RF,t.RA)).ToArray();
        var strength=PlayoffStrengthModel.Estimate(strengthTeams,playedHome,parameters);
        var remaining=new List<(int Home,int Away,double P)>();
        for(int h=0;h<teams.Count;h++)for(int a=0;a<teams.Count;a++)
        {
            var quota=PlayoffSchedule2026.HomeGames(teams[h].Code,teams[a].Code);
            if(playedHome[h,a]>quota)throw new ArgumentException("Played games exceed official home/away allocation");
            if(h==a)continue;
            var probability=PlayoffStrengthModel.Probability(strength,h,a,parameters);
            for(int game=playedHome[h,a];game<quota;game++)remaining.Add((h,a,probability));
        }
        var random=new Random(20260912);
        var qualified=new double[10];var wins=new int[10];var values=new double[10];var ordered=new double[10];
        // 1~4위 확률(코시/플옵/준플옵 직행, 와카 홈 어드밴티지)을 위한 순위별 누적치.
        // 동순위 처리: 해당 시뮬레이션 회차에서 값이 같은 팀들끼리 그 순위 구간을 균등하게 나눠 갖는다고 가정.
        var rank1=new double[10];var rank2=new double[10];var rank3=new double[10];var rank4=new double[10];
        for(int trial=0;trial<Trials;trial++)
        {
            if(trial%100==0)ct.ThrowIfCancellationRequested();
            for(int i=0;i<10;i++)wins[i]=teams[i].W;
            foreach(var game in remaining)wins[random.NextDouble()<game.P?game.Home:game.Away]++;
            for(int i=0;i<10;i++)values[i]=(double)wins[i]/(144-teams[i].D);
            Array.Copy(values,ordered,10);Array.Sort(ordered);
            var cutoff=ordered[5];int above=0,tied=0;
            foreach(var value in values){if(value>cutoff+1e-12)above++;else if(Math.Abs(value-cutoff)<1e-12)tied++;}
            for(int i=0;i<10;i++)qualified[i]+=values[i]>cutoff+1e-12?1:Math.Abs(values[i]-cutoff)<1e-12?(5.0-above)/tied:0;
            for(int i=0;i<10;i++)
            {
                var v=values[i];int aboveCount=0,tiedCount=0;
                for(int j=0;j<10;j++){if(values[j]>v+1e-12)aboveCount++;else if(Math.Abs(values[j]-v)<1e-12)tiedCount++;}
                if(aboveCount<1&&1<=aboveCount+tiedCount)rank1[i]+=1.0/tiedCount;
                if(aboveCount<2&&2<=aboveCount+tiedCount)rank2[i]+=1.0/tiedCount;
                if(aboveCount<3&&3<=aboveCount+tiedCount)rank3[i]+=1.0/tiedCount;
                if(aboveCount<4&&4<=aboveCount+tiedCount)rank4[i]+=1.0/tiedCount;
            }
        }
        // The explanation receives the exact strengths used by this simulation.
        var details=teams.Select((team,i)=>{
            var scale=Math.Max(team.RF,team.RA);
            var scored=scale==0?0:Math.Pow(team.RF/scale,1.83);
            var allowed=scale==0?0:Math.Pow(team.RA/scale,1.83);
            var pyth=scale==0?0.5:scored/(scored+allowed);
            var bounded=Math.Clamp(pyth,0.000001,0.999999);
            var weight=team.G==0?0:(double)team.G/(team.G+parameters.PriorGames);
            return new ForecastDetail(team.Code,team.G,team.RF,team.RA,pyth,
                Math.Log(bounded/(1-bounded)),weight,strength[i]);
        }).ToArray();
        return new(qualified.Select(x=>x/Trials).ToArray(),details,
            rank1.Select(x=>x/Trials).ToArray(),rank2.Select(x=>x/Trials).ToArray(),
            rank3.Select(x=>x/Trials).ToArray(),rank4.Select(x=>x/Trials).ToArray());
    }
}
