using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private static async Task SaveGameMetadataAsync(SqliteConnection connection,SqliteTransaction transaction,string gameId,InputDocument document,CancellationToken ct)
    {
        using var json=JsonDocument.Parse(await document.ReadJsonAsync(ct));
        if(!json.RootElement.TryGetProperty("result",out var result)||!result.TryGetProperty("game",out var game))return;
        string? Value(string key)=>game.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
        string? Scores(string key)=>game.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?v.GetRawText():null;
        await using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="CREATE TABLE IF NOT EXISTS GameMetadata(GameId TEXT PRIMARY KEY REFERENCES Games(GameId) ON DELETE CASCADE,WinPitcher TEXT,LosePitcher TEXT,SavePitcher TEXT,HomeInnings TEXT,AwayInnings TEXT); INSERT OR REPLACE INTO GameMetadata VALUES($id,$win,$lose,$save,$home,$away);";
        cmd.Parameters.AddWithValue("$id",gameId);cmd.Parameters.AddWithValue("$win",(object?)Value("winPitcherName")??DBNull.Value);cmd.Parameters.AddWithValue("$lose",(object?)Value("losePitcherName")??DBNull.Value);cmd.Parameters.AddWithValue("$save",(object?)(Value("savePitcherName")??Value("savePitcher"))??DBNull.Value);cmd.Parameters.AddWithValue("$home",(object?)Scores("homeTeamScoreByInning")??DBNull.Value);cmd.Parameters.AddWithValue("$away",(object?)Scores("awayTeamScoreByInning")??DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
