using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

/// <summary>Bounded concurrency; busy requests fail rather than becoming an unbounded queue.</summary>
public sealed class QueryGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly SiteOptions _options;
    public QueryGate(SiteOptions options) { _options = options; _semaphore = new(Math.Clamp(options.ConcurrentQueries,1,4)); }
    public async Task<T> RunAsync<T>(Func<CancellationToken,Task<T>> action, CancellationToken requestToken)
    {
        if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5), requestToken))
            throw new RequestError("조회가 몰리고 있습니다. 잠시 후 다시 시도하세요.", 429, "QUERY_BUSY");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.QuerySeconds));
        try
        {
            // Microsoft.Data.Sqlite async calls execute synchronously. Do not execute on the request continuation.
            return await Task.Run(() => action(timeout.Token), timeout.Token);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 9 && requestToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(requestToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 9 && timeout.IsCancellationRequested)
        {
            throw new RequestError("조회 제한시간을 초과했습니다. 기간이나 팀을 좁혀 주세요.", 408, "QUERY_TIMEOUT");
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            throw new RequestError("조회 제한시간을 초과했습니다. 조건을 좁혀 주세요.", 408, "QUERY_TIMEOUT");
        }
        finally { _semaphore.Release(); }
    }
    public void Dispose() => _semaphore.Dispose();
}
