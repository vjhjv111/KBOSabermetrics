using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

// Opening the site does not queue behind stats. Concurrent misses share one DB read.
public sealed class CatalogService(DatabaseCacheService db)
{
    private readonly object sync = new();
    private Task<DatabaseCatalog>? pending;
    private DateTimeOffset expires;

    public Task<DatabaseCatalog> GetAsync(CancellationToken ct)
    {
        Task<DatabaseCatalog> task;
        lock(sync)
        {
            if(pending is null || pending.IsFaulted || pending.IsCanceled ||
                (pending.IsCompleted && DateTimeOffset.UtcNow >= expires))
                pending=Task.Run(async ()=>
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var result=await db.GetCatalogAsync(timeout.Token);
                    lock(sync) expires=DateTimeOffset.UtcNow.AddSeconds(30);
                    return result;
                });
            task=pending;
        }
        // A disconnected caller must not cancel the shared read for everyone else.
        return task.WaitAsync(ct);
    }
}
