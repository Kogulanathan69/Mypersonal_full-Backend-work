using System.Text;

namespace BuildMind.Application.Features.Survey.Services;

public sealed class SurveyUploadValidator
{
    public const long MaxFileSizeBytes = 6 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMediaTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png"
    ];

    public async Task<string?> ValidateAsync(
        string? fileName,
        string? mediaType,
        long byteLength,
        Stream content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            fileName.IndexOfAny(['/', '\\']) >= 0 ||
            fileName.Any(char.IsControl) ||
            fileName is "." or "..")
            return "INVALID_FILE_NAME";

        if (byteLength <= 0 || byteLength > MaxFileSizeBytes)
            return "INVALID_FILE_SIZE";

        string normalizedMediaType = mediaType?.Trim().ToLowerInvariant() ?? "";

        if (!AllowedMediaTypes.Contains(normalizedMediaType))
            return "UNSUPPORTED_MEDIA_TYPE";

        if (content is null || !content.CanRead || !content.CanSeek)
            return "INVALID_FILE_CONTENT";

        long originalPosition = content.Position;

        try
        {
            byte[] header = new byte[8];
            int totalRead = 0;

            while (totalRead < header.Length)
            {
                int read = await content.ReadAsync(
                    header.AsMemory(totalRead, header.Length - totalRead),
                    cancellationToken);

                if (read == 0)
                    break;

                totalRead += read;
            }

            return normalizedMediaType switch
            {
                "application/pdf" when totalRead >= 5 &&
                    Encoding.ASCII.GetString(header, 0, 5) == "%PDF-"
                    => null,

                "image/jpeg" when totalRead >= 3 &&
                    header[0] == 0xFF &&
                    header[1] == 0xD8 &&
                    header[2] == 0xFF
                    => null,

                "image/png" when totalRead >= 8 &&
                    header.AsSpan(0, 8).SequenceEqual(
                        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
                    => null,

                _ => "FILE_SIGNATURE_MISMATCH"
            };
        }
        finally
        {
            content.Position = originalPosition;
        }
    }
}
