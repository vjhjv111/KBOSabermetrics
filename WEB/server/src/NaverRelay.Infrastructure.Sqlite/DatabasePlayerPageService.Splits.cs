using Microsoft.Data.Sqlite;
using NaverRelay.Application.Players;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabasePlayerPageService
{
    private sealed class SplitAccumulator
    {
        public int PA, AB, H, D2, D3, HR, BB, HBP, SO, TB;
        public void Add(int resultType, bool countsAsAtBat, bool isHit, int totalBases, bool isWalk, bool isIbb, bool isStrikeout)
        {
            PA++;
            if (countsAsAtBat) AB++;
            if (isHit) H++;
            if (totalBases == 2) D2++;
            else if (totalBases == 3) D3++;
            else if (totalBases == 4) HR++;
            TB += Math.Max(0, totalBases);
            if (isWalk || isIbb) BB++;
            if (resultType == (int)BattingResultType.HitByPitch) HBP++;
            if (isStrikeout) SO++;
        }
    }

    private static double? Divide(double numerator, double denominator) => denominator > 0 ? numerator / denominator : null;

    private static PlayerOpponentSplitRow ToOpponentRow((int Year, string Perspective, string Opponent) key, SplitAccumulator x)
    {
        var avg = Divide(x.H, x.AB);
        var obp = Divide(x.H + x.BB + x.HBP, x.AB + x.BB + x.HBP);
        var slg = Divide(x.TB, x.AB);
        return new PlayerOpponentSplitRow { Year=key.Year, Perspective=key.Perspective, Opponent=key.Opponent, PA=x.PA, AB=x.AB, Hits=x.H, Doubles=x.D2, Triples=x.D3, HomeRuns=x.HR, Walks=x.BB, HitByPitch=x.HBP, Strikeouts=x.SO, AVG=avg, OBP=obp, SLG=slg, OPS=obp.HasValue && slg.HasValue ? obp.Value+slg.Value : null };
    }

    private static PlayerSituationSplitRow ToSituationRow((int Year, string Perspective, string Category, string Bucket) key, SplitAccumulator x)
    {
        var avg=Divide(x.H,x.AB); var obp=Divide(x.H+x.BB+x.HBP,x.AB+x.BB+x.HBP); var slg=Divide(x.TB,x.AB);
        return new PlayerSituationSplitRow { Year=key.Year, Perspective=key.Perspective, Category=key.Category, Bucket=key.Bucket, PA=x.PA, AB=x.AB, Hits=x.H, Doubles=x.D2, Triples=x.D3, HomeRuns=x.HR, Walks=x.BB, HitByPitch=x.HBP, Strikeouts=x.SO, AVG=avg, OBP=obp, SLG=slg, OPS=obp.HasValue && slg.HasValue ? obp.Value+slg.Value : null };
    }

    private static void AddSituation(Dictionary<(int,string,string,string),SplitAccumulator> map, int year, string perspective, string category, string bucket, Action<SplitAccumulator> add)
    {
        var key=(year,perspective,category,bucket);
        if(!map.TryGetValue(key,out var x)) map[key]=x=new SplitAccumulator();
        add(x);
    }

    private async Task<IReadOnlyList<PlayerOpponentSplitRow>> ReadOpponentSplitsAsync(string pcode, CancellationToken ct)
    {
        var map=new Dictionary<(int,string,string),SplitAccumulator>();
        await using var c=await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="""
        SELECT g.SeasonYear, pa.BatterPcode, pa.PitcherPcode, pa.BattingTeamCode, pa.FieldingTeamCode,
               pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases, pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
        FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId
        WHERE pa.IsOfficial=1 AND g.SeasonYear IS NOT NULL AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
          AND (pa.BatterPcode=$pcode OR pa.PitcherPcode=$pcode);
        """;
        cmd.Parameters.AddWithValue("$pcode",pcode);
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var year=r.GetInt32(0); var asBatter=string.Equals(r.IsDBNull(1)?null:r.GetString(1),pcode,StringComparison.Ordinal);
            var key=(year,asBatter?"타자":"투수", asBatter?(r.IsDBNull(4)?"-":r.GetString(4)):(r.IsDBNull(3)?"-":r.GetString(3)));
            if(!map.TryGetValue(key,out var x)) map[key]=x=new SplitAccumulator();
            x.Add(r.GetInt32(5),r.GetInt32(6)!=0,r.GetInt32(7)!=0,r.GetInt32(8),r.GetInt32(9)!=0,r.GetInt32(10)!=0,r.GetInt32(11)!=0);
        }
        return map.Select(kv=>ToOpponentRow(kv.Key,kv.Value)).OrderByDescending(x=>x.Year).ThenBy(x=>x.Perspective).ThenByDescending(x=>x.PA).ToList();
    }

    private async Task<IReadOnlyList<PlayerSituationSplitRow>> ReadSituationSplitsAsync(string pcode, CancellationToken ct)
    {
        var map=new Dictionary<(int,string,string,string),SplitAccumulator>();
        await using var c=await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="""
        SELECT g.SeasonYear, pa.BatterPcode, pa.PitcherPcode, pa.Inning, pa.RawHomeOrAway,
               pa.BeforeOuts, pa.BeforeFirstRunnerPcode, pa.BeforeSecondRunnerPcode, pa.BeforeThirdRunnerPcode,
               pa.BeforeHomeScore, pa.BeforeAwayScore, pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
               pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
        FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId
        WHERE pa.IsOfficial=1 AND g.SeasonYear IS NOT NULL AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
          AND (pa.BatterPcode=$pcode OR pa.PitcherPcode=$pcode);
        """;
        cmd.Parameters.AddWithValue("$pcode",pcode);
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false))
        {
            int year=r.GetInt32(0); bool asBatter=string.Equals(r.IsDBNull(1)?null:r.GetString(1),pcode,StringComparison.Ordinal); string perspective=asBatter?"타자":"투수";
            int inning=r.IsDBNull(3)?0:r.GetInt32(3); int outs=r.IsDBNull(5)?0:r.GetInt32(5);
            bool on1=!r.IsDBNull(6), on2=!r.IsDBNull(7), on3=!r.IsDBNull(8);
            int hs=r.IsDBNull(9)?0:r.GetInt32(9), aws=r.IsDBNull(10)?0:r.GetInt32(10); int diff=asBatter ? ((r.IsDBNull(4)?"":r.GetString(4)).Contains("말")?hs-aws:aws-hs) : ((r.IsDBNull(4)?"":r.GetString(4)).Contains("말")?aws-hs:hs-aws);
            Action<SplitAccumulator> add=x=>x.Add(r.GetInt32(11),r.GetInt32(12)!=0,r.GetInt32(13)!=0,r.GetInt32(14),r.GetInt32(15)!=0,r.GetInt32(16)!=0,r.GetInt32(17)!=0);
            string runners=on1&&on2&&on3?"만루":on1&&on2?"1·2루":on1&&on3?"1·3루":on2&&on3?"2·3루":on1?"1루":on2?"2루":on3?"3루":"주자 없음";
            AddSituation(map,year,perspective,"주자",runners,add);
            AddSituation(map,year,perspective,"아웃",$"{outs}아웃",add);
            AddSituation(map,year,perspective,"이닝",inning<=3?"1~3회":inning<=6?"4~6회":inning<=9?"7~9회":"연장",add);
            AddSituation(map,year,perspective,"점수차",diff==0?"동점":diff>0?(diff<=2?"1~2점 리드":"3점+ 리드"):(diff>=-2?"1~2점 열세":"3점+ 열세"),add);
            AddSituation(map,year,perspective,"득점권",on2||on3?"득점권":"비득점권",add);
            AddSituation(map,year,perspective,"클러치",inning>=7&&Math.Abs(diff)<=3?"클러치":"일반",add);
        }
        return map.Select(kv=>ToSituationRow(kv.Key,kv.Value)).OrderByDescending(x=>x.Year).ThenBy(x=>x.Perspective).ThenBy(x=>x.Category).ThenByDescending(x=>x.PA).ToList();
    }

    private async Task<IReadOnlyList<PlayerPitchTypeBattingRow>> ReadBattingPitchTypeSplitsAsync(string pcode, CancellationToken ct)
    {
        var map=new Dictionary<(int,string),SplitAccumulator>();
        await using var c=await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="""
        SELECT g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상'), pa.ResultType, pa.CountsAsAtBat,
               pa.IsHit, pa.TotalBases, pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
        FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId
        LEFT JOIN Pitches p ON p.PitchEventId=(SELECT p2.PitchEventId FROM Pitches p2 WHERE p2.PlateAppearanceId=pa.PlateAppearanceId ORDER BY p2.ActualPitchIndex DESC LIMIT 1)
        WHERE pa.IsOfficial=1 AND pa.BatterPcode=$pcode AND g.SeasonYear IS NOT NULL
          AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r';
        """;
        cmd.Parameters.AddWithValue("$pcode",pcode);
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false)) { var key=(r.GetInt32(0),r.GetString(1)); if(!map.TryGetValue(key,out var x))map[key]=x=new SplitAccumulator(); x.Add(r.GetInt32(2),r.GetInt32(3)!=0,r.GetInt32(4)!=0,r.GetInt32(5),r.GetInt32(6)!=0,r.GetInt32(7)!=0,r.GetInt32(8)!=0); }
        return map.Select(kv=>{var x=kv.Value;var avg=Divide(x.H,x.AB);var obp=Divide(x.H+x.BB+x.HBP,x.AB+x.BB+x.HBP);var slg=Divide(x.TB,x.AB);return new PlayerPitchTypeBattingRow{Year=kv.Key.Item1,PitchType=kv.Key.Item2,PA=x.PA,AB=x.AB,Hits=x.H,Doubles=x.D2,Triples=x.D3,HomeRuns=x.HR,Walks=x.BB,HitByPitch=x.HBP,Strikeouts=x.SO,AVG=avg,OBP=obp,SLG=slg,OPS=obp.HasValue&&slg.HasValue?obp.Value+slg.Value:null};}).OrderByDescending(x=>x.Year).ThenByDescending(x=>x.PA).ToList();
    }

    private async Task<IReadOnlyList<PlayerPitchTypePitchingRow>> ReadPitchingPitchTypeSplitsAsync(string pcode, CancellationToken ct)
    {
        var rows=new List<(int Year,string Type,int Pitches,int SpeedCount,double SpeedSum,int Strikes,int Swings,int Whiffs,int Contacts,int Csw)>();
        await using var c=await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="""
        SELECT g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상'), COUNT(*), COUNT(p.SpeedKmh), SUM(COALESCE(p.SpeedKmh,0)),
               SUM(CASE WHEN p.PitchResult IN (2,3,4,5,6,7) THEN 1 ELSE 0 END), SUM(p.IsSwing), SUM(p.IsWhiff), SUM(p.IsContact), SUM(CASE WHEN p.IsCalledStrike=1 OR p.IsWhiff=1 THEN 1 ELSE 0 END)
        FROM Pitches p JOIN Games g ON g.GameId=p.GameId
        WHERE p.PitcherPcode=$pcode AND g.SeasonYear IS NOT NULL AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
        GROUP BY g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상');
        """;
        cmd.Parameters.AddWithValue("$pcode",pcode);
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add((r.GetInt32(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.IsDBNull(4)?0:r.GetDouble(4),r.GetInt32(5),r.GetInt32(6),r.GetInt32(7),r.GetInt32(8),r.GetInt32(9)));
        var totals=rows.GroupBy(x=>x.Year).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Pitches));
        return rows.Select(x=>new PlayerPitchTypePitchingRow{Year=x.Year,PitchType=x.Type,Pitches=x.Pitches,UsageRate=Divide(x.Pitches,totals[x.Year]),AverageSpeed=Divide(x.SpeedSum,x.SpeedCount),StrikeRate=Divide(x.Strikes,x.Pitches),SwingRate=Divide(x.Swings,x.Pitches),WhiffRate=Divide(x.Whiffs,x.Swings),ContactRate=Divide(x.Contacts,x.Swings),CswRate=Divide(x.Csw,x.Pitches)}).OrderByDescending(x=>x.Year).ThenByDescending(x=>x.Pitches).ToList();
    }
}
