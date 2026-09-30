using System.Text.Json;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

// All training, reconstruction, calculation and writes use Render's own persistent DB.
public sealed class EstimatedWpaBackfillWorker(SiteOptions site,ILogger<EstimatedWpaBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30),token); }
        catch(OperationCanceledException){return;}
        for(var attempt=1;attempt<=3;attempt++)
        {
            try
            {
                var report=await Task.Run(async()=>
                {
                    if(!File.Exists(site.DatabasePath))throw new FileNotFoundException("Live DB is not ready",site.DatabasePath);
                    var writer=new DatabaseCacheService(site.DatabasePath);
                    var models=await writer.BuildEstimatedWpaModelsAsync(token);
                    logger.LogInformation("[WPA] Render DB {Database}: {Models} models added; backfilling missing 2016-2023 WPA",site.DatabasePath,models);
                    return await writer.BackfillEstimatedWpaAsync(progress:new Progress<string>(message=>logger.LogInformation("[WPA] {Progress}",message)),ct:token);
                },token);
                var output=Path.Combine(site.StateDirectory,"wpa-backfill-report.json");
                var temporary=output+".tmp";
                await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new{version=EstimatedWpaModel.Version,completedUtc=DateTime.UtcNow,report},new JsonSerializerOptions{WriteIndented=true}),token);
                File.Move(temporary,output,true);
                logger.LogInformation("[WPA] Backfill complete: {Games} games, {Filled} filled, {Skipped} unresolved, {Errors} errors. Report: {Report}",report.Games,report.Filled,report.Skipped,report.Errors.Count,output);
                if(report.Errors.Count==0)return;
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogError(ex,"[WPA] Backfill attempt {Attempt} failed; committed games are retained and safe to resume",attempt);}
            if(attempt<3)
            {
                try{await Task.Delay(TimeSpan.FromMinutes(1),token);}
                catch(OperationCanceledException){return;}
            }
        }
    }
}
