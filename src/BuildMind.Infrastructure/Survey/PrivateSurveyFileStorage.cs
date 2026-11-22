using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Application.Features.Survey.Services;

namespace BuildMind.Infrastructure.Survey;

/// <summary>Isolated file adapter. The caller provisions an exclusive private directory before use.</summary>
internal sealed class PrivateSurveyFileStorage : ISurveyStorage
{
    private readonly string directory;

    public PrivateSurveyFileStorage(string privateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateDirectory);
        var root = new DirectoryInfo(privateDirectory);
        if (!Path.IsPathFullyQualified(privateDirectory) || !root.Exists ||
            (root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("A provisioned private survey directory is required.", nameof(privateDirectory));

        directory = root.FullName;
    }

    public async Task<Guid> StoreAsync(Stream content, string mediaType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        var stage = Path.Combine(directory, id.ToString("N") + ".partial");
        var ownsStage = false;
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                BufferSize = 81920
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (var output = new FileStream(stage, options))
            {
                ownsStage = true;
                var buffer = new byte[81920];
                var written = 0L;
                int length;
                while ((length = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += length;
                    if (written > SurveyUploadValidator.MaxFileSizeBytes)
                        throw new InvalidDataException("Survey size exceeds the upload limit.");
                    await output.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                }

                if (written == 0)
                    throw new InvalidDataException("Survey content is empty.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stage, BlobPath(id));
            ownsStage = false;
            return id;
        }
        finally
        {
            if (ownsStage)
                TryDeleteStage(stage);
        }
    }

    public Task DeleteAsync(Guid storageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (storageId == Guid.Empty)
            throw new ArgumentException("A survey storage identifier is required.", nameof(storageId));

        File.Delete(BlobPath(storageId));
        return Task.CompletedTask;
    }

    private string BlobPath(Guid id) => Path.Combine(directory, id.ToString("N") + ".blob");

    private static void TryDeleteStage(string stage)
    {
        try { File.Delete(stage); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
