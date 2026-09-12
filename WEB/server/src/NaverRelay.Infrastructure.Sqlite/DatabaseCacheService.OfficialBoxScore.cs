using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private const string OfficialBoxSourceSchema = """
        CREATE TABLE IF NOT EXISTS OfficialBoxScorePlayers(
            GameId TEXT NOT NULL REFERENCES Games(GameId) ON DELETE CASCADE,
            Pcode TEXT NOT NULL, TeamCode TEXT NOT NULL, Role TEXT NOT NULL,
            DownloadedUtc TEXT NULL, StatsJson TEXT NOT NULL,
            PRIMARY KEY(GameId,Pcode,TeamCode,Role));
        """;

    private static async Task SaveOfficialBoxSourcesAsync(SqliteConnection connection, SqliteTransaction transaction,
        NormalizedGame game, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = OfficialBoxSourceSchema; await command.ExecuteNonQueryAsync(ct);
        async Task Save(string? code, string? team, string role, DateTimeOffset? date, object stats)
        {
            command.CommandText = "INSERT INTO OfficialBoxScorePlayers VALUES($id,$code,$team,$role,$utc,$json)";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$id",game.GameId);
            command.Parameters.AddWithValue("$code",code!); command.Parameters.AddWithValue("$team",team!);
            command.Parameters.AddWithValue("$role",role);
            command.Parameters.AddWithValue("$utc",(object?)date?.UtcDateTime.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(stats));
            await command.ExecuteNonQueryAsync(ct);
        }
        foreach (var line in game.BattingLines.Where(b => b.OfficialStats != null))
            await Save(line.Pcode,line.TeamCode,"batter",line.OfficialStats!.SourceTime,line.OfficialStats);
        foreach (var line in game.PitchingLines.Where(p => p.OfficialStats != null))
            await Save(line.Pcode,line.TeamCode,"pitcher",line.OfficialStats!.SourceTime,line.OfficialStats);
    }

    private static bool SourceIsAtLeastAsNew(DateTimeOffset? incoming, DateTimeOffset? existing) =>
        incoming.HasValue && (!existing.HasValue || incoming.Value >= existing.Value);

    private static DateTimeOffset? OfficialSourceTime(string? value) =>
        DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var time) ? time : null;
}
