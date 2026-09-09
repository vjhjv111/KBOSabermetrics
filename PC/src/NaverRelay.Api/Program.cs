using NaverRelay.Application.Players;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Application.Teams;
using NaverRelay.Infrastructure.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var configuredPath = builder.Configuration["Database:Path"]
    ?? Environment.GetEnvironmentVariable("NAVER_SABERMETRICS_DB");

builder.Services.AddSingleton(_ => new DatabaseCacheService(configuredPath));
builder.Services.AddSingleton<DatabaseAnalyticsService>();
builder.Services.AddSingleton<DatabaseBatterRecordRoomService>();
builder.Services.AddSingleton<IBatterRecordRoomQueryService>(serviceProvider =>
    serviceProvider.GetRequiredService<DatabaseBatterRecordRoomService>());
builder.Services.AddSingleton<DatabasePitcherRecordRoomService>();
builder.Services.AddSingleton<IPitcherRecordRoomQueryService>(serviceProvider =>
    serviceProvider.GetRequiredService<DatabasePitcherRecordRoomService>());
builder.Services.AddSingleton<DatabasePlayerPageService>();
builder.Services.AddSingleton<IPlayerPageService>(serviceProvider =>
    serviceProvider.GetRequiredService<DatabasePlayerPageService>());
builder.Services.AddSingleton<DatabaseTeamPageService>();
builder.Services.AddSingleton<ITeamPageService>(serviceProvider =>
    serviceProvider.GetRequiredService<DatabaseTeamPageService>());
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var database = app.Services.GetRequiredService<DatabaseCacheService>();
await database.InitializeAsync();

app.MapGet("/api/health", async (DatabaseCacheService db, CancellationToken cancellationToken) =>
{
    var catalog = await db.GetCatalogAsync(cancellationToken);
    return Results.Ok(new
    {
        status = "ok",
        database = db.DatabasePath,
        games = catalog.GameCount,
        minDate = catalog.MinGameDate,
        maxDate = catalog.MaxGameDate,
    });
});

app.MapGet("/api/catalog", async (DatabaseCacheService db, CancellationToken cancellationToken) =>
    Results.Ok(await db.GetCatalogAsync(cancellationToken)));

app.MapGet("/api/games", async (
    DatabaseCacheService db,
    int? year,
    string? competition,
    string? team,
    string? opponent,
    string? venue,
    string? stadium,
    DateTime? startDate,
    DateTime? endDate,
    int? recentGames,
    CancellationToken cancellationToken) =>
{
    var query = BuildQuery(
        year, competition, team, opponent, venue, stadium, startDate, endDate, recentGames);

    return Results.Ok(await db.GetGameHeadersAsync(query, cancellationToken));
});


app.MapGet("/api/stats/batters", async (
    DatabaseCacheService db,
    DatabaseAnalyticsService analytics,
    int? year,
    string? competition,
    string? team,
    DateTime? startDate,
    DateTime? endDate,
    CancellationToken cancellationToken) =>
{
    var query = BuildQuery(year, competition, team, null, null, null, startDate, endDate, null);
    var league = await db.GetLeagueReferenceAsync(cancellationToken: cancellationToken);
    var snapshot = await analytics.GetSnapshotAsync(query, league, cancellationToken: cancellationToken);
    return Results.Ok(new
    {
        classic = snapshot.BatterClassic,
        sabermetrics = snapshot.BatterSabermetrics,
        discipline = snapshot.BatterDiscipline,
        value = snapshot.BatterValues,
    });
});

app.MapGet("/api/stats/pitchers", async (
    DatabaseCacheService db,
    DatabaseAnalyticsService analytics,
    int? year,
    string? competition,
    string? team,
    DateTime? startDate,
    DateTime? endDate,
    CancellationToken cancellationToken) =>
{
    var query = BuildQuery(year, competition, team, null, null, null, startDate, endDate, null);
    var league = await db.GetLeagueReferenceAsync(cancellationToken: cancellationToken);
    var snapshot = await analytics.GetSnapshotAsync(query, league, cancellationToken: cancellationToken);
    return Results.Ok(new
    {
        classic = snapshot.PitcherClassic,
        sabermetrics = snapshot.PitcherSabermetrics,
        discipline = snapshot.PitcherDiscipline,
        value = snapshot.PitcherValues,
    });
});

