using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

if (string.Equals(args[0], "--import", StringComparison.OrdinalIgnoreCase))
    return await RunImportAsync(args);

if (args[0] is "--reconcile" or "--verify")
    return await AutomationCommands.RunAsync(args);

if (string.Equals(args[0], "--seal-web", StringComparison.OrdinalIgnoreCase))
    return await WebSealCommand.RunAsync(args);

var inputPath = args[0];
var outputPath = args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal)
    ? args[1]
    : Path.Combine(Environment.CurrentDirectory, "normalized_output");
var compact = args.Contains("--compact", StringComparer.OrdinalIgnoreCase);
var validateKnownSample = args.Contains("--validate-known-sample", StringComparer.OrdinalIgnoreCase);

if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Input path does not exist: {inputPath}");
    return 2;
}

Directory.CreateDirectory(outputPath);
var serializerOptions = new JsonSerializerOptions
{
    WriteIndented = !compact,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};
serializerOptions.Converters.Add(new JsonStringEnumConverter());

var sources = LoadSources(inputPath).ToList();
if (sources.Count == 0)
{
    Console.Error.WriteLine("No JSON relay files were found.");
    return 3;
}

var parsedGames = new List<NormalizedGame>();
var failures = new List<object>();

foreach (var source in sources)
{
    try
    {
        var game = RelayParser.ParseJson(source.Json);
        if (game.ImportedOfficialSource is { } official)
        {
            KboPlayLog.Apply(game, official);
            if (official.BoxScore is { } box) KboBoxScore.Apply(game, box, official.GameId);
        }
        parsedGames.Add(game);

        var safeGameId = string.IsNullOrWhiteSpace(game.GameId)
            ? Path.GetFileNameWithoutExtension(source.Name)
            : game.GameId;
        var outputFile = Path.Combine(outputPath, safeGameId + ".normalized.json");
        await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(game, serializerOptions));

        var s = game.Summary;
        Console.WriteLine(
            $"{safeGameId}: PA {s.CompletedPlateAppearanceCount} + interrupted {s.InterruptedPlateAppearanceCount}, " +
            $"pitches {s.PitchEventCount}, PTS missing {s.PtsMissingPitchCount}, warnings {s.WarningCount}");
    }
    catch (Exception ex)
    {
        failures.Add(new { source = source.Name, error = ex.Message });
        Console.Error.WriteLine($"FAILED {source.Name}: {ex.Message}");
    }
}

var aggregate = new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    input = Path.GetFullPath(inputPath),
    gameCount = parsedGames.Count,
    failedCount = failures.Count,
    totals = new
    {
        rawRelayGroups = parsedGames.Sum(g => g.Summary.RawRelayGroupCount),
        rawEvents = parsedGames.Sum(g => g.Summary.RawEventCount),
        completedPlateAppearances = parsedGames.Sum(g => g.Summary.CompletedPlateAppearanceCount),
        interruptedPlateAppearances = parsedGames.Sum(g => g.Summary.InterruptedPlateAppearanceCount),
        prePlateSubstitutionGroups = parsedGames.Sum(g => g.Summary.PrePlateSubstitutionGroupCount),
        inningMarkerGroups = parsedGames.Sum(g => g.Summary.InningMarkerGroupCount),
        gameSummaryGroups = parsedGames.Sum(g => g.Summary.GameSummaryGroupCount),
        pitches = parsedGames.Sum(g => g.Summary.PitchEventCount),
        ptsMatched = parsedGames.Sum(g => g.Summary.PtsMatchedPitchCount),
        ptsMissing = parsedGames.Sum(g => g.Summary.PtsMissingPitchCount),
        runnerEvents = parsedGames.Sum(g => g.Summary.RunnerEventCount),
        playerChanges = parsedGames.Sum(g => g.Summary.PlayerChangeEventCount),
        administrativeEvents = parsedGames.Sum(g => g.Summary.AdministrativeEventCount),
        duplicateSourceSeqNoOccurrences = parsedGames.Sum(g => g.Summary.DuplicateSourceSeqNoOccurrenceCount),
        unknownRelayGroups = parsedGames.Sum(g => g.Summary.UnknownRelayGroupCount),
        unknownRawEventTypes = parsedGames.Sum(g => g.Summary.UnknownRawEventTypeCount),
        unknownBattingResults = parsedGames.Sum(g => g.Summary.UnknownBattingResultCount),
        unparsedRunnerEvents = parsedGames.Sum(g => g.Summary.UnparsedRunnerEventCount),
        unparsedPlayerChanges = parsedGames.Sum(g => g.Summary.UnparsedPlayerChangeCount),
        unknownAdministrativeEvents = parsedGames.Sum(g => g.Summary.UnknownAdministrativeEventCount),
        ptsCalculationFailures = parsedGames.Sum(g => g.Summary.PtsCalculationFailureCount),
        finalLineBattingMismatches = parsedGames.Sum(g => g.Summary.FinalLineBattingMismatchCount),
        warnings = parsedGames.Sum(g => g.Summary.WarningCount),
        errors = parsedGames.Sum(g => g.Summary.ErrorCount),
    },
    games = parsedGames.Select(g => new { g.GameId, g.Summary }),
    failures,
};

await File.WriteAllTextAsync(
    Path.Combine(outputPath, "aggregate-summary.json"),
    JsonSerializer.Serialize(aggregate, serializerOptions));

Console.WriteLine($"Normalized output: {Path.GetFullPath(outputPath)}");

if (validateKnownSample)
{
    var validationFailures = KnownSampleValidator.Validate(parsedGames);
    if (validationFailures.Count == 0)
    {
        Console.WriteLine("Known 7-game sample validation: PASS");
    }
    else
    {
        Console.Error.WriteLine("Known 7-game sample validation: FAIL");
        foreach (var failure in validationFailures)
        {
            Console.Error.WriteLine($"  - {failure}");
        }

        return 4;
    }
}

