namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>
/// Provisional, feature-local authorization evidence carried by future F02 persistence requests.
/// Construction proves only structural validity; trusted actor provenance remains the responsibility
/// of the future <c>ICurrentUser</c>-backed application boundary.
/// </summary>
public sealed record AuthorizationBinding
{
    public AuthorizationBinding(
        Guid projectId,
        Guid authenticatedActorId,
        AuthorizationVersion version)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("A project identity is required.", nameof(projectId));
        if (authenticatedActorId == Guid.Empty)
            throw new ArgumentException("An authenticated actor identity is required.", nameof(authenticatedActorId));
        if (!version.IsValid)
            throw new ArgumentException("A positive authorization version is required.", nameof(version));

        ProjectId = projectId;
        AuthenticatedActorId = authenticatedActorId;
        Version = version;
    }

    public Guid ProjectId { get; }
    public Guid AuthenticatedActorId { get; }
    public AuthorizationVersion Version { get; }
}

/// <summary>Internal structural outcomes; these are not public API error codes.</summary>
public enum AuthorizationBindingValidationStatus
{
    Valid,
    MissingEvidence,
    InvalidExpectedIdentity,
    InvalidEvidence,
    ProjectMismatch,
    ActorMismatch,
    VersionMismatch,
}

/// <summary>Pure, fail-closed validation for exact authorization evidence.</summary>
public static class AuthorizationBindingValidator
{
    public static AuthorizationBindingValidationStatus Validate(
        AuthorizationBinding? evidence,
        Guid expectedProjectId,
        Guid expectedAuthenticatedActorId,
        AuthorizationVersion expectedVersion)
    {
        if (expectedProjectId == Guid.Empty || expectedAuthenticatedActorId == Guid.Empty || !expectedVersion.IsValid)
            return AuthorizationBindingValidationStatus.InvalidExpectedIdentity;
        if (evidence is null)
            return AuthorizationBindingValidationStatus.MissingEvidence;
        if (evidence.ProjectId == Guid.Empty || evidence.AuthenticatedActorId == Guid.Empty || !evidence.Version.IsValid)
            return AuthorizationBindingValidationStatus.InvalidEvidence;
        if (evidence.ProjectId != expectedProjectId)
            return AuthorizationBindingValidationStatus.ProjectMismatch;
        if (evidence.AuthenticatedActorId != expectedAuthenticatedActorId)
            return AuthorizationBindingValidationStatus.ActorMismatch;
        if (evidence.Version != expectedVersion)
            return AuthorizationBindingValidationStatus.VersionMismatch;
        return AuthorizationBindingValidationStatus.Valid;
    }
}

/// <summary>
/// Provisional operation categories only. Their database keys, canonical business fields and durable
/// idempotency scopes remain cross-member decisions.
/// <c>CandidateExpire</c> does not authorize a background worker or a fabricated user identity:
/// user-driven expiration and a future system capability require separate authority semantics.
/// </summary>
public enum ProvisionalMutationOperation
{
    CandidateCreate,
    CandidateApply,
    CandidateReject,
    CandidateExpire,
    PlanRevisionAppend,
    FinalPlanConfirmation,
}

/// <summary>
/// Structurally valid mutation identity over an already-computed command fingerprint. It deliberately
/// does not choose a canonical serialization profile or a database uniqueness key. A positive profile
/// version is structurally valid only; it does not establish that production supports that profile.
/// </summary>
public sealed record MutationIdentity
{
    public MutationIdentity(
        Guid operationId,
        ProvisionalMutationOperation operation,
        int fingerprintProfileVersion,
        CommandFingerprint fingerprint)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("An operation identity is required.", nameof(operationId));
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (fingerprintProfileVersion <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(fingerprintProfileVersion), "A positive fingerprint profile version is required.");
        if (!fingerprint.IsValid)
            throw new ArgumentException("A valid command fingerprint is required.", nameof(fingerprint));

        OperationId = operationId;
        Operation = operation;
        FingerprintProfileVersion = fingerprintProfileVersion;
        Fingerprint = fingerprint;
    }

    public Guid OperationId { get; }
    public ProvisionalMutationOperation Operation { get; }
    public int FingerprintProfileVersion { get; }
    public CommandFingerprint Fingerprint { get; }
}

public enum MutationIdentityComparisonStatus
{
    ExactMatch,
    MissingIdentity,
    OperationIdMismatch,
    OperationTypeMismatch,
    FingerprintProfileMismatch,
    FingerprintMismatch,
}

