using BuildMind.Application.Features.Reports.Services;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportPrivatePdfReadVerifierTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 private synthetic artifact"u8.ToArray();

    private static ReportArtifact Metadata(byte[] bytes) => new(
        Guid.NewGuid(), "application/pdf", bytes.Length,
        ReportArtifactIntegrity.Hash(bytes), SyntheticReportFixtures.Time);

    [Fact]
    public async Task ExactBytesReturnIndependentVerifiedCopy()
    {
        var original = Pdf.ToArray();
        using var stream = new MemoryStream(original);
        var result = await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
            stream, Metadata(original), original.Length);

        Assert.Equal(original, result);
        Assert.NotSame(original, result);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task TruncatedStreamNeverReleasesPartialBytes()
    {
        var metadata = Metadata(Pdf);
        using var stream = new MemoryStream(Pdf[..^1]);
        Assert.Null(await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(stream, metadata, Pdf.Length));
    }

    [Fact]
    public async Task ExtraBytesAfterDeclaredLengthAreRejected()
    {
        var metadata = Metadata(Pdf);
        var appended = Pdf.Concat(new byte[] { 0x20 }).ToArray();
        using var stream = new MemoryStream(appended);

        Assert.Null(await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(stream, metadata, appended.Length));
        Assert.Equal(Pdf.Length + 1, stream.Position);
    }

    [Fact]
    public async Task SameLengthCorruptedContentCannotPass()
    {
        var corrupted = Pdf.ToArray();
        corrupted[^1] ^= 1;
        using var stream = new MemoryStream(corrupted);

        Assert.Null(await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
            stream, Metadata(Pdf), corrupted.Length));
    }

    [Fact]
    public async Task MatchingHashWithoutPdfHeaderIsRejected()
    {
        var bytes = "not-a-pdf-artifact"u8.ToArray();
        using var stream = new MemoryStream(bytes);

        Assert.Null(await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
            stream, Metadata(bytes), bytes.Length));
    }

    [Fact]
    public async Task MetadataExceedingCapIsRejectedBeforeAnyRead()
    {
        using var stream = new MemoryStream(Pdf);
        var result = await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
            stream, Metadata(Pdf), Pdf.Length - 1);

        Assert.Null(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task CancelledRequestDoesNotRead()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var stream = new MemoryStream(Pdf);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
                stream, Metadata(Pdf), Pdf.Length, source.Token));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task ReadFailurePropagatesWithoutClaimingSafeEmptyArtifact()
    {
        using var stream = new FailingStream(Pdf);
        await Assert.ThrowsAsync<IOException>(() =>
            ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
                stream, Metadata(Pdf), Pdf.Length));
        Assert.Equal(1, stream.Attempts);
    }

    [Fact]
    public async Task InvalidConfiguredCapIsRejected()
    {
        using var stream = new MemoryStream(Pdf);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ReportPrivatePdfReadVerifier.ReadVerifiedAsync(stream, Metadata(Pdf), 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
                stream, Metadata(Pdf), ReportPrivatePdfReadVerifier.AbsoluteMaximumBytes + 1));
    }

    [Fact]
    public async Task ChunkedPrivateStreamReturnsExactVerifiedBytes()
    {
        using var stream = new ChunkedStream(Pdf);
        var result = await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
            stream, Metadata(Pdf), Pdf.Length);

        Assert.Equal(Pdf, result);
        Assert.True(stream.Reads > 2);
        Assert.True(stream.LargestRequestedBytes <= Pdf.Length);
    }

    [Fact]
    public async Task CancellationAfterFirstChunkCannotReturnPartialArtifact()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ChunkedStream(Pdf, cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
                stream, Metadata(Pdf), Pdf.Length, cancellation.Token));

        Assert.Equal(1, stream.Reads);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("trailing")]
    [InlineData("corrupt")]
    public async Task UnsuccessfulPrivateReadZeroesTemporaryBuffer(string failure)
    {
        var inputBytes = failure switch
        {
            "short" => Pdf[..^1],
            "trailing" => Pdf.Concat(new byte[] { 0x42 }).ToArray(),
            _ => Pdf.Select((value, index) => index == Pdf.Length - 1
                ? (byte)(value ^ 1) : value).ToArray()
        };
        using var stream = new CapturingStream(inputBytes);

        var result = await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
            stream, Metadata(Pdf), Pdf.Length);

        Assert.Null(result);
        Assert.True(stream.Captured.Length > 0);
        Assert.All(stream.Captured.ToArray(), value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task MidReadCancellationZeroesTemporaryBuffer()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CapturingStream(Pdf, cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReportPrivatePdfReadVerifier.ReadVerifiedAsync(
                stream, Metadata(Pdf), Pdf.Length, cancellation.Token));

        Assert.True(stream.Captured.Length > 0);
        Assert.All(stream.Captured.ToArray(), value => Assert.Equal((byte)0, value));
    }

    private sealed class CapturingStream(byte[] bytes, Action? afterFirstRead = null) : MemoryStream(bytes)
    {
        public Memory<byte> Captured { get; private set; }
        private int reads;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (reads == 0)
                Captured = buffer;
            var count = await base.ReadAsync(
                buffer[..Math.Min(buffer.Length, 3)], cancellationToken).ConfigureAwait(false);
            if (++reads == 1)
                afterFirstRead?.Invoke();
            return count;
        }
    }

    private sealed class ChunkedStream(byte[] bytes, Action? afterFirstRead = null) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }
        public int LargestRequestedBytes { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestRequestedBytes = Math.Max(LargestRequestedBytes, buffer.Length);
            var count = await base.ReadAsync(
                buffer[..Math.Min(3, buffer.Length)], cancellationToken).ConfigureAwait(false);
            Reads++;
            if (Reads == 1)
                afterFirstRead?.Invoke();
            return count;
        }
    }

    private sealed class FailingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Attempts { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return ValueTask.FromException<int>(new IOException("synthetic private store read error"));
        }
    }
}