return failures.Count == 0 ? 0 : 1;

static IEnumerable<(string Name, string Json)> LoadSources(string inputPath)
{
    if (Directory.Exists(inputPath))
    {
        foreach (var file in Directory.EnumerateFiles(inputPath, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return (file, File.ReadAllText(file));
        }

        yield break;
    }

    if (string.Equals(Path.GetExtension(inputPath), ".zip", StringComparison.OrdinalIgnoreCase))
    {
        using var archive = ZipFile.OpenRead(inputPath);
        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(entry.Open());
            yield return (entry.FullName, reader.ReadToEnd());
        }

        yield break;
    }

    yield return (inputPath, File.ReadAllText(inputPath));
}

// Writes straight into the desktop SQLite DB (the same call the GUI's
// "SQLite DB 수집" mode makes via ParsingWorkflowService), instead of just
// writing normalized JSON to disk. Safe to rerun on a timer: files whose
// content hasn't changed since the last successful import are skipped via
// DatabaseCacheService's own fingerprint cache (ParsedSources table).
static async Task<int> RunImportAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: --import <input-dir> <desktop-db-path>");
        return 2;
    }

    var inputDir = args[1];
    var dbPath = args[2];
    if (!Directory.Exists(inputDir))
    {
        Console.Error.WriteLine($"Input directory does not exist: {inputDir}");
        return 2;
    }

    var documents = DiscoverJsonDocuments(inputDir);
    if (documents.Count == 0)
    {
        Console.WriteLine("No JSON files found.");
        return 0;
    }

    var db = new DatabaseCacheService(dbPath);
    await db.InitializeAsync();

    var unchanged = await db.GetUnchangedSourceKeysAsync(documents);
    var toProcess = documents.Where(d => !unchanged.Contains(d.Id)).ToList();
    Console.WriteLine($"{documents.Count} file(s) found, {toProcess.Count} to import " +
        $"({unchanged.Count} unchanged, skipped).");

    int imported = 0, deferred = 0, failed = 0;
    foreach (var document in toProcess)
    {
        try
        {
            var json = await document.ReadJsonAsync(CancellationToken.None);
            using (var snapshot = JsonDocument.Parse(json))
            {
                var root = snapshot.RootElement;
                if (root.TryGetProperty("collectionStatus", out var state) && state.GetString() == "partial")
                {
                    deferred++;
                    Console.WriteLine($"DEFERRED {document.DisplayName}: 진행 중/미완료 스냅샷. 다음 주기에 다시 확인합니다.");
                    continue;
                }
            }
            var game = RelayParser.ParseJson(json);
            await db.SaveGameAndSourceAsync(game, document);
            imported++;
            var s = game.Summary;
            Console.WriteLine($"OK {document.DisplayName}: PA {s.CompletedPlateAppearanceCount}, " +
                $"pitches {s.PitchEventCount}, warnings {s.WarningCount}");
        }
        catch (GameNotStartedException ex)
        {
            deferred++;
            Console.WriteLine($"DEFERRED {document.DisplayName}: {ex.Message}");
        }
        catch (Exception ex)
        {
            failed++;
            // Deliberately stdout, not stderr: this loop already reports the
            // outcome per file and keeps going - it is not a fatal process
            // error, and a scheduled caller must not treat it as one.
            Console.WriteLine($"FAILED {document.DisplayName}: {ex.Message}");
        }
    }

    Console.WriteLine($"imported={imported} deferred={deferred} failed={failed}");
    // Deferred snapshots are expected. Real parsing/storage failures block publication.
    return failed == 0 ? 0 : 1;
}

static List<InputDocument> DiscoverJsonDocuments(string inputDir)
{
    var documents = new List<InputDocument>();
    foreach (var file in Directory.EnumerateFiles(inputDir, "*.json", SearchOption.TopDirectoryOnly)
                 .Where(path =>
                 {
                     var name = Path.GetFileName(path);
                     return !name.EndsWith(".normalized.json", StringComparison.OrdinalIgnoreCase)
                         && !name.Equals("aggregate-summary.json", StringComparison.OrdinalIgnoreCase);
                 })
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
    {
        var info = new FileInfo(file);
        documents.Add(new InputDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = InputDocumentKind.JsonFile,
            ContainerPath = info.FullName,
            Length = info.Exists ? info.Length : 0,
        });
    }

    return documents;
}

static void PrintUsage()
{
    Console.WriteLine("  --reconcile <desktop-db-path> <report.json> : 공식 정정 / 타점 / 팀 자책점 대조");
    Console.WriteLine("  --verify <desktop-db-path> <report.json> [--allow-reconciliation-pending|--structural-only] : SQLite 무결성 / 외래키 / 기록 오류 검증");
    Console.WriteLine("  --seal-web <existing-db> : 웹 DB 캐시 준비 / 최적화 / WAL 봉인");
    Console.WriteLine("NaverRelay phase-1 normalized parser");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/NaverRelay.Cli -- <json-file|directory|zip> [output-dir] [--compact] [--validate-known-sample]");
    Console.WriteLine("  dotnet run --project src/NaverRelay.Cli -- --import <input-dir> <desktop-db-path>");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project src/NaverRelay.Cli -- SampleData/2026.zip normalized_output --validate-known-sample");
    Console.WriteLine("  dotnet run --project src/NaverRelay.Cli -- raw_games normalized_output");
    Console.WriteLine("  dotnet run --project src/NaverRelay.Cli -- --import C:\\...\\NaverKboCombined C:\\...\\sabermetrics_v2.db");
}
