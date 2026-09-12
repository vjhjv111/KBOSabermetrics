using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private static async Task SaveGameMetadataAsync(SqliteConnection connection,SqliteTransaction transaction,string gameId,string sourceJson,CancellationToken ct)
    {
        using var json=JsonDocument.Parse(sourceJson);
        var root=json.RootElement;
        if(root.TryGetProperty("naver",out var naver))root=naver;
        if(!root.TryGetProperty("result",out var result)||!result.TryGetProperty("game",out var game))return;
        if(!game.TryGetProperty("gameId",out var id)||id.GetString()!=gameId)throw new InvalidDataException("경기 메타데이터와 저장 대상 ID가 다릅니다.");
        string? Value(string key)=>game.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
        string? Scores(string key)=>game.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?v.GetRawText():null;
        await using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="CREATE TABLE IF NOT EXISTS GameMetadata(GameId TEXT PRIMARY KEY REFERENCES Games(GameId) ON DELETE CASCADE,WinPitcher TEXT,LosePitcher TEXT,SavePitcher TEXT,HomeInnings TEXT,AwayInnings TEXT); INSERT OR REPLACE INTO GameMetadata VALUES($id,$win,$lose,$save,$home,$away);";
        cmd.Parameters.AddWithValue("$id",gameId);cmd.Parameters.AddWithValue("$win",(object?)Value("winPitcherName")??DBNull.Value);cmd.Parameters.AddWithValue("$lose",(object?)Value("losePitcherName")??DBNull.Value);cmd.Parameters.AddWithValue("$save",(object?)(Value("savePitcherName")??Value("savePitcher"))??DBNull.Value);cmd.Parameters.AddWithValue("$home",(object?)Scores("homeTeamScoreByInning")??DBNull.Value);cmd.Parameters.AddWithValue("$away",(object?)Scores("awayTeamScoreByInning")??DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
