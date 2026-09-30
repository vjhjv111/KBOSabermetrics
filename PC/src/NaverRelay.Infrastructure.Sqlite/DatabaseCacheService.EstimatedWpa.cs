using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record WpaYearCoverage(int Year,int Total,int Collected,int Estimated,int Missing);
public sealed record WpaBackfillReport(int Games, int Filled, int Skipped, List<string> Errors)
{
    public IReadOnlyList<WpaYearCoverage> Coverage { get; init; }=[];
}

public sealed partial class DatabaseCacheService
{
    private const string EstimatedWpaSchema = """
        CREATE TABLE IF NOT EXISTS EstimatedWpaModels (
            SeasonYear INTEGER PRIMARY KEY, Version TEXT NOT NULL, Json TEXT NOT NULL, CreatedUtc TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS EstimatedWpaValues (
            PlateAppearanceId TEXT PRIMARY KEY REFERENCES PlateAppearances(PlateAppearanceId) ON DELETE CASCADE,
            GameId TEXT NOT NULL REFERENCES Games(GameId) ON DELETE CASCADE,
            ModelYear INTEGER NOT NULL, ModelVersion TEXT NOT NULL,
            HomeBefore REAL NOT NULL, HomeAfter REAL NOT NULL, Wpa REAL NOT NULL, CreatedUtc TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS IX_EstimatedWpaValues_Game ON EstimatedWpaValues(GameId);
        CREATE TABLE IF NOT EXISTS EstimatedWpaBackfillGames (
            GameId TEXT PRIMARY KEY REFERENCES Games(GameId) ON DELETE CASCADE,
            Version TEXT NOT NULL, SourceUpdatedUtc TEXT NOT NULL, Remaining INTEGER NOT NULL,
            Filled INTEGER NOT NULL, Reason TEXT NOT NULL, CompletedUtc TEXT NOT NULL);
        """;

