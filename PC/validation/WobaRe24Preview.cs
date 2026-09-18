using System.Reflection;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;

internal static class WobaRe24Preview
{
    public static async Task Run(string databasePath)
    {
        var immutablePath = Path.GetFullPath(databasePath).Replace('\\', '/');
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = $"file:{immutablePath}?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync().ConfigureAwait(false);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA temp_store=MEMORY;";
            await pragma.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var method = typeof(DatabaseCacheService).GetMethod(
            "ReadWobaConstantsAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("ReadWobaConstantsAsync");
        dynamic task = method.Invoke(null, [connection, CancellationToken.None])
            ?? throw new InvalidOperationException("RE24 계산을 시작하지 못했습니다.");
        await task;
        dynamic result = task.Result;
        WobaConstants overall = result.Item1;
        Dictionary<int, WobaConstants> seasons = result.Item2;

        Print("전체", overall);
        foreach (var pair in seasons.OrderBy(pair => pair.Key))
            Print(pair.Key.ToString(), pair.Value);
    }

    private static void Print(string label, WobaConstants value) => Console.WriteLine(
        $"{label,-8} {value.Source,-31} PA={value.SamplePlateAppearances,7:N0} " +
        $"Scale={value.Scale:0.0000} uBB={value.UnintentionalWalk:0.0000} " +
        $"HBP={value.HitByPitch:0.0000} 1B={value.Single:0.0000} 2B={value.Double:0.0000} " +
        $"3B={value.Triple:0.0000} HR={value.HomeRun:0.0000} lgwOBA={value.LeagueWoba:0.0000}");
}
