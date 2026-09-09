using System.IO.Compression;
using System.Text;

namespace NaverRelay.Application.Importing;

public enum InputDocumentKind
{
    JsonFile,
    ZipEntry,
}

public sealed class InputDocument
{
    public required string Id { get; init; }
    public required InputDocumentKind Kind { get; init; }
    public required string ContainerPath { get; init; }
    public string? EntryName { get; init; }
    public long Length { get; init; }

    public string DisplayName => Kind == InputDocumentKind.JsonFile
        ? Path.GetFileName(ContainerPath)
        : Path.GetFileName(EntryName ?? ContainerPath);

    public string SourceDisplay => Kind == InputDocumentKind.JsonFile
        ? ContainerPath
        : $"{ContainerPath}  >  {EntryName}";

    public async Task<string> ReadJsonAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Kind == InputDocumentKind.JsonFile)
        {
            await using var stream = new FileStream(
                ContainerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        using var archive = ZipFile.OpenRead(ContainerPath);
        var entry = archive.GetEntry(EntryName ?? string.Empty)
            ?? throw new InvalidDataException($"ZIP 엔트리를 찾을 수 없습니다: {EntryName}");
        await using var entryStream = entry.Open();
        using var entryReader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await entryReader.ReadToEndAsync(cancellationToken);
    }

    public override string ToString() => DisplayName;
}
