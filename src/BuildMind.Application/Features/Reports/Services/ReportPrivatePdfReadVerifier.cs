using System.Security.Cryptography;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Services;

/// <summary>
/// Bounded, read-only verification of an already authorized private PDF artifact.
/// This helper does NOT authenticate a caller, access storage, prove persistence, or mark a report Ready.
/// An M4-owned adapter must authorize access and bound reads before opening a private stream.
/// </summary>
public static class ReportPrivatePdfReadVerifier
{
    public const int AbsoluteMaximumBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Reads exactly the declared number of bytes plus a one-byte EOF check, never more than the
    /// configured cap plus one byte. Invalid/short/extra/corrupt content returns null without
    /// releasing partial data. I/O and cancellation exceptions propagate; callers must not retry
    /// an uncertain storage operation automatically.
    /// </summary>
    public static async Task<byte[]?> ReadVerifiedAsync(
        Stream input, ReportArtifact metadata, int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(metadata);
        if (maximumBytes is < 1 or > AbsoluteMaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        cancellationToken.ThrowIfCancellationRequested();

        if (metadata.MediaType != "application/pdf" ||
            metadata.ByteLength < 1 || metadata.ByteLength > maximumBytes)
            return null;

        var expectedLength = checked((int)metadata.ByteLength);
        var bytes = new byte[expectedLength];
        var verified = false;
        try
        {
            var received = 0;
            while (received < expectedLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = await input.ReadAsync(bytes.AsMemory(received), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                    return null;
                received += count;
            }

            // Reject trailing bytes, including when the metadata hash covers only a prefix.
            var tail = new byte[1];
            var trailing = await input.ReadAsync(tail.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (trailing != 0 || !ReportArtifactIntegrity.Matches(metadata, bytes, maximumBytes))
                return null;

            verified = true;
            return bytes;
        }
        finally
        {
            // Best-effort zeroing of this temporary buffer on all unsuccessful exits.
            // This does not erase copies held by the private store or runtime.
            if (!verified)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
