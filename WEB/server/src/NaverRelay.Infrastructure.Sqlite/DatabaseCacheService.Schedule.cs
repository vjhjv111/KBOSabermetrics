using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// 아직 시작하지 않은(예정) 경기를 경기일정 화면에만 노출하기 위한 자리표시자 입력입니다.
/// 박스스코어가 없으므로 팀·일정 정보만 담습니다.
/// </summary>
public sealed record ScheduledGamePlaceholder(
    string DatabaseGameId, int SeasonYear, string GameDate, string? GameDateTime,
    string HomeTeamCode, string AwayTeamCode, string? Stadium, string StatusCode);

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// 예정 경기 자리표시자 행에 사용하는 RoundCode입니다. 정규시즌 실제 경기의 'kbo_r'과
    /// 반드시 달라야 합니다 — 이 사이트의 WAR·순위·평균 등 거의 모든 통계 쿼리가
    /// RoundCode='kbo_r'만 "끝난 경기"로 취급하므로, 다른 값을 쓰면 자리표시자 행이
    /// 그 쿼리들에서 자동으로 제외됩니다. 경기일정 화면(GameWebService)은 RoundCode로
    /// 거르지 않으므로 정상적으로 노출됩니다.
    /// </summary>
    public const string ScheduledPlaceholderRoundCode = "kbo_scheduled";

    /// <summary>
    /// 예정된 정규시즌 경기를 Games 테이블에 자리표시자 행으로 기록합니다. 이미 존재하는
    /// GameId(끝난 실제 경기 또는 이전에 기록한 자리표시자)는 건드리지 않습니다 — 실제
    /// 경기 반영은 항상 DeleteExistingGameAsync + InsertGameAsync 경로(완전한 박스스코어)를
    /// 거치므로, 같은 GameId가 나중에 그 경로로 들어오면 이 자리표시자는 자동으로
    /// 교체됩니다. DataVersion은 올리지 않습니다 — 자리표시자는 WAR·순위·리그 평균 등
    /// DataVersion 캐시가 보여주는 어떤 값도 바꾸지 않기 때문입니다.
    /// </summary>
    public async Task<int> UpsertScheduledGamesAsync(
        IReadOnlyList<ScheduledGamePlaceholder> games,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(games);
        if (games.Count == 0) return 0;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var inserted = 0;
        var updatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var game in games)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO Games(
                    GameId, SeasonYear, GameDate, GameDateTime, SuperCategoryId, UpperCategoryId,
                    UpperCategoryName, CategoryId, CategoryName, RoundCode, CompetitionType, Stadium,
                    StatusCode, Winner, AwayTeamCode, AwayTeamName, AwayScore, AwayHits, AwayErrors,
                    AwayWalks, HomeTeamCode, HomeTeamName, HomeScore, HomeHits, HomeErrors, HomeWalks,
                    LastHomeWinRate, LastAwayWinRate, LastWpaByPlate, CompletedPaCount, PitchCount,
                    MissingPtsCount, RunnerCount, PlayerChangeCount, AdministrativeCount, WarningCount,
                    ErrorCount, UpdatedUtc)
                VALUES(
                    $gameId, $seasonYear, $gameDate, $gameDateTime, NULL, 'kbaseball',
                    NULL, 'kbo', NULL, $roundCode, $competitionType, $stadium,
                    $statusCode, NULL, $awayTeamCode, NULL, NULL, NULL, NULL,
                    NULL, $homeTeamCode, NULL, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL, 0, 0,
                    0, 0, 0, 0, 0,
                    0, $updatedUtc);
                """;
            Add(command, "$gameId", game.DatabaseGameId);
            Add(command, "$seasonYear", game.SeasonYear);
            Add(command, "$gameDate", NormalizeDate(game.GameDate));
            Add(command, "$gameDateTime", game.GameDateTime);
            Add(command, "$roundCode", ScheduledPlaceholderRoundCode);
            Add(command, "$competitionType", (int)GameCompetitionType.RegularSeason);
            Add(command, "$stadium", game.Stadium);
            Add(command, "$statusCode", game.StatusCode);
            Add(command, "$awayTeamCode", game.AwayTeamCode);
            Add(command, "$homeTeamCode", game.HomeTeamCode);
            Add(command, "$updatedUtc", updatedUtc);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    /// <summary>
    /// 주어진 (홈,원정) 방향으로 이미 DB에 기록된 정규시즌 실제 경기 수를 반환합니다.
    /// 예정 경기가 공식 홈/원정 배정 한도를 넘는지 검사해 포스트시즌 후보를 걸러내는 데
    /// 씁니다(정규시즌 라운드 코드로 한정하므로 이 검사 자체는 자리표시자 행의 영향을
    /// 받지 않습니다).
    /// </summary>
    public async Task<int> CountRegularSeasonHomeGamesAsync(
        int seasonYear, string homeTeamCode, string awayTeamCode, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM Games
            WHERE SeasonYear=$year AND LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'
              AND HomeTeamCode=$home AND AwayTeamCode=$away;
            """;
        Add(command, "$year", seasonYear);
        Add(command, "$home", homeTeamCode);
        Add(command, "$away", awayTeamCode);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }
}
