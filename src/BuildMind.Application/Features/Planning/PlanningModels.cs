using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.Planning;

/// <summary>
/// Captain planning workflow (land boundary, generation jobs, candidate persistence). Development-only composition;
/// production activation is NOT approved. Wire contracts to the Python service are the existing M6 drafts
/// (<see cref="GenerationInputDto"/> / <see cref="GenerationResultDto"/>), unchanged.
/// </summary>
public static class PlanningSchemas
{
    public const string MetricLand = "metric-land/0.1-draft";
    public const string EvidenceModeNone = "none";
    public const string EvidenceModeSynthetic = "synthetic-fixture";
    public const string EvidenceModeRag = "rag-constraints";

    /// <summary>Recorded on plan validation evidence produced from synthetic setbacks; never "REVIEWED".</summary>
    public const string SyntheticEvidenceStatus = "SYNTHETIC_FIXTURE";
    public const string UnverifiedEvidenceStatus = "UNVERIFIED";
}

public enum PlanningStatus
{
    Success,
    Unauthenticated,
    NotFound,
    ValidationFailed,
    Conflict,
    StaleInputs,
    InputsMissing,
    GenerationInProgress,
    GenerationUnsuccessful,
    PolicyUnavailable,
    DependencyUnavailable,
    DependencyTimeout,
}

public sealed record PlanningResult<T>(PlanningStatus Status, T? Value = default, string? Code = null,
    IReadOnlyList<DiagnosticDto>? Diagnostics = null)
{
    public static PlanningResult<T> Ok(T value) => new(PlanningStatus.Success, value);
}

public enum ProtectedWriteStatus { Written, Denied, Conflict }

/// <summary>Immutable, user-confirmed land boundary in a local metric frame. Survey link is provenance only.</summary>
public sealed record LandRevision(
    Guid Id, Guid ProjectId, long RevisionNumber, MetricLandDto Land, double AreaM2, int EdgeCount,
    Guid? SurveyId, Guid CreatedBy, DateTimeOffset CreatedAtUtc);

public enum GenerationJobStatus { Running, Succeeded, Failed }

public sealed record GenerationJob(
    Guid Id, Guid ProjectId, Guid RequestedBy, string IdempotencyKey, Guid LandRevisionId,
    Guid RequirementsRevisionId, GenerationJobStatus Status, string? Outcome, string? ReasonCode,
    string? AlgorithmVersion, string EvidenceMode, int Seed, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc, IReadOnlyList<DiagnosticDto> Diagnostics);

/// <summary>One validated alternative, persisted exactly as generated (M3 draft geometry + M6 metadata).</summary>
public sealed record GenerationCandidate(
    Guid Id, Guid JobId, Guid ProjectId, int Ordinal, PlanGeometryDto Geometry, string GeometryJson,
    string GeometryHash, AlternativeMetricsDto Metrics, ValidationResultDto Validation, string DiversitySignature,
    DateTimeOffset CreatedAtUtc);

public sealed record LandCheckResult(bool Valid, string Code, double? AreaM2, int? EdgeCount);

public sealed record PlanCheckRequest(
    Guid ProjectId, Guid LandRevisionId, MetricLandDto Land, IReadOnlyList<double>? SetbackDistancesM,
    IReadOnlyDictionary<string, int>? ExpectedRoomCounts, PlanGeometryDto Geometry);

public sealed record PlanCheckResult(
    bool GeometryValid, bool? RequirementsMatch, string ValidatorVersion, IReadOnlyList<DiagnosticDto> Diagnostics);

/// <summary>Operator-supplied, synthetic, development-only generation policy (no compiled-in values).</summary>
public sealed record GenerationPolicy(
    string Version, string Label, IReadOnlyDictionary<string, RoomDimensionsDto> RoomDimensions,
    string EvidenceMode, double? SyntheticSetbackM, string? SyntheticRuleSetVersion);

public interface IGenerationPolicyProvider
{
    /// <summary>Null means unavailable (fail closed), never a permissive default.</summary>
    GenerationPolicy? Get();
}

public interface ILandRevisionStore
{
    Task<LandRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken);
    Task<LandRevision?> GetAsync(Guid projectId, Guid landRevisionId, CancellationToken cancellationToken);

    /// <summary>Protected append: project row lock + owner + authorization version, then current-head CAS.</summary>
    Task<ProtectedWriteStatus> TryAppendAsync(LandRevision revision, Guid? expectedCurrentId, long authorizationVersion,
        CancellationToken cancellationToken);
}

public interface IGenerationJobStore
{
    Task<GenerationJob?> FindByKeyAsync(Guid projectId, Guid actorId, string idempotencyKey, CancellationToken cancellationToken);
    Task<GenerationJob?> GetAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<GenerationCandidate>> GetCandidatesAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken);

    /// <summary>Protected insert of a Running job; a duplicate (project, actor, key) returns Conflict.</summary>
    Task<ProtectedWriteStatus> TryStartAsync(GenerationJob running, long authorizationVersion, CancellationToken cancellationToken);

    /// <summary>Protected Running -> terminal transition plus candidate inserts in one transaction.</summary>
    Task<ProtectedWriteStatus> TryCompleteAsync(GenerationJob completed, IReadOnlyList<GenerationCandidate> candidates,
        long authorizationVersion, CancellationToken cancellationToken);
}

/// <summary>Private Python service (fail closed). Failures throw DependencyFailureException.</summary>
public interface IPlanningComputeClient
{
    Task<LandCheckResult> ValidateLandAsync(Guid projectId, Guid landRevisionId, MetricLandDto land, CancellationToken cancellationToken);
    Task<GenerationResultDto> RunGenerationAsync(GenerationInputDto input, bool syntheticReviewedSetbacks, CancellationToken cancellationToken);
    Task<PlanCheckResult> ValidatePlanAsync(PlanCheckRequest request, CancellationToken cancellationToken);
}

/// <summary>Server-issued plan validation evidence (the only evidence a revision may cite).</summary>
public sealed record IssuedValidationEvidence(Guid ProjectId, Guid PlanId, PlanValidationReferenceDto Reference,
    IReadOnlyList<DiagnosticDto> Diagnostics);

public interface IPlanCreationStore
{
    /// <summary>
    /// Protected creation of a plan with its first immutable revision and the server-issued evidence it cites
    /// (project row lock + owner + version). An existing plan id returns Conflict.
    /// </summary>
    Task<ProtectedWriteStatus> TryCreateAsync(PlanRevisionSnapshot firstRevision, IssuedValidationEvidence evidence,
        long authorizationVersion, CancellationToken cancellationToken);
}

public sealed record PlanConfirmationSummary(Guid ConfirmationId, Guid ConfirmedRevisionId, string GeometryHash,
    Guid ValidationId, DateTimeOffset ConfirmedAtUtc, bool WarningAcknowledgement);

public sealed record PlanSummary(Guid PlanId, Guid? HeadRevisionId, long? HeadVersionNumber, DateTimeOffset? HeadCreatedAtUtc,
    PlanConfirmationSummary? Confirmation);

/// <summary>Read model of a project's plans (caller must establish ownership first).</summary>
public interface IPlanCatalog
{
    Task<IReadOnlyList<PlanSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken);
}
