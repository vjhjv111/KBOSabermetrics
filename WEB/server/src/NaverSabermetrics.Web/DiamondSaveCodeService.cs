using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record DiamondSaveCodeView(string? Code, bool HasData, long? UpdatedAt);

/// <summary>A recovery credential for the owner's live season and career, without copying game state.</summary>
public sealed class DiamondSaveCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string InvalidCodeMessage = "저장 코드를 확인해 주세요.";
    private readonly Func<long> _clock;
    public string DatabasePath { get; }

    public DiamondSaveCodeService(string stateDirectory, Func<long>? clock = null)
    {
        Directory.CreateDirectory(stateDirectory);
        DatabasePath = Path.Combine(Path.GetFullPath(stateDirectory), "diamond_career.db");
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var c = Open(); using var cmd = c.CreateCommand();
        // These tables have no dependency on the source warehouse or game-service initialization.
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS DiamondSaveCodes(Owner TEXT PRIMARY KEY,Code TEXT NOT NULL UNIQUE,CreatedAt INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS DiamondOwnerAliases(CookieHash TEXT PRIMARY KEY,Owner TEXT NOT NULL,CreatedAt INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_DiamondOwnerAliases_Owner ON DiamondOwnerAliases(Owner);
            CREATE TABLE IF NOT EXISTS DiamondSaveCodeLimits(Key TEXT PRIMARY KEY,Count INTEGER NOT NULL,ExpiresAt INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_DiamondSaveCodeLimits_Expires ON DiamondSaveCodeLimits(ExpiresAt);
            """;
        cmd.ExecuteNonQuery();
    }

    public long Now() => _clock();
    private SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 20 }.ToString());
        c.Open(); return c;
    }
    private static void CheckOwner(string owner)
    {
        if (!Regex.IsMatch(owner ?? "", "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
            throw new DiamondInputError("저장 세션을 확인해 주세요.", 403);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string CanonicalOwner(SqliteConnection c, SqliteTransaction tx, string hash)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT Owner FROM DiamondOwnerAliases WHERE CookieHash=$hash"; cmd.Parameters.AddWithValue("$hash", hash);
        return cmd.ExecuteScalar() as string ?? hash;
    }
    public string ResolveOwner(string cookieHash)
    {
        CheckOwner(cookieHash);
        using var c = Open(); using var tx = c.BeginTransaction(deferred: true);
        var owner = CanonicalOwner(c, tx, cookieHash); tx.Commit(); return owner;
    }
    private static bool TableExists(SqliteConnection c, SqliteTransaction tx, string table)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$table"; cmd.Parameters.AddWithValue("$table", table);
        return cmd.ExecuteScalar() != null;
    }
    private static (bool HasData, long? UpdatedAt) ReadData(SqliteConnection c, SqliteTransaction tx, string owner)
    {
        var exists = false; long? updated = null;
        if (TableExists(c, tx, "DiamondSeasons"))
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT UpdatedAt FROM DiamondSeasons WHERE Owner=$owner"; cmd.Parameters.AddWithValue("$owner", owner);
            if (cmd.ExecuteScalar() is long at) { exists = true; updated = at; }
        }
        if (TableExists(c, tx, "DiamondCareerPlayers"))
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT State FROM DiamondCareerPlayers WHERE Owner=$owner"; cmd.Parameters.AddWithValue("$owner", owner);
            if (cmd.ExecuteScalar() is string state)
            {
                exists = true;
                using var doc = JsonDocument.Parse(state);
                if (doc.RootElement.TryGetProperty("updatedAt", out var value) && value.TryGetInt64(out var at))
                    updated = updated.HasValue ? Math.Max(updated.Value, at) : at;
            }
        }
        return (exists, updated);
    }
    private static string Display(string code) => string.Join('-', Enumerable.Range(0, 6).Select(i => code.Substring(i * 4, 4)));
    private static string? Normalize(string? code)
    {
        if (code == null || code.Length > 128) return null;
        var normalized = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());
        return normalized.Length == 24 && normalized.All(c => Alphabet.Contains(c)) ? normalized : null;
    }
    private static string? ReadCode(SqliteConnection c, SqliteTransaction tx, string owner)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT Code FROM DiamondSaveCodes WHERE Owner=$owner"; cmd.Parameters.AddWithValue("$owner", owner);
        return cmd.ExecuteScalar() as string;
    }
    public DiamondSaveCodeView Get(string owner, CancellationToken token = default)
    {
        CheckOwner(owner); token.ThrowIfCancellationRequested();
        using var c = Open(); using var tx = c.BeginTransaction(deferred: true);
        owner = CanonicalOwner(c, tx, owner); var data = ReadData(c, tx, owner); var code = ReadCode(c, tx, owner);
        tx.Commit(); return new(code == null ? null : Display(code), data.HasData, data.UpdatedAt);
    }
    public DiamondSaveCodeView Save(string owner, CancellationToken token = default)
    {
        CheckOwner(owner); token.ThrowIfCancellationRequested();
        using var c = Open(); using var tx = c.BeginTransaction();
        owner = CanonicalOwner(c, tx, owner); var data = ReadData(c, tx, owner);
        if (!data.HasData) throw new DiamondInputError("먼저 리그를 시작하거나 선수를 만들어 주세요.", 409);
        var code = ReadCode(c, tx, owner);
        if (code == null)
        {
            // 24 independent base32 symbols contain 120 bits; no modulo bias with a 32-symbol alphabet.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var candidate = new string(RandomNumberGenerator.GetBytes(24).Select(b => Alphabet[b & 31]).ToArray());
                using var cmd = c.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO DiamondSaveCodes(Owner,Code,CreatedAt) VALUES($owner,$code,$at)";
                cmd.Parameters.AddWithValue("$owner", owner); cmd.Parameters.AddWithValue("$code", candidate); cmd.Parameters.AddWithValue("$at", Now());
                if (cmd.ExecuteNonQuery() == 1) { code = candidate; break; }
                code = ReadCode(c, tx, owner); if (code != null) break;
            }
            if (code == null) throw new DiamondInputError("저장 코드를 만들지 못했습니다. 다시 시도해 주세요.", 503);
        }
        token.ThrowIfCancellationRequested(); tx.Commit(); return new(Display(code), true, data.UpdatedAt);
    }
    private static long IncrementLimit(SqliteConnection c, SqliteTransaction tx, string key, long expires)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO DiamondSaveCodeLimits(Key,Count,ExpiresAt) VALUES($key,1,$expires) ON CONFLICT(Key) DO UPDATE SET Count=MIN(Count+1,1000000) RETURNING Count";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$expires", expires);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
    private static long ReadLimit(SqliteConnection c, SqliteTransaction tx, string key)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT Count FROM DiamondSaveCodeLimits WHERE Key=$key"; cmd.Parameters.AddWithValue("$key", key);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }
    /// <summary>Returns a fresh raw cookie only to the endpoint; it must never be returned in JSON or logged.</summary>
    public string Load(string? code, string ip, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); var now = Now(); var ipHash = Hash(ip ?? "unknown");
        using var c = Open(); using var tx = c.BeginTransaction();
        var minute = now / 60000; var hour = now / 3600000; var quarter = now / 900000;
        var requests = IncrementLimit(c, tx, $"load-minute:{ipHash}:{minute}", (minute + 1) * 60000);
        var hourly = IncrementLimit(c, tx, $"load-hour:{ipHash}:{hour}", (hour + 1) * 3600000);
        var failureKey = $"load-failed:{ipHash}:{quarter}";
        using (var clean = c.CreateCommand())
        {
            clean.Transaction = tx; clean.CommandText = "DELETE FROM DiamondSaveCodeLimits WHERE Key IN (SELECT Key FROM DiamondSaveCodeLimits WHERE ExpiresAt<=$now LIMIT 1000)";
            clean.Parameters.AddWithValue("$now", now); clean.ExecuteNonQuery();
        }
        if (requests > 20 || hourly > 100 || ReadLimit(c, tx, failureKey) >= 10)
        {
            tx.Commit(); throw new DiamondInputError("저장 불러오기 요청이 많습니다. 잠시 후 다시 시도해 주세요.", 429);
        }
        var normalized = Normalize(code); string? owner = null;
        if (normalized != null)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT Owner FROM DiamondSaveCodes WHERE Code=$code"; cmd.Parameters.AddWithValue("$code", normalized);
            owner = cmd.ExecuteScalar() as string;
        }
        if (owner == null || !ReadData(c, tx, owner).HasData)
        {
            IncrementLimit(c, tx, failureKey, (quarter + 1) * 900000);
            tx.Commit(); throw new DiamondInputError(InvalidCodeMessage, 400);
        }
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var cookie = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(); var cookieHash = Hash(cookie);
            if (cookieHash == owner || ReadData(c, tx, cookieHash).HasData) continue;
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO DiamondOwnerAliases(CookieHash,Owner,CreatedAt) VALUES($hash,$owner,$at)";
            cmd.Parameters.AddWithValue("$hash", cookieHash); cmd.Parameters.AddWithValue("$owner", owner); cmd.Parameters.AddWithValue("$at", now);
            if (cmd.ExecuteNonQuery() != 1) continue;
            token.ThrowIfCancellationRequested(); tx.Commit(); return cookie;
        }
        throw new DiamondInputError("저장 연결을 준비하지 못했습니다. 다시 시도해 주세요.", 503);
    }
}
