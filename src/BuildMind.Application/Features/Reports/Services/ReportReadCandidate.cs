using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Reports.Services;

// PROVISIONAL: no production registration or storage adapter. Actor comes from trusted authentication.
public enum ReportReadStatus { Found, Hidden, Unavailable }
public sealed record ReportReadReceipt(ReportReadStatus Status, Guid Actor, Report? Report = null, byte[]? Bytes = null);
public interface IReportReadBoundaryCandidate
{
    // Authorize current project/report access and select the exact persisted report at a consistent boundary.
    // For download, read immutable private bytes with the specified cap BEFORE allocation and recheck
    // authorization and report/artifact identity before returning. Define revocation linearization in M1/M4.
    // Hidden is uniform for missing and unauthorized resources. Never return payload on non-Found.
    Task<ReportReadReceipt> ReadAsync(Guid actor, Guid projectId, Guid reportId, bool download,
        int maximumBytes, CancellationToken token);
}
public enum ReportAccessOutcome { Available, Hidden, NotReady, Unavailable }
public sealed record ReportStatusView(Guid ProjectId, Guid ReportId, ReportStatus Status, long StateVersion);
public sealed record ReportStatusResult(ReportAccessOutcome Outcome, ReportStatusView? Metadata = null);
public sealed record ReportDownloadResult(ReportAccessOutcome Outcome, byte[]? Bytes = null,
    string? FileName = null, string? ContentType = null);

public sealed class ReportReadCandidate
{
    private readonly IReportReadBoundaryCandidate boundary;
    private readonly int maximumBytes;
    public ReportReadCandidate(IReportReadBoundaryCandidate boundary, int maximumBytes = 10 * 1024 * 1024)
    {
        this.boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        if (maximumBytes is < 1 or > 10 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.maximumBytes = maximumBytes;
    }
    private async Task<(ReportAccessOutcome Outcome, ReportReadReceipt? Receipt)> ReadAsync(
        Guid actor, Guid projectId, Guid reportId, bool download, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (actor == Guid.Empty || projectId == Guid.Empty || reportId == Guid.Empty)
            return (ReportAccessOutcome.Hidden, null);
        ReportReadReceipt receipt;
        try { receipt = await boundary.ReadAsync(actor, projectId, reportId, download, maximumBytes, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException) { return (ReportAccessOutcome.Unavailable, null); }
        token.ThrowIfCancellationRequested();
        if (receipt is null || receipt.Actor != actor) return (ReportAccessOutcome.Unavailable, null);
        if (receipt.Status != ReportReadStatus.Found)
            return (receipt.Status == ReportReadStatus.Hidden && receipt.Report is null && receipt.Bytes is null
                ? ReportAccessOutcome.Hidden : ReportAccessOutcome.Unavailable, null);
        var report = receipt.Report;
        if (report is null || report.ProjectId != projectId || report.Id != reportId ||
            report.InputSnapshot.ProjectId != projectId || (!download && receipt.Bytes is not null))
            return (ReportAccessOutcome.Unavailable, null);
        return (ReportAccessOutcome.Available, receipt);
    }
    public async Task<ReportStatusResult> GetStatusAsync(Guid actor, Guid projectId, Guid reportId,
        CancellationToken token = default)
    {
        var (outcome, receipt) = await ReadAsync(actor, projectId, reportId, false, token);
        if (receipt?.Report is not { } report) return new(outcome);
        return new(outcome, new(report.ProjectId, report.Id, report.Status, report.StateVersion));
    }
    public async Task<ReportDownloadResult> DownloadAsync(Guid actor, Guid projectId, Guid reportId,
        CancellationToken token = default)
    {
        var (outcome, receipt) = await ReadAsync(actor, projectId, reportId, true, token);
        if (receipt?.Report is not { } report) return new(outcome);
        if (report.Status != ReportStatus.Ready)
            return new(receipt.Bytes is null ? ReportAccessOutcome.NotReady : ReportAccessOutcome.Unavailable);
        if (report.Artifact is not { } artifact || receipt.Bytes is not { } bytes || bytes.Length > maximumBytes)
            return new(ReportAccessOutcome.Unavailable);
        var copy = bytes.ToArray();
        if (!ReportArtifactIntegrity.Matches(artifact, copy, maximumBytes)) return new(ReportAccessOutcome.Unavailable);
        token.ThrowIfCancellationRequested();
        return new(ReportAccessOutcome.Available, copy, $"report-{reportId:N}.pdf", "application/pdf");
    }
}
