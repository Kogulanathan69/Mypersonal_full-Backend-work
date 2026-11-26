using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Requirements.Integration;

/// <summary>Exact immutable inputs for M6; no readiness, ownership or land-confirmation policy is implemented here.</summary>
public sealed class RequirementReadinessSnapshot
{
    public Guid ProjectId => Requirements.ProjectId;
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId => Requirements.Id;
    public RequirementRevision Requirements { get; }
    public DateTimeOffset CapturedAtUtc { get; }

    public RequirementReadinessSnapshot(Guid projectId, Guid landRevisionId,
        RequirementRevision requirements, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (projectId == Guid.Empty || landRevisionId == Guid.Empty || requirements.ProjectId != projectId)
            throw new ArgumentException("Readiness inputs must refer to the same project and exact land revision.");
        LandRevisionId = landRevisionId;
        Requirements = requirements;
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
    }
}

// Provisional internal outcomes; public API and diagnostic catalogue remain subject to agreement.
public enum RequirementReadinessStatus { Ready, Incomplete, Unavailable }

public sealed class RequirementReadinessResult
{
    public RequirementReadinessStatus Status { get; }
    public RequirementReadinessSnapshot? Snapshot { get; }
    public IReadOnlyList<string> DiagnosticCodes { get; }

    public RequirementReadinessResult(RequirementReadinessStatus status,
        RequirementReadinessSnapshot? snapshot, IEnumerable<string> diagnosticCodes)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        var codes = diagnosticCodes.ToArray();
        if (codes.Any(string.IsNullOrWhiteSpace) || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
            throw new ArgumentException("Diagnostic codes must be nonblank and unique.", nameof(diagnosticCodes));
        if (status == RequirementReadinessStatus.Ready ? snapshot is null || codes.Length != 0 : snapshot is not null || codes.Length == 0)
            throw new ArgumentException("Readiness result payload conflicts with status.");
        Status = status;
        Snapshot = snapshot;
        DiagnosticCodes = Array.AsReadOnly(codes);
    }
}

/// <summary>Candidate M6 query port. Implementations must authorize the owner, verify saved applicable
/// land and requirement revisions, agreed policy and project state, and fail closed when unavailable.
/// No generation job is created. Consumers recheck revisions atomically before generation.</summary>
public interface IRequirementReadinessSource
{
    Task<RequirementReadinessResult> GetAsync(Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken);
}
