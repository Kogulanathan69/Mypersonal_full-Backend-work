using BuildMind.Application.Features.Reports.Services;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Reports.Tests.Fixtures;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportReadCandidateTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 synthetic"u8.ToArray();
    private static Report Ready(string? hash = null) => SyntheticReportFixtures.Pending().Complete(1,
        new ReportArtifact(SyntheticReportFixtures.ArtifactId, "application/pdf", Pdf.Length,
            hash ?? ReportArtifactIntegrity.Hash(Pdf), SyntheticReportFixtures.Time.AddMinutes(3)),
        SyntheticReportFixtures.Time.AddMinutes(4));
    private sealed class Boundary(ReportReadReceipt receipt) : IReportReadBoundaryCandidate
    {
        public int Calls;
        public ReportReadReceipt Response { get; set; } = receipt;
        public bool Throw;
        public CancellationTokenSource? Cancel;
        public bool? RequestedDownload;
        public int? RequestedMaximumBytes;
        public CancellationToken ReceivedToken;
        public Task<ReportReadReceipt> ReadAsync(Guid actor, Guid projectId, Guid reportId, bool download,
            int maximumBytes, CancellationToken token)
        {
            Calls++;
            RequestedDownload = download;
            RequestedMaximumBytes = maximumBytes;
            ReceivedToken = token;
            if (Throw) throw new TimeoutException();
            Cancel?.Cancel();
            return Task.FromResult(Response);
        }
    }
    private static ReportReadReceipt Found(Report report, byte[]? bytes = null) => new(ReportReadStatus.Found, SyntheticReportFixtures.User, report, bytes);
    private static Task<ReportDownloadResult> Download(Boundary b, int limit = 1024) => new ReportReadCandidate(b,limit).DownloadAsync(SyntheticReportFixtures.User,SyntheticReportFixtures.Project,SyntheticReportFixtures.ReportId);
    [Fact] public async Task ExactReadyBytesAreCopiedAndNamedSafely()
    { var bytes=Pdf.ToArray(); var result=await Download(new(Found(Ready(),bytes))); Assert.Equal(ReportAccessOutcome.Available,result.Outcome); Assert.Equal(Pdf,result.Bytes); Assert.NotSame(bytes,result.Bytes); Assert.Equal("application/pdf",result.ContentType); Assert.Equal($"report-{SyntheticReportFixtures.ReportId:N}.pdf",result.FileName); }
    [Fact] public async Task MetadataOmitsSnapshotAndArtifact()
    { var p=Ready(); var result=await new ReportReadCandidate(new Boundary(Found(p))).GetStatusAsync(p.RequestedBy,p.ProjectId,p.Id); Assert.Equal(new ReportStatusView(p.ProjectId,p.Id,ReportStatus.Ready,2),result.Metadata); }
    [Fact] public async Task MissingOrDeniedIsHidden()
    { var result=await Download(new(new(ReportReadStatus.Hidden,SyntheticReportFixtures.User))); Assert.Equal(ReportAccessOutcome.Hidden,result.Outcome); Assert.Null(result.Bytes); }
    [Fact] public async Task EmptyActorNeverDispatches()
    { var b=new Boundary(Found(Ready(),Pdf)); var result=await new ReportReadCandidate(b).DownloadAsync(Guid.Empty,SyntheticReportFixtures.Project,SyntheticReportFixtures.ReportId); Assert.Equal(ReportAccessOutcome.Hidden,result.Outcome); Assert.Equal(0,b.Calls); }
    [Fact] public async Task WrongActorReceiptNeverReleasesBytes()
    { var result=await Download(new(Found(Ready(),Pdf) with { Actor=Guid.NewGuid() })); Assert.Equal(ReportAccessOutcome.Unavailable,result.Outcome); Assert.Null(result.Bytes); }
    [Fact] public async Task WrongReportNeverReleasesBytes()
    { var p=Ready(); var b=new Boundary(Found(p,Pdf)); var result=await new ReportReadCandidate(b).DownloadAsync(p.RequestedBy,p.ProjectId,Guid.NewGuid()); Assert.Equal(ReportAccessOutcome.Unavailable,result.Outcome); Assert.Null(result.Bytes); }
    [Fact] public async Task WrongProjectNeverReleasesBytes()
    { var p=Ready(); var result=await new ReportReadCandidate(new Boundary(Found(p,Pdf))).DownloadAsync(p.RequestedBy,Guid.NewGuid(),p.Id); Assert.Equal(ReportAccessOutcome.Unavailable,result.Outcome); Assert.Null(result.Bytes); }
    [Fact] public async Task PendingAndFailedAreNotDownloadable()
    { var p=SyntheticReportFixtures.Pending(); Assert.Equal(ReportAccessOutcome.NotReady,(await Download(new(Found(p)))).Outcome); Assert.Equal(ReportAccessOutcome.NotReady,(await Download(new(Found(p.Fail(1,"TEST_FAILED",SyntheticReportFixtures.Time.AddMinutes(3)))))).Outcome); }
    [Fact] public async Task MissingArtifactBytesUnavailable()
    { Assert.Equal(ReportAccessOutcome.Unavailable,(await Download(new(Found(Ready())))).Outcome); }
    [Fact] public async Task CorruptBytesUnavailable()
    { var bytes=Pdf.ToArray(); bytes[^1]^=1; var result=await Download(new(Found(Ready(),bytes))); Assert.Equal(ReportAccessOutcome.Unavailable,result.Outcome); Assert.Null(result.Bytes); }
    [Fact] public async Task CapBoundaryEnforced()
    { Assert.Equal(ReportAccessOutcome.Available,(await Download(new(Found(Ready(),Pdf)),Pdf.Length)).Outcome); Assert.Equal(ReportAccessOutcome.Unavailable,(await Download(new(Found(Ready(),Pdf)),Pdf.Length-1)).Outcome); }
    [Fact] public async Task NonFoundWithPayloadNeverLeaks()
    { var result=await Download(new(Found(Ready(),Pdf) with { Status=ReportReadStatus.Hidden })); Assert.Equal(ReportAccessOutcome.Unavailable,result.Outcome); Assert.Null(result.Bytes); }
    [Fact] public async Task TimeoutIsUnavailable()
    { Assert.Equal(ReportAccessOutcome.Unavailable,(await Download(new(Found(Ready(),Pdf)){Throw=true})).Outcome); }
    [Fact] public async Task CancelledReadNeverReleasesBytes()
    { using var c=new CancellationTokenSource(); var p=Ready(); var b=new Boundary(Found(p,Pdf)){Cancel=c}; await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ReportReadCandidate(b).DownloadAsync(p.RequestedBy,p.ProjectId,p.Id,c.Token)); }
    [Fact]
    public async Task StatusReadCancellingAfterBoundaryDispatchCannotExposeMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        var report = Ready();
        var boundary = new Boundary(Found(report)) { Cancel = cancellation };
        var candidate = new ReportReadCandidate(boundary);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            candidate.GetStatusAsync(report.RequestedBy, report.ProjectId, report.Id, cancellation.Token));

        Assert.Equal(1, boundary.Calls);
        Assert.False(boundary.RequestedDownload);
        Assert.Equal(cancellation.Token, boundary.ReceivedToken);
    }

    [Fact]
    public async Task ReportReadBoundaryReceivesExactDownloadFlagAndByteCap()
    {
        const int maximumBytes = 1024;
        var report = Ready();
        var boundary = new Boundary(Found(report));
        var candidate = new ReportReadCandidate(boundary, maximumBytes);

        var status = await candidate.GetStatusAsync(report.RequestedBy, report.ProjectId, report.Id);
        Assert.Equal(ReportAccessOutcome.Available, status.Outcome);
        Assert.Equal(false, boundary.RequestedDownload);
        Assert.Equal(maximumBytes, boundary.RequestedMaximumBytes);

        boundary.Response = Found(report, Pdf);
        var download = await candidate.DownloadAsync(report.RequestedBy, report.ProjectId, report.Id);
        Assert.Equal(ReportAccessOutcome.Available, download.Outcome);
        Assert.Equal(true, boundary.RequestedDownload);
        Assert.Equal(maximumBytes, boundary.RequestedMaximumBytes);
        Assert.Equal(2, boundary.Calls);
    }

    [Fact] public async Task UnexpectedMetadataBytesFailClosed()
    { var p=Ready(); var result=await new ReportReadCandidate(new Boundary(Found(p,Pdf))).GetStatusAsync(p.RequestedBy,p.ProjectId,p.Id); Assert.Equal(ReportAccessOutcome.Unavailable,result.Outcome); Assert.Null(result.Metadata); }
}
