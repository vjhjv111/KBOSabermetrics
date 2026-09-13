using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverSabermetrics.Web;

internal static class CreationLimits
{
    public static int Run(string lib, string output, string source)
    {
        var directory = Path.Combine(output, "admission-state");
        long now = 1700000000000; var checks = 0;
        DiamondSeasonService Service() => new(directory, lib, new(source), () => now, new Random(913).NextDouble);
        var service = Service();
        JsonElement Request(string id, string team = "HH") => JsonSerializer.SerializeToElement(new
        { op = "create", requestId = "admission-" + id, season = 2025, team, pace = "full", seriesPerPair = 1 });
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidDataException(name); }
        void Error(Action action, int status, string name)
        {
            try { action(); throw new InvalidDataException(name + " did not reject"); }
            catch (DiamondInputError error) { Check(error.Status == status, name); }
        }
        long Count(string table)
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = service.DatabasePath }.ToString()); c.Open();
            using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM " + table;
            return (long)cmd.ExecuteScalar()!;
        }
        void Exhaust(string key)
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = service.DatabasePath }.ToString()); c.Open();
            using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE DiamondSeasonCreationLimits SET Count=2000 WHERE Key=$key";
            cmd.Parameters.AddWithValue("$key", key); Check(cmd.ExecuteNonQuery() == 1, "expected global admission bucket exists");
        }
        Error(() => service.Post(Request("invalid", "missing"), "invalid-owner", ip: "ip-a"), 400, "invalid team remains input error");
        Check(Count("DiamondSeasonCreationLimits") == 0, "rejected creation consumes no durable quota");
        for (var i = 0; i < 20; i++) service.Post(Request(i.ToString()), "owner-" + i, ip: "ip-a");
        Check(Count("DiamondSeasons") == 20, "twenty separate owners admitted on one IP");
        var beforeRetry = Count("DiamondSeasonRequests");
        Check(service.Post(Request("0"), "owner-0", ip: "ip-a").Save != null && Count("DiamondSeasonRequests") == beforeRetry,
            "idempotent create retry works at admission limit without another request record");
        service = Service();
        Error(() => service.Post(Request("ip-denied"), "rotated-owner", ip: "ip-a"), 429, "cookie rotation and service restart retain IP limit");
        Check(service.Get("rotated-owner").Save == null && Count("DiamondSeasonRequests") == beforeRetry,
            "denied creation persists neither league nor request");
        service.Post(Request("other-ip"), "other-ip-owner", ip: "ip-b");
        Check(Count("DiamondSeasons") == 21, "independent IP may still create a league");
        Exhaust("global-hour:" + now / 3600000);
        Error(() => service.Post(Request("global-denied"), "new-owner", ip: "new-ip"), 429, "global limit blocks rotating IPs");
        now = (now / 3600000 + 1) * 3600000;
        service.Post(Request("next-hour"), "next-hour-owner", ip: "ip-a");
        Check(service.Get("next-hour-owner").Save != null, "hour boundary restores admission");
        Exhaust("global-day:" + now / 86400000);
        Error(() => service.Post(Request("daily-denied"), "daily-owner", ip: "daily-ip"), 429, "daily global bound survives hourly reset");
        now = (now / 86400000 + 1) * 86400000;
        service.Post(Request("next-day"), "next-day-owner", ip: "ip-a");
        Check(service.Get("next-day-owner").Save != null, "day boundary restores admission");
        Check(Count("DiamondSeasonCreationLimits") == 4, "expired admission buckets are removed");
        return checks;
    }
}
