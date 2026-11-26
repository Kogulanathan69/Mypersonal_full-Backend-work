using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Editing;

public interface ICandidatePreviewStore
{
    Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken cancellationToken);
    Task<CandidatePreviewSnapshot?> GetAsync(Guid candidateId, CancellationToken cancellationToken);
}

/// <summary>
/// Provisional M6 boundary needed because the current compute response does not carry validation evidence.
/// Implementations must return evidence bound to the exact server-computed geometry hash.
/// </summary>
public interface ICandidateValidationEvidenceSource
{
    Task<PlanValidationReferenceDto?> GetAsync(
        Guid projectId, Guid planId, string exactGeometryHash, CancellationToken cancellationToken);
}

/// <summary>
/// M4 must implement candidate consumption and immutable revision append in one database transaction.
/// Implementations must recheck pending state, expiry, ownership, project/plan, and current base revision.
/// </summary>
public interface ICandidatePreviewTransaction
{
    Task<CandidateActionResult> TryApplyAtomicallyAsync(
        CandidatePreviewSnapshot expectedCandidate,
        PlanRevisionSnapshot proposedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CandidateActionResult> TryRejectAtomicallyAsync(
        CandidatePreviewSnapshot expectedCandidate,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CandidateActionResult> TryExpireAtomicallyAsync(
        CandidatePreviewSnapshot expectedCandidate,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
