using System.IO.Compression;
using NaverRelay.Application.Importing;

namespace NaverRelay.Gui.Services;

internal static class InputDiscoveryService
{
    public static Task<IReadOnlyList<InputDocument>> DiscoverAsync(
        string inputPath,
        Action<string>? warning,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<InputDocument>>(
            () => Discover(inputPath, warning, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<InputDocument> Discover(
        string inputPath,
        Action<string>? warning,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("입력 경로가 비어 있습니다.", nameof(inputPath));
        }

        var fullPath = Path.GetFullPath(inputPath);
        var documents = new List<InputDocument>();

        if (File.Exists(fullPath))
        {
            AddFile(fullPath, documents, warning, cancellationToken);
        }
        else if (Directory.Exists(fullPath))
        {
            var candidates = Directory
                .EnumerateFiles(fullPath, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedInputFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFile(candidate, documents, warning, cancellationToken);
            }
        }
        else
        {
            throw new FileNotFoundException("입력 파일 또는 폴더를 찾을 수 없습니다.", fullPath);
        }

        return documents
            .OrderBy(document => document.SourceDisplay, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddFile(
        string filePath,
        ICollection<InputDocument> documents,
        Action<string>? warning,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith(".normalized.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("aggregate-summary.json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var info = new FileInfo(filePath);
            documents.Add(new InputDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = InputDocumentKind.JsonFile,
                ContainerPath = info.FullName,
                Length = info.Exists ? info.Length : 0,
            });
            return;
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            foreach (var entry in archive.Entries
                         .Where(entry => entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                documents.Add(new InputDocument
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = InputDocumentKind.ZipEntry,
                    ContainerPath = Path.GetFullPath(filePath),
                    EntryName = entry.FullName,
                    Length = entry.Length,
                });
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            warning?.Invoke($"ZIP을 읽지 못해 건너뜁니다: {filePath} ({ex.Message})");
        }
    }

    private static bool IsSupportedInputFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }
}
