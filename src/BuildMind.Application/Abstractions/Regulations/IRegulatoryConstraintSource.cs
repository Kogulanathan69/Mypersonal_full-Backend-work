namespace BuildMind.Application.Abstractions.Regulations;

/// <summary>
/// M7 human-reviewed regulatory constraint port (constraints contract v0; PENDING M6 + lead approval, doc 29).
/// Returns an evidence pack bound to the exact project, land and requirement revisions supplied. It never converts
/// retrieved passages into constraints and never decides plan compliance: M6's deterministic validator is
/// authoritative. Only <see cref="ConstraintKindStatus.Verified"/> kinds carry human-reviewed values that may block.
/// </summary>
public interface IRegulatoryConstraintSource
{
    Task<RegulatoryConstraintPack> GetConstraintsAsync(RegulatoryConstraintQuery query, CancellationToken cancellationToken);
}

public enum RegulatoryConstraintKind
{
    RearSpaceMin,
    SideSpaceMin,
    FrontSetbackMin,
    BuildingHeightMax,
    PlotCoverageMax,
    FloorAreaRatioMax,
    ParkingSpacesMin,
}

/// <summary>Per-kind evidence state. Anything other than <see cref="Verified"/> requires review; MISSING is never "no limit".</summary>
public enum ConstraintKindStatus
{
    Verified,
    Conflicting,
    Unverified,
    Missing,
}

/// <summary>The exact revisions an evidence pack is bound to (never inferred by the provider).</summary>
/// <param name="ProjectRevision">The project's Int64 revision (<c>Project.Revision</c>), 1 to <see cref="MaxProjectRevision"/>.</param>
public sealed record ConstraintBinding(Guid ProjectId, long ProjectRevision, Guid LandRevisionId, Guid RequirementRevisionId)
{
    /// <summary>Largest revision that canonical JSON (BM-JCS-1) represents exactly in both languages (below 1e15).</summary>
    public const long MaxProjectRevision = 999_999_999_999_999;
}

public sealed record RegulatoryConstraintQuery(
    string Jurisdiction,
    IReadOnlyList<RegulatoryConstraintKind> Kinds,
    ConstraintBinding Binding,
    bool ResidentialOnly = false);

/// <param name="EvidencePackId">SHA-256 (BM-JCS-1) of the pack without this field, recomputed and verified on receipt.</param>
/// <param name="TableSha256">Integrity digest of the sealed, human-reviewed constraint table that produced the pack.</param>
/// <param name="ReviewerRegistrySha256">Integrity digest of the reviewer registry that authorized every sign-off.</param>
/// <param name="Synthetic">True only for test tables; accepted only where synthetic evidence is explicitly allowed.</param>
public sealed record RegulatoryConstraintPack(
    string EvidencePackId,
    string TableVersion,
    string TableSha256,
    string ReviewerRegistryVersion,
    string ReviewerRegistrySha256,
    bool Synthetic,
    string Jurisdiction,
    ConstraintBinding Binding,
    IReadOnlyList<ConstraintKindResult> Kinds,
    bool Complete,
    IReadOnlyList<string> Warnings);

public sealed record ConstraintKindResult(RegulatoryConstraintKind Kind, ConstraintKindStatus Status, IReadOnlyList<RegulatoryConstraint> Constraints);

/// <param name="ReviewStatus"><c>HUMAN_REVIEWED</c> or <c>UNREVIEWED</c> (rejected constraints are never returned).</param>
/// <param name="ReviewerId">Pseudonymous reviewer identifier (never a personal name); null when unreviewed.</param>
/// <param name="ReviewReference">Review record reference (for example <c>BMR-2026-001</c>); null when unreviewed.</param>
public sealed record RegulatoryConstraint(
    string ConstraintId,
    string Comparator,
    double Value,
    string Unit,
    string AppliesTo,
    IReadOnlyList<ConstraintCondition> Conditions,
    string ReviewStatus,
    string? ReviewerRole,
    string? ReviewerId,
    string? ReviewReference,
    DateOnly? ReviewedAt,
    ConstraintSource Source);

public sealed record ConstraintCondition(string Field, string Comparator, double Value, string Unit);

/// <param name="Page">One-based PDF page position.</param>
public sealed record ConstraintSource(string DocumentId, int Page, int StartChar, int EndChar, string Excerpt);