public static class MutationIdentityValidator
{
    public static MutationIdentityComparisonStatus Compare(
        MutationIdentity? actual,
        MutationIdentity? expected)
    {
        if (actual is null || expected is null)
            return MutationIdentityComparisonStatus.MissingIdentity;
        if (actual.OperationId != expected.OperationId)
            return MutationIdentityComparisonStatus.OperationIdMismatch;
        if (actual.Operation != expected.Operation)
            return MutationIdentityComparisonStatus.OperationTypeMismatch;
        if (actual.FingerprintProfileVersion != expected.FingerprintProfileVersion)
            return MutationIdentityComparisonStatus.FingerprintProfileMismatch;
        if (!actual.Fingerprint.Matches(expected.Fingerprint))
            return MutationIdentityComparisonStatus.FingerprintMismatch;
        return MutationIdentityComparisonStatus.ExactMatch;
    }
}

/// <summary>
/// Opaque scope for a future authorization-aware reconciliation query. This type neither executes a
/// query nor grants retry permission.
/// </summary>
public sealed record ReconciliationReference
{
    public ReconciliationReference(
        Guid planId,
        AuthorizationBinding authorization,
        MutationIdentity mutation)
    {
        if (planId == Guid.Empty)
            throw new ArgumentException("A plan identity is required.", nameof(planId));
        Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));

        PlanId = planId;
    }

    public Guid PlanId { get; }
    public AuthorizationBinding Authorization { get; }
    public MutationIdentity Mutation { get; }
}

public enum ReconciliationStatus
{
    CommittedMatching,
    ConfirmedAbsent,
    ConflictingIdentity,
    UnauthorizedOrNotFound,
    DependencyUnavailable,
    Indeterminate,
}

/// <summary>
/// Immutable structural evidence describing the historical authorization and mutation identity associated
/// with a reported durable outcome. Construction does not prove authentication, authorized disclosure or
/// that a database commit occurred.
/// </summary>
public sealed record CommittedMutationEvidence
{
    public CommittedMutationEvidence(
        Guid durableOutcomeId,
        Guid planId,
        AuthorizationBinding historicalAuthorization,
        MutationIdentity mutation)
    {
        if (durableOutcomeId == Guid.Empty)
            throw new ArgumentException("A durable outcome identity is required.", nameof(durableOutcomeId));
        if (planId == Guid.Empty)
            throw new ArgumentException("A plan identity is required.", nameof(planId));
        HistoricalAuthorization = historicalAuthorization ?? throw new ArgumentNullException(nameof(historicalAuthorization));
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));

        DurableOutcomeId = durableOutcomeId;
        PlanId = planId;
    }

    public Guid DurableOutcomeId { get; }
    public Guid PlanId { get; }
    public AuthorizationBinding HistoricalAuthorization { get; }
    public MutationIdentity Mutation { get; }
}

/// <summary>
/// Provisional reconciliation result. The reference carries fresh authorization for the current read;
/// committed evidence describes the historical mutation authority. Their authorization versions and actors
/// are intentionally not required to match. Structural matching does not establish authentication, database
/// commit, authorized retrieval or durable absence. <c>ConfirmedAbsent</c> is only a reported structural
/// outcome and requires future trusted, authorized durable-query evidence. No outcome permits automatic retry.
/// </summary>
public sealed record ReconciliationResult
{
    public ReconciliationResult(
        ReconciliationStatus status,
        ReconciliationReference reference,
        CommittedMutationEvidence? committedEvidence = null)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        if (status == ReconciliationStatus.CommittedMatching && committedEvidence is null)
            throw new ArgumentNullException(nameof(committedEvidence), "A matching commit requires exact immutable evidence.");
        if (status != ReconciliationStatus.CommittedMatching && committedEvidence is not null)
            throw new ArgumentException("Only a matching committed result may contain committed evidence.", nameof(committedEvidence));

        if (committedEvidence is not null)
        {
            if (committedEvidence.PlanId != reference.PlanId ||
                committedEvidence.HistoricalAuthorization.ProjectId != reference.Authorization.ProjectId ||
                MutationIdentityValidator.Compare(committedEvidence.Mutation, reference.Mutation) !=
                    MutationIdentityComparisonStatus.ExactMatch)
                throw new ArgumentException(
                    "Committed evidence must match the reconciliation project, plan and mutation identity.",
                    nameof(committedEvidence));
        }