app.MapGet("/api/record-room/batters/{view}", async (
    string view,
    DatabaseCacheService db,
    DatabaseAnalyticsService analytics,
    IBatterRecordRoomQueryService recordRoom,
    string? room,
    int? year,
    string? competition,
    string? team,
    string? opponent,
    string? venue,
    string? stadium,
    DateTime? startDate,
    DateTime? endDate,
    CancellationToken cancellationToken) =>
{
    var query = BuildQuery(year, competition, team, opponent, venue, stadium, startDate, endDate, null) with
    {
        Grouping = ParseGrouping(room),
        SeasonYear = ParseGrouping(room) == AnalyticsGrouping.PlayerCareer ? null : year,
    };
    var league = await db.GetLeagueReferenceAsync(cancellationToken: cancellationToken);
    var snapshot = await analytics.GetSnapshotAsync(query, league, cancellationToken: cancellationToken);
    var result = view.Trim().ToLowerInvariant() switch
    {
        "advanced" => (object)RecordRoomRowFactory.BuildAdvanced(snapshot),
        "value" => RecordRoomRowFactory.BuildValue(snapshot),
        "extended" => RecordRoomRowFactory.BuildExtended(snapshot),
        "clutch" => await recordRoom.GetClutchAsync(query, league, cancellationToken),
        "power" => RecordRoomRowFactory.BuildPower(snapshot),
        "team-batting" => RecordRoomRowFactory.BuildTeamBatting(snapshot),
        "steal" => RecordRoomRowFactory.BuildSteal(snapshot),
        "baserunning" => RecordRoomRowFactory.BuildBaserunning(snapshot),
        "batted-ball" => await recordRoom.GetBattedBallAsync(query, cancellationToken),
        "direction" => await recordRoom.GetDirectionAsync(query, cancellationToken),
        "discipline" => RecordRoomRowFactory.BuildPitchProfile(snapshot),
        "pitch-types" => await recordRoom.GetPitchTypesAsync(query, cancellationToken),
        _ => RecordRoomRowFactory.BuildBasic(snapshot),
    };
    return Results.Ok(result);
});

app.MapGet("/api/record-room/pitchers/{view}", async (
    string view,
    DatabaseCacheService db,
    DatabaseAnalyticsService analytics,
    IPitcherRecordRoomQueryService recordRoom,
    string? room,
    int? year,
    string? competition,
    string? team,
    string? opponent,
    string? venue,
    string? stadium,
    DateTime? startDate,
    DateTime? endDate,
    CancellationToken cancellationToken) =>
{
    var grouping = ParseGrouping(room);
    var query = BuildQuery(year, competition, team, opponent, venue, stadium, startDate, endDate, null) with
    {
        Grouping = grouping,
        SeasonYear = grouping == AnalyticsGrouping.PlayerCareer ? null : year,
    };
    var league = await db.GetLeagueReferenceAsync(cancellationToken: cancellationToken);
    var snapshot = await analytics.GetSnapshotAsync(query, league, cancellationToken: cancellationToken);
    var values = PitcherRecordRoomRowFactory.BuildValue(snapshot, league);
    object result;
    switch (view.Trim().ToLowerInvariant())
    {
        case "advanced":
            result = PitcherRecordRoomRowFactory.BuildAdvanced(snapshot);
            break;
        case "value":
            result = values;
            break;
        case "extended":
            result = await recordRoom.GetExtendedAsync(query, cancellationToken);
            break;
        case "wp":
        case "win-probability":
            result = await recordRoom.GetWinProbabilityAsync(query, league, cancellationToken);
            break;
        case "runner":
        case "baserunning":
            result = await recordRoom.GetRunnerAsync(query, cancellationToken);
            break;
        case "starter":
        {
            var rows = (await recordRoom.GetStarterAsync(query, cancellationToken)).ToList();
            ApplyStarterWar(rows, values);
            result = rows;
            break;
        }
        case "reliever":
        {
            var rows = (await recordRoom.GetRelieverAsync(query, league, cancellationToken)).ToList();
            ApplyRelieverWar(rows, values);
            result = rows;
            break;
        }
        case "batted-ball":
            result = await recordRoom.GetBattedBallAsync(query, cancellationToken);
            break;
        case "direction":
            result = await recordRoom.GetDirectionAsync(query, cancellationToken);
            break;
        case "discipline":
        case "pitch-profile":
            result = await recordRoom.GetPitchProfileAsync(query, cancellationToken);
            break;
        case "pitch-types":
            result = await recordRoom.GetPitchTypesAsync(query, cancellationToken);
            break;
        default:
            result = PitcherRecordRoomRowFactory.BuildBasic(snapshot);
            break;
    }
    return Results.Ok(result);
});

