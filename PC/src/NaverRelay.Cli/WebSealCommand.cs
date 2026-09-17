using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;

internal static class WebSealCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: --seal-web <existing-db>");
            return 2;
        }

        var path = Path.GetFullPath(args[1]);
        try
        {
            var db = new DatabaseCacheService(path);
            await db.InitializeAsync();
            _ = await db.GetLeagueReferenceAsync();
            SqliteConnection.ClearAllPools();

            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandTimeout = 600;
                command.CommandText = "ANALYZE; PRAGMA optimize; PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            Console.WriteLine($"SEALED_WEB_DB={path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
