using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record OfficialPlayerProfile(
    string Code, string? Name, string? TeamName, string? UniformNumber, string? BirthDate,
    string? Position, string? BatsThrows, int? HeightCm, int? WeightKg, string? Career,
    string? SigningBonusText, string? SalaryText, string? DraftText, string? EntryYearText,
    int? ProfileSeason, string? SourceUrl, string? PhotoSourceUrl, string? PhotoUrl,
    string? FetchedUtc, string? LastCheckedUtc);

public sealed record OfficialPlayerPhoto(byte[] Bytes, string ContentType);

// The offline collector owns this optional table and the adjacent image files.
// Public requests only read them; historical game lines remain independent.
public sealed class OfficialPlayerProfileService(DatabaseCacheService db, SiteOptions options)
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    public async Task<OfficialPlayerProfile?> GetAsync(string code, CancellationToken ct)
    {
        if (!IsOfficialCode(code)) return null;
        await using var connection = await OpenAsync(ct);
        if (!await HasProfilesAsync(connection, ct)) return null;
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.QuerySeconds;
        command.CommandText = """
            SELECT Name,TeamName,UniformNumber,BirthDate,Position,BatsThrows,HeightCm,WeightKg,
                   Career,SigningBonusText,SalaryText,DraftText,EntryYearText,ProfileSeason,
                   SourceUrl,PhotoSourceUrl,PhotoFileName,FetchedUtc,LastCheckedUtc
            FROM OfficialPlayerProfiles WHERE Pcode=$code LIMIT 1
            """;
        command.Parameters.AddWithValue("$code", code);
        using var cancellation = ct.Register(command.Cancel);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        string? String(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        int? Number(int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
        var photo = ResolvePhoto(code, String(16));
        return new(code, String(0), String(1), String(2), String(3), String(4), String(5),
            Number(6), Number(7), String(8), String(9), String(10), String(11), String(12),
            Number(13), SafeSourceUrl(String(14), false), SafeSourceUrl(String(15), true),
            photo is null ? null : $"/api/player-photo/{code}", String(17), String(18));
    }

    public async Task<OfficialPlayerPhoto?> GetPhotoAsync(string code, CancellationToken ct)
    {
        if (!IsOfficialCode(code)) return null;
        await using var connection = await OpenAsync(ct);
        if (!await HasProfilesAsync(connection, ct)) return null;
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.QuerySeconds;
        command.CommandText = "SELECT PhotoFileName FROM OfficialPlayerProfiles WHERE Pcode=$code LIMIT 1";
        command.Parameters.AddWithValue("$code", code);
        using var cancellation = ct.Register(command.Cancel);
        var name = await command.ExecuteScalarAsync(ct) as string;
        var photo = ResolvePhoto(code, name);
        if (photo is null) return null;
        try
        {
            await using var file = new FileStream(photo.Value.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 8 or > MaxPhotoBytes) return null;
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, ct);
            return DetectContentType(bytes) == photo.Value.ContentType ? new(bytes, photo.Value.ContentType) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = db.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private
        }.ToString());
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private async Task<bool> HasProfilesAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.QuerySeconds;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OfficialPlayerProfiles' LIMIT 1";
        using var cancellation = ct.Register(command.Cancel);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private (string Path, string ContentType)? ResolvePhoto(string code, string? name)
    {
        // Only collector-generated basenames may leave the DB; never accept a path or an SVG.
        if (name != $"{code}.jpg" && name != $"{code}.jpeg" && name != $"{code}.png") return null;
        var directory = string.IsNullOrWhiteSpace(options.PlayerPhotoDirectory)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(db.DatabasePath))!, "player-photos")
            : Path.GetFullPath(options.PlayerPhotoDirectory);
        var path = Path.Combine(directory, name);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 8 or > MaxPhotoBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[8];
            file.ReadExactly(header);
            var type = DetectContentType(header);
            return type is null ? null : (path, type);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool IsOfficialCode(string code) => code.Length is >= 4 and <= 10 && code.All(char.IsAsciiDigit);

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        return null;
    }

    private static string? SafeSourceUrl(string? value, bool photo)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        var official = uri.Host.Equals("koreabaseball.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".koreabaseball.com", StringComparison.OrdinalIgnoreCase);
        var officialImages = photo && uri.Host.Equals("6ptotvmi5753.edge.naverncp.com", StringComparison.OrdinalIgnoreCase);
        return official || officialImages ? uri.AbsoluteUri : null;
    }
}