app.MapGet("/api/players/search", async (
    DatabasePlayerPageService players,
    string? q,
    int? limit,
    CancellationToken cancellationToken) =>
{
    var safeLimit = Math.Clamp(limit ?? 50, 1, 200);
    return Results.Ok(await players.SearchPlayersAsync(q, safeLimit, cancellationToken));
});

app.MapGet("/api/players/{pcode}", async (
    DatabasePlayerPageService players,
    string pcode,
    CancellationToken cancellationToken) =>
{
    var result = await players.GetPlayerPageAsync(pcode, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/teams", async (
    DatabaseTeamPageService teams,
    CancellationToken cancellationToken) =>
    Results.Ok(await teams.GetTeamsAsync(cancellationToken)));

app.MapGet("/api/teams/{teamCode}", async (
    DatabaseTeamPageService teams,
    string teamCode,
    CancellationToken cancellationToken) =>
{
    var result = await teams.GetTeamPageAsync(teamCode, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.Run();

static GameQuery BuildQuery(
    int? year,
    string? competition,
    string? team,
    string? opponent,
    string? venue,
    string? stadium,
    DateTime? startDate,
    DateTime? endDate,
    int? recentGames) => new()
{
    SeasonYear = year,
    Competition = string.IsNullOrWhiteSpace(competition) ? "정규시즌" : competition.Trim(),
    TeamCode = Empty(team),
    OpponentCode = Empty(opponent),
    Venue = Empty(venue),
    Stadium = Empty(stadium),
    StartDate = startDate?.Date,
    EndDate = endDate?.Date,
    RecentGameCount = recentGames,
};

static AnalyticsGrouping ParseGrouping(string? room) => room?.Trim().ToLowerInvariant() switch
{
    "career" or "통산" => AnalyticsGrouping.PlayerCareer,
    "team" or "팀" => AnalyticsGrouping.Team,
    _ => AnalyticsGrouping.PlayerByTeam,
};

static void ApplyStarterWar(
    IReadOnlyList<PitcherStarterRecordRow> rows,
    IReadOnlyList<PitcherDetailedValueRecordRow> values)
{
    var lookup = values.ToDictionary(
        row => PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode),
        StringComparer.Ordinal);
    foreach (var row in rows)
        if (lookup.TryGetValue(PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode), out var value))
            row.StarterWar = value.StarterWar;
}

static void ApplyRelieverWar(
    IReadOnlyList<PitcherRelieverRecordRow> rows,
    IReadOnlyList<PitcherDetailedValueRecordRow> values)
{
    var lookup = values.ToDictionary(
        row => PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode),
        StringComparer.Ordinal);
    foreach (var row in rows)
        if (lookup.TryGetValue(PitcherRecordRoomRowFactory.Key(row.Pcode, row.TeamCode), out var value))
            row.ReliefWar = value.ReliefWar;
}

static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
