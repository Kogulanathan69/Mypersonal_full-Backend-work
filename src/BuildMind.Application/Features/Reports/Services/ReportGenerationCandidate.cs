using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Services;

// PROVISIONAL isolated contract: requires M1/M3/M4 agreement before any production binding.
// Actor comes from a trusted authentication boundary, never a customer request body.
public sealed record ReportGenerationRequest(Guid ProjectId, Guid ReportId, Guid PlanId,
    Guid PlanRevisionId, Guid ConfirmationId, string GeometryHash);
public enum ReportAcceptanceStatus { Accepted, Rejected, Existing, Unavailable, Unknown }
public sealed record ReportAcceptance(ReportAcceptanceStatus Status, Report? Pending = null);
public enum ReportPublicationStatus { Published, Rejected, Unknown }
public sealed record ReportPublication(ReportPublicationStatus Status, ReportArtifact? Artifact = null);
public enum ReportGenerationOutcome { Published, Rejected, Existing, Unavailable, Failed, OutcomeUnknown }

// This is a candidate composite capability, NOT an adapter over sequential repository reads.
// Accept must atomically authenticate/authorize actor, resolve exact persisted eligible confirmation,
// bind immutable sources, and reserve a single execution. Existing must never grant execution.
// Publish must reauthorize actor, verify same pending version, privately persist immutable bytes,
// verify bounded read-back, and CAS to Ready. Storage outside DB transaction needs durable recovery.
// A rejected publication must guarantee no Ready transition; Unknown includes cancellation after dispatch.
// Fail must reauthorize and CAS the exact pending record to Failed, with no artifact publication.
// No automatic retries. Durable deduplication, lock ordering, actor-version and recovery semantics
// remain integration gates; no concrete implementation or DI registration is supplied.
public interface IReportGenerationBoundaryCandidate
{
    Task<ReportAcceptance> AcceptAsync(Guid actor, ReportGenerationRequest request, CancellationToken token);
    Task<ReportPublication> PublishAsync(Guid actor, Report pending, ReadOnlyMemory<byte> bytes,
        string expectedHash, CancellationToken token);
    Task<bool> FailAsync(Guid actor, Report pending, string safeCode, CancellationToken token);
}

public sealed class ReportGenerationCandidate
{
    private readonly IReportGenerationBoundaryCandidate boundary;
    private readonly IPreliminaryReportRenderer renderer;
    private readonly int maximumBytes;
    public ReportGenerationCandidate(IReportGenerationBoundaryCandidate boundary,
        IPreliminaryReportRenderer renderer, int maximumBytes = 10 * 1024 * 1024)
    {
        this.boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        if (maximumBytes is < 1 or > 10 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.maximumBytes = maximumBytes;
    }

    public async Task<ReportGenerationOutcome> GenerateAsync(Guid actor, ReportGenerationRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        if (actor == Guid.Empty || request.ProjectId == Guid.Empty || request.ReportId == Guid.Empty ||
            request.PlanId == Guid.Empty || request.PlanRevisionId == Guid.Empty || request.ConfirmationId == Guid.Empty ||
            request.GeometryHash is not { Length: 71 } || !request.GeometryHash.StartsWith("sha256:", StringComparison.Ordinal) ||
            request.GeometryHash.AsSpan(7).ContainsAnyExcept("0123456789abcdef"))
            return ReportGenerationOutcome.Rejected;
        ReportAcceptance acceptance;
        try { acceptance = await boundary.AcceptAsync(actor, request, token); }
        catch (Exception e) when (e is not OutOfMemoryException) { return ReportGenerationOutcome.OutcomeUnknown; }
        if (token.IsCancellationRequested || acceptance is null) return ReportGenerationOutcome.OutcomeUnknown;
        if (acceptance.Status != ReportAcceptanceStatus.Accepted)
        {
            if (acceptance.Pending is not null) return ReportGenerationOutcome.OutcomeUnknown;
            return acceptance.Status switch {
                ReportAcceptanceStatus.Rejected => ReportGenerationOutcome.Rejected,
                ReportAcceptanceStatus.Existing => ReportGenerationOutcome.Existing,
                ReportAcceptanceStatus.Unavailable => ReportGenerationOutcome.Unavailable,
                _ => ReportGenerationOutcome.OutcomeUnknown };
        }
        var pending = acceptance.Pending;
        if (pending is null || pending.Status != ReportStatus.Pending || pending.StateVersion != 1 ||
            pending.Id != request.ReportId || pending.ProjectId != request.ProjectId || pending.RequestedBy != actor ||
            pending.InputSnapshot.Plan.PlanId != request.PlanId || pending.InputSnapshot.Plan.RevisionId != request.PlanRevisionId ||
            pending.InputSnapshot.Confirmation.ConfirmationId != request.ConfirmationId ||
            !string.Equals(pending.InputSnapshot.Plan.GeometryHash, request.GeometryHash, StringComparison.Ordinal))
            return ReportGenerationOutcome.OutcomeUnknown;
        byte[] bytes;
        try
        {
            if (pending.InputSnapshot.Plan.GeometryJson.Length > 1024 * 1024 ||
                System.Text.Encoding.UTF8.GetByteCount(pending.InputSnapshot.Plan.GeometryJson) > 1024 * 1024)
                throw new ArgumentException("Report geometry limit.");
            token.ThrowIfCancellationRequested();
            bytes = await renderer.RenderAsync(ReportDataBuilder.Build(pending.InputSnapshot), token);
            token.ThrowIfCancellationRequested();
            if (bytes is null || bytes.Length > maximumBytes || !bytes.AsSpan().StartsWith("%PDF-"u8))
                throw new InvalidOperationException("Invalid render output.");
            bytes = bytes.ToArray();
        }
        catch (OperationCanceledException) { return ReportGenerationOutcome.OutcomeUnknown; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (token.IsCancellationRequested) return ReportGenerationOutcome.OutcomeUnknown;
            try {
                var failed = await boundary.FailAsync(actor, pending, "REPORT_RENDER_FAILED", token);
                return failed && !token.IsCancellationRequested ? ReportGenerationOutcome.Failed : ReportGenerationOutcome.OutcomeUnknown;
            } catch (Exception ex) when (ex is not OutOfMemoryException) { return ReportGenerationOutcome.OutcomeUnknown; }
        }
        try
        {
            token.ThrowIfCancellationRequested();
            var hash = ReportArtifactIntegrity.Hash(bytes);
            var publication = await boundary.PublishAsync(actor, pending, bytes, hash, token);
            if (token.IsCancellationRequested || publication is null) return ReportGenerationOutcome.OutcomeUnknown;
            if (publication.Status == ReportPublicationStatus.Published && publication.Artifact is not null &&
                string.Equals(publication.Artifact.ContentHash, hash, StringComparison.Ordinal) &&
                ReportArtifactIntegrity.Matches(publication.Artifact, bytes, maximumBytes))
                return ReportGenerationOutcome.Published;
            // Pending state or private artifact may still exist. Do not retry, fail, or delete here.
            return ReportGenerationOutcome.OutcomeUnknown;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ReportGenerationOutcome.OutcomeUnknown; }
    }
}