    // Completed innings 1-8 avoid censoring from walk-offs. One sample per PA start.
    // The empirical remaining-run PMF is shrunk toward the pooled KBO PMF (100 PA prior).
    // This is a retrospective run-environment model, not FanGraphs' proprietary WE table.
    public async Task<int> BuildEstimatedWpaModelsAsync(CancellationToken ct = default)
    {
        await EnsureEstimatedWpaSchemaAsync(ct);
        await using var db = await OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            WITH Complete AS (
              SELECT r.GameId,r.Inning,r.BattingSide,MAX(r.AfterHomeScore) HomeEnd,MAX(r.AfterAwayScore) AwayEnd
              FROM RelayGroups r JOIN Games g ON g.GameId=r.GameId
              WHERE r.Inning BETWEEN 1 AND 8 AND g.RoundCode='kbo_r' AND g.StatusCode IN ('RESULT','ENDED')
              GROUP BY r.GameId,r.Inning,r.BattingSide HAVING MAX(r.AfterOuts)=3
            )
            SELECT g.SeasonYear,p.GameId,p.BeforeOuts,
              (CASE WHEN COALESCE(p.BeforeFirstRunnerPcode,p.BeforeFirstRunnerName,'')<>'' THEN 1 ELSE 0 END)+
              (CASE WHEN COALESCE(p.BeforeSecondRunnerPcode,p.BeforeSecondRunnerName,'')<>'' THEN 2 ELSE 0 END)+
              (CASE WHEN COALESCE(p.BeforeThirdRunnerPcode,p.BeforeThirdRunnerName,'')<>'' THEN 4 ELSE 0 END),
              CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN c.HomeEnd-p.BeforeHomeScore ELSE c.AwayEnd-p.BeforeAwayScore END
            FROM PlateAppearances p JOIN Games g ON g.GameId=p.GameId
              JOIN Complete c ON c.GameId=p.GameId AND c.Inning=p.Inning AND c.BattingSide=p.BattingSide
            WHERE p.IsOfficial=1 AND p.BeforeOuts BETWEEN 0 AND 2 AND g.SeasonYear BETWEEN 2012 AND 2100
              AND p.BeforeHomeScore IS NOT NULL AND p.BeforeAwayScore IS NOT NULL
            """;
        var counts = new Dictionary<int, double[][]>(); var games = new Dictionary<int, HashSet<string>>();
        double[][] NewCounts() => Enumerable.Range(0,24).Select(_=>new double[31]).ToArray();
        var pool = NewCounts();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(4)) continue;
                var runs=reader.GetInt32(4); if(runs is <0 or >30)continue;
                var year=reader.GetInt32(0); var state=reader.GetInt32(2)*8+reader.GetInt32(3);
                if(!counts.TryGetValue(year,out var cells)){counts[year]=cells=NewCounts();games[year]=new();}
                cells[state][runs]++;pool[state][runs]++;games[year].Add(reader.GetString(1));
            }
        if(pool.Any(c=>c.Sum()<20))throw new InvalidOperationException("24개 주자·아웃 상황의 학습 표본이 부족합니다.");
        using var tx = db.BeginTransaction(); cmd.Transaction=tx;
        var built=0;
        foreach(var (year,cells) in counts)
        {
            if(games[year].Count<100)continue;
            // A stored model is immutable for reproducible backfills. Version upgrades need an explicit migration.
            cmd.CommandText="SELECT COUNT(*) FROM EstimatedWpaModels WHERE SeasonYear=$year";
            cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$year",year);
            if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))!=0)continue;
            var model=new EstimatedWpaModel{Year=year,TrainingGames=games[year].Count,Samples=cells.Select(c=>(int)c.Sum()).ToArray(),Runs=NewCounts()};
            for(var s=0;s<24;s++)for(var r=0;r<31;r++)model.Runs[s][r]=(cells[s][r]+100*pool[s][r]/pool[s].Sum())/(model.Samples[s]+100);
            cmd.CommandText="INSERT INTO EstimatedWpaModels VALUES($year,$version,$json,$utc)";
            cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(model));cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);built++;
        }
        tx.Commit();return built;
    }

    private async Task<EstimatedWpaModel?> LoadEstimatedWpaModelAsync(int? year, CancellationToken ct)
    {
        if(year is null)return null;
        await using var db=await OpenAsync(ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Json FROM EstimatedWpaModels WHERE SeasonYear=$year AND Version=$version";
        cmd.Parameters.AddWithValue("$year",year);cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
        return await cmd.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<EstimatedWpaModel>(json) : null;
    }

    private async Task<List<EstimatedWpaValue>> ApplyEstimatedWpaAsync(NormalizedGame game,CancellationToken ct)
    {
        if(!game.PlateAppearances.Any(p=>p.IsOfficialPlateAppearance && p.WpaByPlate is null))return [];
        var model=await LoadEstimatedWpaModelAsync(game.SeasonYear,ct);
        if(model is null)return [];
        var values=EstimatedWpa.Calculate(game,model);
        foreach(var value in values)
        {
            var pa=game.PlateAppearances.First(p=>p.PlateAppearanceId==value.PlateAppearanceId);
            pa.WpaByPlate=value.Wpa;
            // Leave native win-rate fields untouched: our draw-adjusted expectancy is a different measure.
        }
        return values;
    }

    private static async Task SaveEstimatedWpaValuesAsync(SqliteConnection db,SqliteTransaction tx,string gameId,
        IEnumerable<EstimatedWpaValue> values,CancellationToken ct)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;
        foreach(var v in values)
        {
            cmd.CommandText="INSERT INTO EstimatedWpaValues VALUES($id,$game,$year,$version,$before,$after,$wpa,$utc)";
            cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$id",v.PlateAppearanceId);cmd.Parameters.AddWithValue("$game",gameId);
            cmd.Parameters.AddWithValue("$year",v.Year);cmd.Parameters.AddWithValue("$version",v.ModelVersion);
            cmd.Parameters.AddWithValue("$before",v.HomeBefore);cmd.Parameters.AddWithValue("$after",v.HomeAfter);
            cmd.Parameters.AddWithValue("$wpa",v.Wpa);cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<WpaBackfillReport> BackfillEstimatedWpaAsync(int? year=null,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        await EnsureEstimatedWpaSchemaAsync(ct);
        var sources=new List<(string Id,int Year,int Pending,string Updated)>();
        await using(var db=await OpenAsync(ct))
        {
            await using var cmd=db.CreateCommand();cmd.CommandText="""
                SELECT g.GameId,g.SeasonYear,
                  (SELECT COUNT(*) FROM PlateAppearances p WHERE p.GameId=g.GameId AND p.IsOfficial=1 AND p.WpaByPlate IS NULL),g.UpdatedUtc FROM Games g
                JOIN EstimatedWpaModels m ON m.SeasonYear=g.SeasonYear
                WHERE g.RoundCode='kbo_r' AND g.SeasonYear BETWEEN 2016 AND 2023 AND m.Version=$version AND ($year IS NULL OR g.SeasonYear=$year)
                  AND EXISTS(SELECT 1 FROM PlateAppearances p WHERE p.GameId=g.GameId AND p.IsOfficial=1 AND p.WpaByPlate IS NULL)
                  AND NOT EXISTS(SELECT 1 FROM EstimatedWpaBackfillGames b WHERE b.GameId=g.GameId
                    AND b.Version=$version AND b.SourceUpdatedUtc=g.UpdatedUtc AND b.Remaining=
                    (SELECT COUNT(*) FROM PlateAppearances p WHERE p.GameId=g.GameId AND p.IsOfficial=1 AND p.WpaByPlate IS NULL))
                GROUP BY g.GameId,g.SeasonYear ORDER BY g.GameId
                """;cmd.Parameters.AddWithValue("$year",(object?)year??DBNull.Value);cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))sources.Add((reader.GetString(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3)));
        }
        int filled=0,skipped=0,completed=0;var errors=new List<string>();
        var models=new Dictionary<int,EstimatedWpaModel>();
        foreach(var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var game=await LoadGameForEstimatedWpaAsync(source.Id,ct)??throw new InvalidDataException("경기 데이터 없음");
                if(!models.TryGetValue(source.Year,out var model))models[source.Year]=model=(await LoadEstimatedWpaModelAsync(source.Year,ct))!;
                var values=EstimatedWpa.Calculate(game,model);
                await using var db=await OpenAsync(ct);using var tx=db.BeginTransaction();await using var cmd=db.CreateCommand();cmd.Transaction=tx;
                db.CreateFunction<string?,string>("wpa_clean",text=>System.Text.RegularExpressions.Regex.Replace(text??"",@"\s+",""));
                var saved=new List<EstimatedWpaValue>();
                foreach(var value in values)
                {
                    var pa=game.PlateAppearances.First(p=>p.PlateAppearanceId==value.PlateAppearanceId);
                    cmd.CommandText="""
                        UPDATE PlateAppearances SET WpaByPlate=$wpa
                        WHERE PlateAppearanceId=$id AND GameId=$game AND WpaByPlate IS NULL AND IsOfficial=1
                          AND RelayGroupId=$relay AND wpa_clean(ResultText)=wpa_clean($result) AND Inning=$inning
                          AND BatterPcode=$batter AND BattingTeamCode=$team
                          AND BeforeHomeScore=$bh AND BeforeAwayScore=$ba AND BeforeOuts=$bo
                          AND AfterHomeScore=$ah AND AfterAwayScore=$aa AND AfterOuts=$ao
                          AND EXISTS(SELECT 1 FROM Games WHERE GameId=$game AND UpdatedUtc=$updated)
                        """;
                    cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$wpa",value.Wpa);cmd.Parameters.AddWithValue("$id",value.PlateAppearanceId);
                    cmd.Parameters.AddWithValue("$game",source.Id);cmd.Parameters.AddWithValue("$relay",value.RelayGroupId);
                    cmd.Parameters.AddWithValue("$updated",source.Updated);
                    cmd.Parameters.AddWithValue("$result",(object?)pa.ResultText??DBNull.Value);cmd.Parameters.AddWithValue("$inning",pa.Inning!.Value);
                    cmd.Parameters.AddWithValue("$batter",(object?)pa.BatterPcode??DBNull.Value);cmd.Parameters.AddWithValue("$team",(object?)pa.BattingTeamCode??DBNull.Value);
                    cmd.Parameters.AddWithValue("$bh",pa.StateBefore!.HomeScore!.Value);cmd.Parameters.AddWithValue("$ba",pa.StateBefore.AwayScore!.Value);cmd.Parameters.AddWithValue("$bo",pa.StateBefore.Outs!.Value);
                    cmd.Parameters.AddWithValue("$ah",pa.StateAfter!.HomeScore!.Value);cmd.Parameters.AddWithValue("$aa",pa.StateAfter.AwayScore!.Value);cmd.Parameters.AddWithValue("$ao",pa.StateAfter.Outs!.Value);
                    if(await cmd.ExecuteNonQueryAsync(ct)==1)saved.Add(value);
                }
                if(saved.Count>0)
                {
                    await SaveEstimatedWpaValuesAsync(db,tx,source.Id,saved,ct);
                    cmd.CommandText="""
                        UPDATE BatterGameStats SET WPA=COALESCE((SELECT SUM(p.WpaByPlate) FROM PlateAppearances p
                          WHERE p.GameId=BatterGameStats.GameId AND p.BatterPcode=BatterGameStats.Pcode
                            AND p.BattingTeamCode=BatterGameStats.TeamCode AND p.IsOfficial=1),0) WHERE GameId=$game;
                        UPDATE Metadata SET MetaValue=CAST(CAST(MetaValue AS INTEGER)+1 AS TEXT) WHERE MetaKey='DataVersion';
                        DELETE FROM ComputedCache; DELETE FROM LeagueConstants;
                        """;cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$game",source.Id);await cmd.ExecuteNonQueryAsync(ct);
                }
                cmd.Parameters.Clear();cmd.CommandText="""
                    INSERT INTO EstimatedWpaBackfillGames VALUES($game,$version,$updated,$remaining,$filled,$reason,$utc)
                    ON CONFLICT(GameId) DO UPDATE SET Version=excluded.Version,SourceUpdatedUtc=excluded.SourceUpdatedUtc,
                      Remaining=excluded.Remaining,Filled=excluded.Filled,Reason=excluded.Reason,CompletedUtc=excluded.CompletedUtc
                    """;
                cmd.Parameters.AddWithValue("$game",source.Id);cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
                cmd.Parameters.AddWithValue("$updated",source.Updated);cmd.Parameters.AddWithValue("$remaining",source.Pending-saved.Count);
                var last=game.PlateAppearances.LastOrDefault(p=>p.IsOfficialPlateAppearance);
                var reason=saved.Count==source.Pending?"complete":last?.Inning<9?"shortened-game":
                    last?.StateAfter?.HomeScore!=game.HomeTeam.FinalScore||last?.StateAfter?.AwayScore!=game.AwayTeam.FinalScore?"terminal-score-mismatch":
                    game.HomeTeam.FinalScore==game.AwayTeam.FinalScore&&values.Count==0?"unsupported-draw-ending":"missing-or-inconsistent-pa-state";
                cmd.Parameters.AddWithValue("$filled",saved.Count);cmd.Parameters.AddWithValue("$reason",reason);cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();filled+=saved.Count;skipped+=source.Pending-saved.Count;
            }
            catch(Exception ex) when(ex is not OperationCanceledException){errors.Add(source.Id+": "+ex.Message);}
            completed++;
            if(completed%25==0||completed==sources.Count)progress?.Report($"{completed}/{sources.Count} games; filled={filled}, errors={errors.Count}");
            await Task.Delay(1,ct);
        }
        return new(completed,filled,skipped,errors){Coverage=await GetEstimatedWpaCoverageAsync(ct)};
    }

    private async Task EnsureEstimatedWpaSchemaAsync(CancellationToken ct)
    {
        await using var db=await OpenAsync(ct);
        await ExecuteAsync(db,EstimatedWpaSchema,ct);
    }

    public async Task<IReadOnlyList<WpaYearCoverage>> GetEstimatedWpaCoverageAsync(CancellationToken ct=default)
    {
        await using var db=await OpenAsync(ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT g.SeasonYear,COUNT(*),
              SUM(CASE WHEN p.WpaByPlate IS NOT NULL AND e.PlateAppearanceId IS NULL THEN 1 ELSE 0 END),
              SUM(CASE WHEN e.PlateAppearanceId IS NOT NULL THEN 1 ELSE 0 END),
              SUM(CASE WHEN p.WpaByPlate IS NULL THEN 1 ELSE 0 END)
            FROM PlateAppearances p JOIN Games g ON g.GameId=p.GameId
              LEFT JOIN EstimatedWpaValues e ON e.PlateAppearanceId=p.PlateAppearanceId
            WHERE g.RoundCode='kbo_r' AND g.SeasonYear BETWEEN 2016 AND 2023 AND p.IsOfficial=1
            GROUP BY g.SeasonYear ORDER BY g.SeasonYear
            """;
        var result=new List<WpaYearCoverage>();await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4)));
        return result;
    }

    public async Task<NormalizedGame?> LoadGameForEstimatedWpaAsync(string gameId,CancellationToken ct=default)
    {
        var game=await LoadGameByIdAsync(gameId,GameDataProjection.Metadata|GameDataProjection.PlateAppearances|GameDataProjection.Events|GameDataProjection.RelayGroups,ct);
        if(game is null)return null;
        var groups=game.RelayGroups.OrderBy(g=>g.ChronologicalIndex).ToArray();
        var indexes=groups.Select((g,i)=>(g.RelayGroupId,i)).ToDictionary(x=>x.RelayGroupId,x=>x.i);
        foreach(var pa in game.PlateAppearances)
        {
            if(pa.StateAfter is not {} after || !indexes.TryGetValue(pa.RelayGroupId,out var index))continue;
            // The parser sets the next relay's StateBefore from the previous relay's final snapshot.
            // This is NOT the next PA's starting state: intervening steals/substitutions must not be attributed to this PA.
            var next=groups.Skip(index+1).Select(g=>g.StateBefore).FirstOrDefault(s=>s?.Outs is not null);
            if(after.Outs==3 || (pa.BattingSide==TeamSide.Home && pa.Inning>=9 && after.HomeScore>after.AwayScore))continue;
            if(next is null || next.Outs!=after.Outs || next.HomeScore!=after.HomeScore || next.AwayScore!=after.AwayScore)
            {
                pa.StartEventId=null; // Fail closed; no reliable after-base state.
                continue;
            }
            after.FirstBaseRunnerPcode=next.FirstBaseRunnerPcode;after.FirstBaseRunnerName=next.FirstBaseRunnerName;
            after.SecondBaseRunnerPcode=next.SecondBaseRunnerPcode;after.SecondBaseRunnerName=next.SecondBaseRunnerName;
            after.ThirdBaseRunnerPcode=next.ThirdBaseRunnerPcode;after.ThirdBaseRunnerName=next.ThirdBaseRunnerName;
        }
        return game;
    }
}