        Status = status;
        CommittedEvidence = committedEvidence;
    }

    public ReconciliationStatus Status { get; }
    public ReconciliationReference Reference { get; }
    public CommittedMutationEvidence? CommittedEvidence { get; }

    /// <summary>Reconciliation never authorizes an automatic mutation replay.</summary>
    public bool AllowsAutomaticRetry => false;
}

public enum ReconciliationResultValidationStatus
{
    Valid,
    MissingResult,
    InvalidExpectedReference,
    ProjectMismatch,
    PlanMismatch,
    CurrentAuthorizationMismatch,
    MutationIdentityMismatch,
    MissingExpectedHistoricalAuthorization,
    HistoricalAuthorizationMismatch,
    CommittedEvidenceMismatch,
}

/// <summary>
/// Exact structural validation against the caller's fresh reconciliation scope. For a committed result,
/// historical authorization is checked independently and is never substituted for fresh authorization.
/// </summary>
public static class ReconciliationResultValidator
{
    public static ReconciliationResultValidationStatus Validate(
        ReconciliationResult? result,
        ReconciliationReference? expectedReference,
        AuthorizationBinding? expectedHistoricalAuthorization = null)
    {
        if (result is null)
            return ReconciliationResultValidationStatus.MissingResult;
        if (expectedReference is null)
            return ReconciliationResultValidationStatus.InvalidExpectedReference;
        if (result.Reference.Authorization.ProjectId != expectedReference.Authorization.ProjectId)
            return ReconciliationResultValidationStatus.ProjectMismatch;
        if (result.Reference.PlanId != expectedReference.PlanId)
            return ReconciliationResultValidationStatus.PlanMismatch;
        if (AuthorizationBindingValidator.Validate(
                result.Reference.Authorization,
                expectedReference.Authorization.ProjectId,
                expectedReference.Authorization.AuthenticatedActorId,
                expectedReference.Authorization.Version) != AuthorizationBindingValidationStatus.Valid)
            return ReconciliationResultValidationStatus.CurrentAuthorizationMismatch;
        if (MutationIdentityValidator.Compare(result.Reference.Mutation, expectedReference.Mutation) !=
            MutationIdentityComparisonStatus.ExactMatch)
            return ReconciliationResultValidationStatus.MutationIdentityMismatch;

        if (result.Status != ReconciliationStatus.CommittedMatching)
            return result.CommittedEvidence is null
                ? ReconciliationResultValidationStatus.Valid
                : ReconciliationResultValidationStatus.CommittedEvidenceMismatch;

        if (result.CommittedEvidence is null)
            return ReconciliationResultValidationStatus.CommittedEvidenceMismatch;
        if (expectedHistoricalAuthorization is null)
            return ReconciliationResultValidationStatus.MissingExpectedHistoricalAuthorization;
        if (result.CommittedEvidence.PlanId != result.Reference.PlanId ||
            result.CommittedEvidence.HistoricalAuthorization.ProjectId != result.Reference.Authorization.ProjectId ||
            MutationIdentityValidator.Compare(result.CommittedEvidence.Mutation, result.Reference.Mutation) !=
                MutationIdentityComparisonStatus.ExactMatch)
            return ReconciliationResultValidationStatus.CommittedEvidenceMismatch;
        if (AuthorizationBindingValidator.Validate(
                result.CommittedEvidence.HistoricalAuthorization,
                expectedHistoricalAuthorization.ProjectId,
                expectedHistoricalAuthorization.AuthenticatedActorId,
                expectedHistoricalAuthorization.Version) != AuthorizationBindingValidationStatus.Valid)
            return ReconciliationResultValidationStatus.HistoricalAuthorizationMismatch;

        return ReconciliationResultValidationStatus.Valid;
    }
}

public enum AtomicMutationStatus
{
    Succeeded,
    MatchingDuplicate,
    AuthorizationDenied,
    AuthorizationVersionMismatch,
    StaleBaseRevision,
    StaleApplicableInputs,
    CandidateStateConflict,
    FingerprintConflict,
    InvalidDependencyResponse,
    DependencyUnavailable,
    PersistenceFailure,
    TransactionOutcomeUnknown,
}

/// <summary>Protected evidence returned only for an exact successful or matching-duplicate outcome.</summary>
public sealed record AtomicMutationEvidence
{
    public AtomicMutationEvidence(
        Guid durableOutcomeId,
        Guid planId,
        AuthorizationBinding authorization,
        MutationIdentity mutation)
    {
        if (durableOutcomeId == Guid.Empty)
            throw new ArgumentException("A durable outcome identity is required.", nameof(durableOutcomeId));
        if (planId == Guid.Empty)
            throw new ArgumentException("A plan identity is required.", nameof(planId));
        Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        DurableOutcomeId = durableOutcomeId;
        PlanId = planId;
    }

