namespace NaverRelay.Application.Queries;

/// <summary>
/// WinForms와 ASP.NET Core가 동일하게 사용할 관계형 데이터 조회 계약입니다.
/// 구현체는 SQLite 또는 PostgreSQL로 교체할 수 있습니다.
/// </summary>
public interface IWarehouseReadService
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<DatabaseCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<DatabaseFilterOptions> GetFilterOptionsAsync(
        GameQuery baseQuery,
        CancellationToken cancellationToken = default);
    Task<DatabaseSummary> GetSummaryAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatabaseGameHeader>> GetGameHeadersAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);
}

public interface IAnalyticsQueryService
{
    Task<AnalyticsSnapshot> GetSnapshotAsync(
        GameQuery query,
        LeagueReference league,
        IProgress<DatabaseLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
