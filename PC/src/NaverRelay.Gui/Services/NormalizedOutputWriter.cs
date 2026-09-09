using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NaverRelay.Parsing;

namespace NaverRelay.Gui.Services;

internal static class NormalizedOutputWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static async Task<string> SaveGameAsync(
        NormalizedGame game,
        string outputDirectory,
        string sourceName,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var baseName = !string.IsNullOrWhiteSpace(game.GameId)
            ? game.GameId
            : Path.GetFileNameWithoutExtension(sourceName);
        var safeBaseName = SanitizeFileName(baseName);
        var path = GetAvailablePath(outputDirectory, $"{safeBaseName}.normalized.json");
        var json = JsonSerializer.Serialize(game, SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public static async Task<string> SaveAggregateSummaryAsync(
        IReadOnlyCollection<NormalizedGame> games,
        IReadOnlyCollection<(string Source, string Error)> failures,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var aggregate = new
        {
            generatedAtLocal = DateTimeOffset.Now,
            gameCount = games.Count,
            failedCount = failures.Count,
            totals = new
            {
                rawRelayGroups = games.Sum(game => game.Summary.RawRelayGroupCount),
                rawEvents = games.Sum(game => game.Summary.RawEventCount),
                completedPlateAppearances = games.Sum(game => game.Summary.CompletedPlateAppearanceCount),
                interruptedPlateAppearances = games.Sum(game => game.Summary.InterruptedPlateAppearanceCount),
                pitches = games.Sum(game => game.Summary.PitchEventCount),
                ptsMatched = games.Sum(game => game.Summary.PtsMatchedPitchCount),
                ptsMissing = games.Sum(game => game.Summary.PtsMissingPitchCount),
                runnerEvents = games.Sum(game => game.Summary.RunnerEventCount),
                playerChanges = games.Sum(game => game.Summary.PlayerChangeEventCount),
                administrativeEvents = games.Sum(game => game.Summary.AdministrativeEventCount),
                warnings = games.Sum(game => game.Summary.WarningCount),
                errors = games.Sum(game => game.Summary.ErrorCount),
            },
            games = games.Select(game => new
            {
                game.GameId,
                game.GameDate,
                away = game.AwayTeam.TeamCode,
                home = game.HomeTeam.TeamCode,
                game.Summary,
            }),
            failures = failures.Select(failure => new { source = failure.Source, error = failure.Error }),
        };

        var path = Path.Combine(outputDirectory, "aggregate-summary.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(aggregate, SerializerOptions),
            cancellationToken);
        return path;
    }


    public static async Task<string> SaveAggregateSummaryAsync(
        IReadOnlyCollection<NaverRelay.Gui.Models.LightweightParsedGameSummary> games,
        IReadOnlyCollection<(string Source, string Error)> failures,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var aggregate = new
        {
            generatedAtLocal = DateTimeOffset.Now,
            gameCount = games.Count,
            failedCount = failures.Count,
            totals = new
            {
                rawRelayGroups = games.Sum(game => game.Summary.RawRelayGroupCount),
                rawEvents = games.Sum(game => game.Summary.RawEventCount),
                completedPlateAppearances = games.Sum(game => game.Summary.CompletedPlateAppearanceCount),
                interruptedPlateAppearances = games.Sum(game => game.Summary.InterruptedPlateAppearanceCount),
                pitches = games.Sum(game => game.Summary.PitchEventCount),
                ptsMatched = games.Sum(game => game.Summary.PtsMatchedPitchCount),
                ptsMissing = games.Sum(game => game.Summary.PtsMissingPitchCount),
                runnerEvents = games.Sum(game => game.Summary.RunnerEventCount),
                playerChanges = games.Sum(game => game.Summary.PlayerChangeEventCount),
                administrativeEvents = games.Sum(game => game.Summary.AdministrativeEventCount),
                warnings = games.Sum(game => game.Summary.WarningCount),
                errors = games.Sum(game => game.Summary.ErrorCount),
            },
            games = games.Select(game => new
            {
                game.GameId,
                game.GameDate,
                away = game.AwayTeamCode,
                home = game.HomeTeamCode,
                game.Summary,
            }),
            failures = failures.Select(failure => new { source = failure.Source, error = failure.Error }),
        };

        var path = Path.Combine(outputDirectory, "aggregate-summary.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(aggregate, SerializerOptions), cancellationToken);
        return path;
    }

    public static async Task SaveAllAsync(
        IReadOnlyList<NormalizedGame> games,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        for (var index = 0; index < games.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var game = games[index];
            await SaveGameAsync(game, outputDirectory, $"game-{index + 1}.json", cancellationToken);
        }

        await SaveAggregateSummaryAsync(
            games,
            Array.Empty<(string Source, string Error)>(),
            outputDirectory,
            cancellationToken);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string GetAvailablePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem}_{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("출력 파일 이름을 만들 수 없습니다.");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "normalized-game" : sanitized;
    }
}