    public Guid DurableOutcomeId { get; }
    public Guid PlanId { get; }
    public AuthorizationBinding Authorization { get; }
    public MutationIdentity Mutation { get; }
}

/// <summary>
/// Database-free F02 result foundation. It defines safe structural combinations only and does not claim
/// persistence atomicity, authorization provenance or a production error mapping. Provider-specific failure
/// mapping remains an M4 integration responsibility. Dependency unavailability represents only confirmed
/// pre-mutation unavailability; uncertain writes, commits or rollbacks must remain TransactionOutcomeUnknown.
/// </summary>
public sealed record AtomicMutationResult
{
    public AtomicMutationResult(
        AtomicMutationStatus status,
        AtomicMutationEvidence? evidence = null,
        ReconciliationReference? reconciliation = null)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        var succeeded = status is AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate;
        if (succeeded && evidence is null)
            throw new ArgumentNullException(nameof(evidence), "A successful outcome requires exact durable evidence.");
        if (!succeeded && evidence is not null)
            throw new ArgumentException("A failed or uncertain outcome cannot expose success evidence.", nameof(evidence));
        if (status == AtomicMutationStatus.TransactionOutcomeUnknown && reconciliation is null)
            throw new ArgumentNullException(nameof(reconciliation), "An unknown transaction outcome requires a reconciliation reference.");
        if (status != AtomicMutationStatus.TransactionOutcomeUnknown && reconciliation is not null)
            throw new ArgumentException("Only an unknown transaction outcome may carry a reconciliation reference.", nameof(reconciliation));

        Status = status;
        Evidence = evidence;
        Reconciliation = reconciliation;
    }

    public AtomicMutationStatus Status { get; }
    public AtomicMutationEvidence? Evidence { get; }
    public ReconciliationReference? Reconciliation { get; }

    /// <summary>No result from this foundation permits blind mutation replay.</summary>
    public bool AllowsAutomaticRetry => false;
}

public enum AtomicMutationResultValidationStatus
{
    Valid,
    MissingResult,
    InvalidExpectedEvidence,
    NoBindableEvidence,
    PlanScopeMismatch,
    AuthorizationMismatch,
    MutationIdentityMismatch,
}

/// <summary>
/// Pure exact-binding validation for a structurally valid atomic result. <c>Valid</c> means only that
/// success evidence, matching-duplicate evidence or an unknown-outcome reference exactly matches the
/// expected structural scope. It does not mean successful mutation, database commit, trusted authentication,
/// authorized disclosure or permission to retry. Future consumers must inspect the mutation status, validation
/// outcome and trusted authority; a valid unknown-outcome reference remains uncertain.
/// </summary>
public static class AtomicMutationResultValidator
{
    public static AtomicMutationResultValidationStatus Validate(
        AtomicMutationResult? result,
        Guid expectedPlanId,
        AuthorizationBinding expectedAuthorization,
        MutationIdentity expectedMutation)
    {
        if (result is null)
            return AtomicMutationResultValidationStatus.MissingResult;
        if (expectedPlanId == Guid.Empty || expectedAuthorization is null || expectedMutation is null)
            return AtomicMutationResultValidationStatus.InvalidExpectedEvidence;

        var planId = result.Evidence?.PlanId ?? result.Reconciliation?.PlanId;
        var authorization = result.Evidence?.Authorization ?? result.Reconciliation?.Authorization;
        var mutation = result.Evidence?.Mutation ?? result.Reconciliation?.Mutation;
        if (authorization is null || mutation is null)
            return AtomicMutationResultValidationStatus.NoBindableEvidence;
        if (planId != expectedPlanId)
            return AtomicMutationResultValidationStatus.PlanScopeMismatch;

        if (AuthorizationBindingValidator.Validate(
                authorization,
                expectedAuthorization.ProjectId,
                expectedAuthorization.AuthenticatedActorId,
                expectedAuthorization.Version) != AuthorizationBindingValidationStatus.Valid)
            return AtomicMutationResultValidationStatus.AuthorizationMismatch;

        return MutationIdentityValidator.Compare(mutation, expectedMutation) == MutationIdentityComparisonStatus.ExactMatch
            ? AtomicMutationResultValidationStatus.Valid
            : AtomicMutationResultValidationStatus.MutationIdentityMismatch;
    }
}
