using NaverRelay.Application.Statistics;

namespace NaverRelay.Application.Queries;

/// <summary>
/// 기록실의 타자 세부 탭에서 사용하는 관계형 조회 계약입니다.
/// JSON을 다시 읽지 않고 PlateAppearances/Pitches만 집계합니다.
/// </summary>
public interface IBatterRecordRoomQueryService
{
    Task<IReadOnlyList<BatterClutchRecordRow>> GetClutchAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatterBattedBallRecordRow>> GetBattedBallAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatterDirectionRecordRow>> GetDirectionAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatterPitchTypeRecordRow>> GetPitchTypesAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 투수 기록실의 확장/WP/주자/선발/구원/타구/투구/구종 탭 조회 계약입니다.
/// 최초 적재 후에는 관계형 SQLite 테이블만 사용합니다.
/// </summary>
public interface IPitcherRecordRoomQueryService
{
    Task<IReadOnlyList<PitcherExtendedRecordRow>> GetExtendedAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherWinProbabilityRecordRow>> GetWinProbabilityAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherRunnerRecordRow>> GetRunnerAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherStarterRecordRow>> GetStarterAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherRelieverRecordRow>> GetRelieverAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherBattedBallRecordRow>> GetBattedBallAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherDirectionRecordRow>> GetDirectionAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherPitchProfileRecordRow>> GetPitchProfileAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitcherPitchTypeRecordRow>> GetPitchTypesAsync(
        GameQuery query,
        CancellationToken cancellationToken = default);
}
