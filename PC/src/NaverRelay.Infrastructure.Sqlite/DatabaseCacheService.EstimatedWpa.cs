using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record WpaYearCoverage(int Year,int Total,int Collected,int Estimated,int Missing);
public sealed record WpaBackfillReport(int Games, int Filled, int Skipped, List<string> Errors)
{
    public int Replaced { get; init; }
    public int Cleared { get; init; }
    public IReadOnlyList<WpaYearCoverage> Coverage { get; init; }=[];
}
public sealed record EstimatedWpaChange(string Id,double? Original,EstimatedWpaValue? Value,string Reason);

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
        CREATE TABLE IF NOT EXISTS WpaModelHistory (
            SeasonYear INTEGER NOT NULL, Version TEXT NOT NULL, Json TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            PRIMARY KEY(SeasonYear,Version));
        CREATE TABLE IF NOT EXISTS WpaRevisionHistory (
            RevisionId INTEGER PRIMARY KEY AUTOINCREMENT, PlateAppearanceId TEXT NOT NULL, GameId TEXT NOT NULL,
            OldWpa REAL NULL, NewWpa REAL NULL, OldVersion TEXT NOT NULL, NewVersion TEXT NOT NULL,
            OldHomeBefore REAL NULL, OldHomeAfter REAL NULL, NewHomeBefore REAL NULL, NewHomeAfter REAL NULL,
            Reason TEXT NOT NULL, ChangedUtc TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS IX_WpaRevisionHistory_Game ON WpaRevisionHistory(GameId);
        """;

    // Register the pinned, complete, embedded table. There is no statistical training or network I/O.
    public async Task<int> BuildEstimatedWpaModelsAsync(CancellationToken ct = default)
    {
        FanGraphsWeTable.Validate(); // Fail before altering any DB values if the bundle is incomplete/corrupt.
        await EnsureEstimatedWpaSchemaAsync(ct);
        await using var db=await OpenAsync(ct);using var tx=db.BeginTransaction();
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT OR IGNORE INTO WpaModelHistory SELECT SeasonYear,Version,Json,CreatedUtc FROM EstimatedWpaModels";
        await cmd.ExecuteNonQueryAsync(ct);
        var count=0;
        for(var year=2016;year<=2023;year++)
        {
            cmd.CommandText="""
                INSERT INTO EstimatedWpaModels VALUES($year,$version,$json,$utc)
                ON CONFLICT(SeasonYear) DO UPDATE SET Version=excluded.Version,Json=excluded.Json,CreatedUtc=excluded.CreatedUtc
                WHERE EstimatedWpaModels.Version<>excluded.Version OR EstimatedWpaModels.Json<>excluded.Json
                """;
            cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$year",year);cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
            cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(new EstimatedWpaModel{Year=year}));
            cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));count+=await cmd.ExecuteNonQueryAsync(ct);
        }
        tx.Commit();return count;
    }

    private async Task<EstimatedWpaModel?> LoadEstimatedWpaModelAsync(int? year,CancellationToken ct)
    {
        if(year is <2016 or >2023 or null)return null;
        await using var db=await OpenAsync(ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Json FROM EstimatedWpaModels WHERE SeasonYear=$year AND Version=$version";
        cmd.Parameters.AddWithValue("$year",year);cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
        var model=await cmd.ExecuteScalarAsync(ct) is string json?JsonSerializer.Deserialize<EstimatedWpaModel>(json):null;
        if(model is not null && (model.TableSha256!=FanGraphsWeTable.Sha256 || model.TableVersion!=EstimatedWpaModel.Version))
            throw new InvalidDataException("Registered FanGraphs table differs from the server bundle.");
        return model;
    }

    private async Task<List<EstimatedWpaChange>> ApplyEstimatedWpaAsync(NormalizedGame game,CancellationToken ct)
    {
        if(game.StatusCode is not ("RESULT" or "ENDED"))return [];
        var model=await LoadEstimatedWpaModelAsync(game.SeasonYear,ct);if(model is null)return [];
        var values=EstimatedWpa.Calculate(game,model,replaceExisting:true).ToDictionary(v=>v.PlateAppearanceId);
        var changes=new List<EstimatedWpaChange>();
        foreach(var pa in game.PlateAppearances.Where(p=>p.IsOfficialPlateAppearance))
        {
            values.TryGetValue(pa.PlateAppearanceId,out var value);
            changes.Add(new(pa.PlateAppearanceId,pa.WpaByPlate,value,value is null?"reimport-unsupported-state":"reimport-fangraphs"));
            pa.WpaByPlate=value?.Wpa;
        }
        return changes;
    }

    private static async Task ArchiveExistingWpaBeforeReimportAsync(SqliteConnection db,SqliteTransaction tx,string gameId,CancellationToken ct)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO WpaRevisionHistory
              (PlateAppearanceId,GameId,OldWpa,NewWpa,OldVersion,NewVersion,OldHomeBefore,OldHomeAfter,
               NewHomeBefore,NewHomeAfter,Reason,ChangedUtc)
            SELECT p.PlateAppearanceId,p.GameId,p.WpaByPlate,NULL,COALESCE(e.ModelVersion,'collected-or-external'),
              $version,e.HomeBefore,e.HomeAfter,NULL,NULL,'before-reimport-backup',$utc
            FROM PlateAppearances p LEFT JOIN EstimatedWpaValues e ON e.PlateAppearanceId=p.PlateAppearanceId AND e.Wpa=p.WpaByPlate
            WHERE p.GameId=$game AND p.IsOfficial=1 AND p.WpaByPlate IS NOT NULL
            """;
        cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$game",gameId);await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SaveEstimatedWpaImportCheckpointAsync(SqliteConnection db,SqliteTransaction tx,string gameId,CancellationToken ct)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT OR REPLACE INTO EstimatedWpaBackfillGames
            SELECT GameId,$version,UpdatedUtc,
              (SELECT COUNT(*) FROM PlateAppearances p WHERE p.GameId=g.GameId AND p.IsOfficial=1 AND p.WpaByPlate IS NULL),
              (SELECT COUNT(*) FROM EstimatedWpaValues e WHERE e.GameId=g.GameId),
              'reimport', $utc FROM Games g WHERE GameId=$game
            """;
        cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$game",gameId);await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SaveEstimatedWpaValuesAsync(SqliteConnection db,SqliteTransaction tx,string gameId,
        IEnumerable<EstimatedWpaChange> changes,CancellationToken ct)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;
        foreach(var change in changes)
        {
            var v=change.Value;
            // History has no cascading FK: original values survive a later game reimport.
            cmd.CommandText="""
                INSERT INTO WpaRevisionHistory
                  (PlateAppearanceId,GameId,OldWpa,NewWpa,OldVersion,NewVersion,OldHomeBefore,OldHomeAfter,
                   NewHomeBefore,NewHomeAfter,Reason,ChangedUtc)
                SELECT $id,$game,$old,$new,COALESCE(e.ModelVersion,CASE WHEN $old IS NULL THEN 'missing' ELSE 'collected-or-external' END),
                  $version,e.HomeBefore,e.HomeAfter,$before,$after,$reason,$utc
                FROM (SELECT 1) x LEFT JOIN EstimatedWpaValues e ON e.PlateAppearanceId=$id AND e.Wpa IS $old;
                DELETE FROM EstimatedWpaValues WHERE PlateAppearanceId=$id;
                """;
            cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$id",change.Id);cmd.Parameters.AddWithValue("$game",gameId);
            cmd.Parameters.AddWithValue("$old",(object?)change.Original??DBNull.Value);cmd.Parameters.AddWithValue("$new",(object?)v?.Wpa??DBNull.Value);
            cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);cmd.Parameters.AddWithValue("$before",(object?)v?.HomeBefore??DBNull.Value);
            cmd.Parameters.AddWithValue("$after",(object?)v?.HomeAfter??DBNull.Value);cmd.Parameters.AddWithValue("$reason",change.Reason);
            cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);
            if(v is null)continue;
            cmd.CommandText="INSERT INTO EstimatedWpaValues VALUES($id,$game,$year,$version,$before,$after,$new,$utc)";
            cmd.Parameters.AddWithValue("$year",v.Year);await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<WpaBackfillReport> BackfillEstimatedWpaAsync(int? year=null,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        FanGraphsWeTable.Validate();
        await EnsureEstimatedWpaSchemaAsync(ct);
        var sources=new List<(string Id,int Year,string Updated)>();
        await using(var db=await OpenAsync(ct))
        {
            await using var cmd=db.CreateCommand();cmd.CommandText="""
                SELECT g.GameId,g.SeasonYear,g.UpdatedUtc FROM Games g JOIN EstimatedWpaModels m ON m.SeasonYear=g.SeasonYear
                WHERE g.SeasonYear BETWEEN 2016 AND 2023 AND g.StatusCode IN ('RESULT','ENDED') AND m.Version=$version
                  AND ($year IS NULL OR g.SeasonYear=$year)
                  AND EXISTS(SELECT 1 FROM PlateAppearances p WHERE p.GameId=g.GameId AND p.IsOfficial=1)
                  AND (NOT EXISTS(SELECT 1 FROM EstimatedWpaBackfillGames b WHERE b.GameId=g.GameId AND b.Version=$version
                      AND b.SourceUpdatedUtc=g.UpdatedUtc AND b.Remaining=(SELECT COUNT(*) FROM PlateAppearances p
                          WHERE p.GameId=g.GameId AND p.IsOfficial=1 AND p.WpaByPlate IS NULL))
                    OR EXISTS(SELECT 1 FROM PlateAppearances p LEFT JOIN EstimatedWpaValues e ON e.PlateAppearanceId=p.PlateAppearanceId
                      WHERE p.GameId=g.GameId AND p.IsOfficial=1 AND p.WpaByPlate IS NOT NULL
                        AND (e.ModelVersion IS NOT $version OR e.Wpa IS NOT p.WpaByPlate)))
                ORDER BY g.GameId
                """;
            cmd.Parameters.AddWithValue("$year",(object?)year??DBNull.Value);cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))sources.Add((reader.GetString(0),reader.GetInt32(1),reader.GetString(2)));
        }
        int filled=0,replaced=0,cleared=0,skipped=0,completed=0;var errors=new List<string>();
        var models=new Dictionary<int,EstimatedWpaModel>();
        foreach(var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var game=await LoadGameForEstimatedWpaAsync(source.Id,ct)??throw new InvalidDataException("경기 데이터 없음");
                if(!models.TryGetValue(source.Year,out var model))models[source.Year]=model=(await LoadEstimatedWpaModelAsync(source.Year,ct))!;
                var values=EstimatedWpa.Calculate(game,model,replaceExisting:true).ToDictionary(v=>v.PlateAppearanceId);
                await using var db=await OpenAsync(ct);using var tx=db.BeginTransaction();await using var cmd=db.CreateCommand();cmd.Transaction=tx;
                cmd.CommandText="SELECT UpdatedUtc FROM Games WHERE GameId=$game";cmd.Parameters.AddWithValue("$game",source.Id);
                if((string?)await cmd.ExecuteScalarAsync(ct)!=source.Updated)throw new InvalidOperationException("Game changed during calculation; retry required.");
                var current=new Dictionary<string,(double Wpa,string Version)>();
                cmd.CommandText="SELECT PlateAppearanceId,Wpa,ModelVersion FROM EstimatedWpaValues WHERE GameId=$game";
                await using(var rd=await cmd.ExecuteReaderAsync(ct))while(await rd.ReadAsync(ct))current[rd.GetString(0)]=(rd.GetDouble(1),rd.GetString(2));
                var saved=new List<EstimatedWpaChange>();
                foreach(var pa in game.PlateAppearances.Where(p=>p.IsOfficialPlateAppearance))
                {
                    values.TryGetValue(pa.PlateAppearanceId,out var value);
                    if(pa.WpaByPlate is null && value is null)continue;
                    if(value is not null && pa.WpaByPlate==value.Wpa && current.TryGetValue(pa.PlateAppearanceId,out var old) &&
                        old.Version==EstimatedWpaModel.Version && old.Wpa==value.Wpa)continue;
                    cmd.CommandText="""
                        UPDATE PlateAppearances SET WpaByPlate=$new WHERE PlateAppearanceId=$id AND GameId=$game
                          AND IsOfficial=1 AND WpaByPlate IS $old AND RelayGroupId=$relay AND Inning IS $inning
                          AND BeforeHomeScore IS $bh AND BeforeAwayScore IS $ba AND BeforeOuts IS $bo
                          AND AfterHomeScore IS $ah AND AfterAwayScore IS $aa AND AfterOuts IS $ao
                        """;
                    cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$id",pa.PlateAppearanceId);cmd.Parameters.AddWithValue("$game",source.Id);
                    cmd.Parameters.AddWithValue("$new",(object?)value?.Wpa??DBNull.Value);cmd.Parameters.AddWithValue("$old",(object?)pa.WpaByPlate??DBNull.Value);
                    cmd.Parameters.AddWithValue("$relay",pa.RelayGroupId);cmd.Parameters.AddWithValue("$inning",(object?)pa.Inning??DBNull.Value);
                    cmd.Parameters.AddWithValue("$bh",(object?)pa.StateBefore?.HomeScore??DBNull.Value);cmd.Parameters.AddWithValue("$ba",(object?)pa.StateBefore?.AwayScore??DBNull.Value);
                    cmd.Parameters.AddWithValue("$bo",(object?)pa.StateBefore?.Outs??DBNull.Value);cmd.Parameters.AddWithValue("$ah",(object?)pa.StateAfter?.HomeScore??DBNull.Value);
                    cmd.Parameters.AddWithValue("$aa",(object?)pa.StateAfter?.AwayScore??DBNull.Value);cmd.Parameters.AddWithValue("$ao",(object?)pa.StateAfter?.Outs??DBNull.Value);
                    if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("PA changed during calculation; retry required: "+pa.PlateAppearanceId);
                    saved.Add(new(pa.PlateAppearanceId,pa.WpaByPlate,value,value is null?"unsupported-state-or-ending":"fangraphs-table"));
                }
                if(saved.Count>0)
                {
                    await SaveEstimatedWpaValuesAsync(db,tx,source.Id,saved,ct);
                    foreach(var pa in game.PlateAppearances.Where(p=>p.IsOfficialPlateAppearance))
                        pa.WpaByPlate=values.GetValueOrDefault(pa.PlateAppearanceId)?.Wpa;
                    var projections=WarehouseProjectionBuilder.Build(game,usePlateAppearanceWpa:true);
                    foreach(var pitcher in projections.PitcherGames)
                    {
                        cmd.CommandText="UPDATE PitcherGameStats SET EntryAbsoluteWpaSum=$sum,EntryWpaCount=$count WHERE GameId=$game AND Pcode=$code AND TeamCode=$team";
                        cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$sum",pitcher.EntryAbsoluteWpaSum);cmd.Parameters.AddWithValue("$count",pitcher.EntryWpaCount);
                        cmd.Parameters.AddWithValue("$game",source.Id);cmd.Parameters.AddWithValue("$code",pitcher.Pcode);cmd.Parameters.AddWithValue("$team",pitcher.TeamCode);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                    cmd.CommandText="""
                        UPDATE BatterGameStats SET WPA=COALESCE((SELECT SUM(p.WpaByPlate) FROM PlateAppearances p
                          WHERE p.GameId=BatterGameStats.GameId AND p.BatterPcode=BatterGameStats.Pcode
                            AND p.BattingTeamCode=BatterGameStats.TeamCode AND p.IsOfficial=1),0) WHERE GameId=$game;
                        UPDATE Metadata SET MetaValue=CAST(CAST(MetaValue AS INTEGER)+1 AS TEXT) WHERE MetaKey='DataVersion';
                        DELETE FROM ComputedCache; DELETE FROM LeagueConstants;
                        """;cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$game",source.Id);await cmd.ExecuteNonQueryAsync(ct);
                }
                cmd.CommandText="SELECT COUNT(*) FROM PlateAppearances WHERE GameId=$game AND IsOfficial=1 AND WpaByPlate IS NULL";
                cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$game",source.Id);var remaining=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
                cmd.CommandText="""
                    INSERT INTO EstimatedWpaBackfillGames VALUES($game,$version,$updated,$remaining,$filled,$reason,$utc)
                    ON CONFLICT(GameId) DO UPDATE SET Version=excluded.Version,SourceUpdatedUtc=excluded.SourceUpdatedUtc,
                      Remaining=excluded.Remaining,Filled=excluded.Filled,Reason=excluded.Reason,CompletedUtc=excluded.CompletedUtc
                    """;
                cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);cmd.Parameters.AddWithValue("$updated",source.Updated);
                cmd.Parameters.AddWithValue("$remaining",remaining);cmd.Parameters.AddWithValue("$filled",saved.Count(v=>v.Value is not null));
                cmd.Parameters.AddWithValue("$reason",remaining==0?"complete":"table-range-or-incomplete-state-or-unsupported-ending");
                cmd.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();
                filled+=saved.Count(v=>v.Value is not null);replaced+=saved.Count(v=>v.Original.HasValue && v.Value is not null);
                cleared+=saved.Count(v=>v.Original.HasValue && v.Value is null);skipped+=remaining;
            }
            catch(Exception ex) when(ex is not OperationCanceledException){errors.Add(source.Id+": "+ex.Message);}
            completed++;
            if(completed%25==0||completed==sources.Count)progress?.Report($"{completed}/{sources.Count} games; written={filled}, replaced={replaced}, cleared={cleared}, errors={errors.Count}");
            await Task.Delay(1,ct);
        }
        return new(completed,filled,skipped,errors){Replaced=replaced,Cleared=cleared,Coverage=await GetEstimatedWpaCoverageAsync(ct)};
    }

    private async Task EnsureEstimatedWpaSchemaAsync(CancellationToken ct)
    {
        await using var db=await OpenAsync(ct);await ExecuteAsync(db,EstimatedWpaSchema,ct);
    }

    public async Task<IReadOnlyList<WpaYearCoverage>> GetEstimatedWpaCoverageAsync(CancellationToken ct=default)
    {
        await using var db=await OpenAsync(ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT g.SeasonYear,COUNT(*),
              SUM(CASE WHEN p.WpaByPlate IS NOT NULL AND (e.ModelVersion IS NOT $version OR e.Wpa IS NOT p.WpaByPlate) THEN 1 ELSE 0 END),
              SUM(CASE WHEN e.ModelVersion=$version AND e.Wpa=p.WpaByPlate THEN 1 ELSE 0 END),
              SUM(CASE WHEN p.WpaByPlate IS NULL THEN 1 ELSE 0 END)
            FROM PlateAppearances p JOIN Games g ON g.GameId=p.GameId LEFT JOIN EstimatedWpaValues e ON e.PlateAppearanceId=p.PlateAppearanceId
            WHERE g.SeasonYear BETWEEN 2016 AND 2023 AND p.IsOfficial=1 AND g.StatusCode IN ('RESULT','ENDED')
            GROUP BY g.SeasonYear ORDER BY g.SeasonYear
            """;
        cmd.Parameters.AddWithValue("$version",EstimatedWpaModel.Version);
        var result=new List<WpaYearCoverage>();await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4)));
        return result;
    }

    public async Task<NormalizedGame?> LoadGameForEstimatedWpaAsync(string gameId,CancellationToken ct=default)
    {
        var game=await LoadGameByIdAsync(gameId,GameDataProjection.Metadata|GameDataProjection.PlateAppearances|GameDataProjection.Events|GameDataProjection.RelayGroups|GameDataProjection.PlayerChanges,ct);
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
