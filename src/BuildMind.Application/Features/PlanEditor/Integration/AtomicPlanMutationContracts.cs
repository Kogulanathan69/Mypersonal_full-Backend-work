using System.Buffers;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>
/// Immutable, database-free authority and identity scope shared by every F02 mutation request.
/// Trusted actor acquisition remains the responsibility of the ICurrentUser-backed application boundary.
/// </summary>
public sealed class AtomicMutationScope
{
    private AtomicMutationScope(
        Guid projectId,
        Guid planId,
        Guid operationId,
        ProvisionalMutationOperation operation,
        Guid trustedActorId,
        AuthorizationVersion authorizationVersion)
    {
        ProjectId = projectId;
        PlanId = planId;
        OperationId = operationId;
        Operation = operation;
        TrustedActorId = trustedActorId;
        AuthorizationVersion = authorizationVersion;
    }

    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public Guid OperationId { get; }
    public ProvisionalMutationOperation Operation { get; }
    public Guid TrustedActorId { get; }
    public AuthorizationVersion AuthorizationVersion { get; }

    public static AtomicMutationScope Create(
        Guid projectId,
        Guid planId,
        Guid operationId,
        ProvisionalMutationOperation operation,
        Guid trustedActorId,
        AuthorizationVersion authorizationVersion)
    {
        AtomicMutationContractGuards.RequireId(projectId, nameof(projectId));
        AtomicMutationContractGuards.RequireId(planId, nameof(planId));
        AtomicMutationContractGuards.RequireId(operationId, nameof(operationId));
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        AtomicMutationContractGuards.RequireId(trustedActorId, nameof(trustedActorId));
        if (!authorizationVersion.IsValid)
            throw new ArgumentException("A positive authorization version is required.", nameof(authorizationVersion));

        return new AtomicMutationScope(
            projectId, planId, operationId, operation, trustedActorId, authorizationVersion);
    }

    internal MutationFingerprintScope ToFingerprintScope() =>
        new(ProjectId, PlanId, OperationId, TrustedActorId, AuthorizationVersion);
}

/// <summary>Independently owned command snapshot used only as typed V1 fingerprint material.</summary>
public sealed class FrozenPlanEditCommand
{
    private readonly JsonElement _payload;

    private FrozenPlanEditCommand(
        string schemaVersion,
        Guid commandId,
        string commandType,
        Guid floorId,
        Guid? targetId,
        JsonElement payload)
    {
        SchemaVersion = schemaVersion;
        CommandId = commandId;
        CommandType = commandType;
        FloorId = floorId;
        TargetId = targetId;
        _payload = payload;
    }

    public string SchemaVersion { get; }
    public Guid CommandId { get; }
    public string CommandType { get; }
    public Guid FloorId { get; }
    public Guid? TargetId { get; }
    public JsonElement Payload => _payload.Clone();

    public static FrozenPlanEditCommand Create(string schemaVersion, PlanEditCommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AtomicMutationContractGuards.RequireText(schemaVersion, nameof(schemaVersion));
        AtomicMutationContractGuards.RequireId(command.CommandId, nameof(command.CommandId));
        AtomicMutationContractGuards.RequireText(command.CommandType, nameof(command.CommandType));
        AtomicMutationContractGuards.RequireId(command.FloorId, nameof(command.FloorId));
        if (command.TargetId == Guid.Empty)
            throw new ArgumentException("A target identity, when supplied, cannot be empty.", nameof(command));
        if (command.Payload.ValueKind is JsonValueKind.Undefined)
            throw new ArgumentException("A command payload is required.", nameof(command));

        return new FrozenPlanEditCommand(
            schemaVersion,
            command.CommandId,
            command.CommandType,
            command.FloorId,
            command.TargetId,
            command.Payload.Clone());
    }

    internal FrozenPlanEditCommand Copy() =>
        new(SchemaVersion, CommandId, CommandType, FloorId, TargetId, _payload.Clone());

    internal PlanEditCommandDto ToFingerprintCommand() => new()
    {
        CommandId = CommandId,
        CommandType = CommandType,
        FloorId = FloorId,
        TargetId = TargetId,
        Payload = _payload.Clone(),
    };
}

/// <summary>Exact, independently owned geometry JSON and its verified canonical digest.</summary>
public sealed class FrozenGeometrySnapshot
{
    private FrozenGeometrySnapshot(
        string schemaVersion,
        string exactJson,
        string geometryHash,
        Guid projectId,
        Guid planId,
        Guid landRevisionId,
        Guid requirementsRevisionId)
    {
        SchemaVersion = schemaVersion;
        ExactJson = exactJson;
        GeometryHash = geometryHash;
        ProjectId = projectId;
        PlanId = planId;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
    }

    public string SchemaVersion { get; }
    public string ExactJson { get; }
    public string GeometryHash { get; }
    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }

    public static FrozenGeometrySnapshot Create(
        string schemaVersion,
        string exactJson,
        string expectedGeometryHash)
    {
        AtomicMutationContractGuards.RequireText(schemaVersion, nameof(schemaVersion));
        AtomicMutationContractGuards.RequireText(exactJson, nameof(exactJson));
        AtomicMutationContractGuards.RequireGeometryJsonSize(exactJson, nameof(exactJson));
        AtomicMutationContractGuards.RequireCanonicalHash(expectedGeometryHash, nameof(expectedGeometryHash));
        AtomicMutationContractGuards.ValidateStrictJson(exactJson, nameof(exactJson));

        PlanGeometryDto geometry;
        try
        {
            geometry = JsonSerializer.Deserialize<PlanGeometryDto>(exactJson, PlanEditorJson.StrictOptions)
                ?? throw new ArgumentException("Geometry JSON cannot be null.", nameof(exactJson));
            PlanEditorContractValidator.Validate(geometry);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Geometry JSON does not satisfy the strict Plan Editor contract.", nameof(exactJson), exception);
        }

        if (!string.Equals(schemaVersion, geometry.SchemaVersion, StringComparison.Ordinal))
            throw new ArgumentException("Geometry schema does not match the frozen schema version.", nameof(schemaVersion));

        var computed = PlanRevisionService.ComputeGeometryHash(exactJson);
        if (!string.Equals(computed, expectedGeometryHash, StringComparison.Ordinal))
            throw new ArgumentException("Geometry JSON does not match its exact digest.", nameof(expectedGeometryHash));

        return new FrozenGeometrySnapshot(
            schemaVersion, exactJson, expectedGeometryHash,
            geometry.ProjectId, geometry.PlanId,
            geometry.LandRevisionId, geometry.RequirementsRevisionId);
    }

    internal FrozenGeometrySnapshot Copy() => new(
        SchemaVersion, ExactJson, GeometryHash,
        ProjectId, PlanId, LandRevisionId, RequirementsRevisionId);
}

/// <summary>Frozen validation identity and exact geometry/applicable-input bindings.</summary>
public sealed class FrozenValidationEvidence
{
    private readonly string[] _issueCodes;

    private FrozenValidationEvidence(
        Guid validationId,
        string validatorVersion,
        string geometryHash,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        bool geometryValid,
        bool? requirementsMatch,
        string regulatoryEvidenceStatus,
        bool hasBlockingIssues,
        DateTimeOffset validatedAtUtc,
        string[] issueCodes)
    {
        ValidationId = validationId;
        ValidatorVersion = validatorVersion;
        GeometryHash = geometryHash;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        GeometryValid = geometryValid;
        RequirementsMatch = requirementsMatch;
        RegulatoryEvidenceStatus = regulatoryEvidenceStatus;
        HasBlockingIssues = hasBlockingIssues;
        RequiresWarningAcknowledgement =
            requirementsMatch is not true ||
            !string.Equals(regulatoryEvidenceStatus, "VERIFIED", StringComparison.Ordinal);
        ValidatedAtUtc = validatedAtUtc;
        _issueCodes = issueCodes;
    }

    public Guid ValidationId { get; }
    public string ValidatorVersion { get; }
    public string GeometryHash { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public bool GeometryValid { get; }
    public bool? RequirementsMatch { get; }
    public string RegulatoryEvidenceStatus { get; }
    public bool HasBlockingIssues { get; }
    public bool RequiresWarningAcknowledgement { get; }
    public DateTimeOffset ValidatedAtUtc { get; }
    public IReadOnlyList<string> IssueCodes =>
        new ReadOnlyCollection<string>((string[])_issueCodes.Clone());

    public static FrozenValidationEvidence Create(
        Guid validationId,
        string validatorVersion,
        string geometryHash,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        bool geometryValid,
        bool? requirementsMatch,
        string regulatoryEvidenceStatus,
        bool hasBlockingIssues,
        DateTimeOffset validatedAtUtc,
        IEnumerable<string>? issueCodes = null)
    {
        AtomicMutationContractGuards.RequireId(validationId, nameof(validationId));
        AtomicMutationContractGuards.RequireBoundedText(
            validatorVersion, AtomicMutationContractGuards.MaxValidatorVersionCharacters,
            nameof(validatorVersion));
        AtomicMutationContractGuards.RequireCanonicalHash(geometryHash, nameof(geometryHash));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));
        AtomicMutationContractGuards.RequireKnownRegulatoryEvidenceStatus(
            regulatoryEvidenceStatus, nameof(regulatoryEvidenceStatus));
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(validatedAtUtc, nameof(validatedAtUtc));

        var copiedCodes = AtomicMutationContractGuards.CopyBoundedIssueCodes(
            issueCodes, nameof(issueCodes));

        return new FrozenValidationEvidence(
            validationId, validatorVersion, geometryHash, landRevisionId, requirementsRevisionId,
            geometryValid, requirementsMatch, regulatoryEvidenceStatus, hasBlockingIssues,
            validatedAtUtc, copiedCodes);
    }

    internal FrozenValidationEvidence Copy() => new(
        ValidationId, ValidatorVersion, GeometryHash, LandRevisionId, RequirementsRevisionId,
        GeometryValid, RequirementsMatch, RegulatoryEvidenceStatus, HasBlockingIssues,
        ValidatedAtUtc, (string[])_issueCodes.Clone());
}

/// <summary>Prepared, non-authoritative candidate output returned by the atomic mutation provider.</summary>
public sealed class PreparedCandidateSnapshot
{
    private PreparedCandidateSnapshot(
        Guid candidateId,
        Guid ownerId,
        Guid baseRevisionId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        FrozenGeometrySnapshot geometry,
        FrozenValidationEvidence validation,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        CandidateId = candidateId;
        OwnerId = ownerId;
        BaseRevisionId = baseRevisionId;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        Geometry = geometry;
        Validation = validation;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid CandidateId { get; }
    public Guid OwnerId { get; }
    public Guid BaseRevisionId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public FrozenGeometrySnapshot Geometry { get; }
    public FrozenValidationEvidence Validation { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public static PreparedCandidateSnapshot Create(
        Guid candidateId,
        Guid ownerId,
        Guid baseRevisionId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        FrozenGeometrySnapshot geometry,
        FrozenValidationEvidence validation,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        AtomicMutationContractGuards.RequireId(candidateId, nameof(candidateId));
        AtomicMutationContractGuards.RequireId(ownerId, nameof(ownerId));
        AtomicMutationContractGuards.RequireId(baseRevisionId, nameof(baseRevisionId));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(validation);
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(createdAtUtc, nameof(createdAtUtc));
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Candidate expiry must be later than creation.", nameof(expiresAtUtc));
        AtomicMutationContractGuards.RequireValidationBinding(
            geometry, validation, landRevisionId, requirementsRevisionId);
        AtomicMutationContractGuards.RequireMutationSafeValidation(validation);

        return new PreparedCandidateSnapshot(
            candidateId, ownerId, baseRevisionId, landRevisionId, requirementsRevisionId,
            geometry.Copy(), validation.Copy(), createdAtUtc, expiresAtUtc);
    }

    internal PreparedCandidateSnapshot Copy() => new(
        CandidateId, OwnerId, BaseRevisionId, LandRevisionId, RequirementsRevisionId,
        Geometry.Copy(), Validation.Copy(), CreatedAtUtc, ExpiresAtUtc);
}

/// <summary>Frozen revision material; revision identity, number, parent and timestamps remain provider outcomes.</summary>
public sealed class PreparedRevisionSnapshot
{
    private PreparedRevisionSnapshot(
        Guid expectedBaseRevisionId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        FrozenGeometrySnapshot geometry,
        FrozenValidationEvidence validation)
    {
        ExpectedBaseRevisionId = expectedBaseRevisionId;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        Geometry = geometry;
        Validation = validation;
    }

    public Guid ExpectedBaseRevisionId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public FrozenGeometrySnapshot Geometry { get; }
    public FrozenValidationEvidence Validation { get; }

    public static PreparedRevisionSnapshot Create(
        Guid expectedBaseRevisionId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        FrozenGeometrySnapshot geometry,
        FrozenValidationEvidence validation)
    {
        AtomicMutationContractGuards.RequireId(expectedBaseRevisionId, nameof(expectedBaseRevisionId));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(validation);
        AtomicMutationContractGuards.RequireValidationBinding(
            geometry, validation, landRevisionId, requirementsRevisionId);
        AtomicMutationContractGuards.RequireMutationSafeValidation(validation);
        return new PreparedRevisionSnapshot(
            expectedBaseRevisionId, landRevisionId, requirementsRevisionId,
            geometry.Copy(), validation.Copy());
    }

    internal PreparedRevisionSnapshot Copy() => new(
        ExpectedBaseRevisionId, LandRevisionId, RequirementsRevisionId,
        Geometry.Copy(), Validation.Copy());
}

/// <summary>Exact confirmation input binding. Confirmation identity/time remain provider outcomes.</summary>
public sealed class PreparedConfirmationSnapshot
{
    private PreparedConfirmationSnapshot(
        Guid exactRevisionId,
        string geometryHash,
        Guid validationEvidenceId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        bool warningAcknowledged)
    {
        ExactRevisionId = exactRevisionId;
        GeometryHash = geometryHash;
        ValidationEvidenceId = validationEvidenceId;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        WarningAcknowledged = warningAcknowledged;
    }

    public Guid ExactRevisionId { get; }
    public string GeometryHash { get; }
    public Guid ValidationEvidenceId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public bool WarningAcknowledged { get; }

    public static PreparedConfirmationSnapshot Create(
        Guid exactRevisionId,
        string geometryHash,
        Guid validationEvidenceId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        bool warningAcknowledged)
    {
        AtomicMutationContractGuards.RequireId(exactRevisionId, nameof(exactRevisionId));
        AtomicMutationContractGuards.RequireCanonicalHash(geometryHash, nameof(geometryHash));
        AtomicMutationContractGuards.RequireId(validationEvidenceId, nameof(validationEvidenceId));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));
        return new PreparedConfirmationSnapshot(
            exactRevisionId, geometryHash, validationEvidenceId,
            landRevisionId, requirementsRevisionId, warningAcknowledged);
    }

    internal PreparedConfirmationSnapshot Copy() => new(
        ExactRevisionId, GeometryHash, ValidationEvidenceId,
        LandRevisionId, RequirementsRevisionId, WarningAcknowledged);
}

public sealed class CandidateCreateMutationRequest
{
    private readonly byte[] _canonicalBytes;

    private CandidateCreateMutationRequest(
        AtomicMutationScope scope,
        AuthorizationBinding authorization,
        MutationIdentity mutation,
        Guid expectedBaseRevisionId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        FrozenPlanEditCommand command,
        byte[] canonicalBytes)
    {
        Scope = scope;
        CurrentAuthorization = authorization;
        Mutation = mutation;
        ExpectedBaseRevisionId = expectedBaseRevisionId;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        Command = command;
        _canonicalBytes = canonicalBytes;
    }

    public AtomicMutationScope Scope { get; }
    public AuthorizationBinding CurrentAuthorization { get; }
    public MutationIdentity Mutation { get; }
    public Guid ExpectedBaseRevisionId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public FrozenPlanEditCommand Command { get; }
    public byte[] GetCanonicalFingerprintBytes() => (byte[])_canonicalBytes.Clone();

    public static CandidateCreateMutationRequest Create(
        AtomicMutationScope scope,
        AuthorizationBinding currentAuthorization,
        MutationIdentity mutation,
        Guid expectedBaseRevisionId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        string commandSchemaVersion,
        PlanEditCommandDto command)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentAuthorization);
        ArgumentNullException.ThrowIfNull(mutation);
        AtomicMutationContractGuards.RequireOperation(scope, ProvisionalMutationOperation.CandidateCreate);
        AtomicMutationContractGuards.RequireId(expectedBaseRevisionId, nameof(expectedBaseRevisionId));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));

        var frozenCommand = FrozenPlanEditCommand.Create(commandSchemaVersion, command);
        var input = new CandidateCreateFingerprintInput(
            scope.ToFingerprintScope(), expectedBaseRevisionId, landRevisionId, requirementsRevisionId,
            frozenCommand.SchemaVersion, frozenCommand.ToFingerprintCommand());
        var canonical = MutationFingerprintProfileV1.GetCanonicalBytes(input);
        AtomicMutationContractGuards.RequireCommonBinding(
            scope, currentAuthorization, mutation, ProvisionalMutationOperation.CandidateCreate,
            MutationFingerprintProfileV1.Compute(input));

        return new CandidateCreateMutationRequest(
            scope, currentAuthorization, mutation, expectedBaseRevisionId, landRevisionId,
            requirementsRevisionId, frozenCommand.Copy(), canonical);
    }
}

public sealed class CandidateApplyMutationRequest
{
    private readonly byte[] _canonicalBytes;

    private CandidateApplyMutationRequest(
        AtomicMutationScope scope, AuthorizationBinding authorization, MutationIdentity mutation,
        Guid candidateId, Guid expectedBaseRevisionId, Guid landRevisionId, Guid requirementsRevisionId,
        string candidateGeometryHash, Guid validationEvidenceId,
        PreparedRevisionSnapshot preparedRevision, byte[] canonicalBytes)
    {
        Scope = scope;
        CurrentAuthorization = authorization;
        Mutation = mutation;
        CandidateId = candidateId;
        ExpectedBaseRevisionId = expectedBaseRevisionId;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        CandidateGeometryHash = candidateGeometryHash;
        ValidationEvidenceId = validationEvidenceId;
        PreparedRevision = preparedRevision;
        _canonicalBytes = canonicalBytes;
    }

    public AtomicMutationScope Scope { get; }
    public AuthorizationBinding CurrentAuthorization { get; }
    public MutationIdentity Mutation { get; }
    public Guid CandidateId { get; }
    public Guid ExpectedBaseRevisionId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public string CandidateGeometryHash { get; }
    public Guid ValidationEvidenceId { get; }
    public PreparedRevisionSnapshot PreparedRevision { get; }
    public byte[] GetCanonicalFingerprintBytes() => (byte[])_canonicalBytes.Clone();

    public static CandidateApplyMutationRequest Create(
        AtomicMutationScope scope, AuthorizationBinding currentAuthorization, MutationIdentity mutation,
        Guid candidateId, Guid expectedBaseRevisionId, Guid landRevisionId, Guid requirementsRevisionId,
        string candidateGeometryHash, Guid validationEvidenceId,
        PreparedRevisionSnapshot preparedRevision)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentAuthorization);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(preparedRevision);
        AtomicMutationContractGuards.RequireOperation(scope, ProvisionalMutationOperation.CandidateApply);
        AtomicMutationContractGuards.RequireId(candidateId, nameof(candidateId));
        AtomicMutationContractGuards.RequireId(expectedBaseRevisionId, nameof(expectedBaseRevisionId));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));
        AtomicMutationContractGuards.RequireCanonicalHash(candidateGeometryHash, nameof(candidateGeometryHash));
        AtomicMutationContractGuards.RequireId(validationEvidenceId, nameof(validationEvidenceId));
        if (preparedRevision.Validation.RequiresWarningAcknowledgement)
            throw new ArgumentException(
                "Candidate Apply requiring warning acknowledgement is not representable by fingerprint V1.",
                nameof(preparedRevision));
        if (preparedRevision.ExpectedBaseRevisionId != expectedBaseRevisionId ||
            preparedRevision.LandRevisionId != landRevisionId ||
            preparedRevision.RequirementsRevisionId != requirementsRevisionId ||
            !string.Equals(preparedRevision.Geometry.GeometryHash, candidateGeometryHash, StringComparison.Ordinal) ||
            preparedRevision.Validation.ValidationId != validationEvidenceId)
            throw new ArgumentException("Prepared revision does not match the exact candidate inputs.", nameof(preparedRevision));
        AtomicMutationContractGuards.RequireGeometryScope(
            preparedRevision.Geometry, scope, landRevisionId, requirementsRevisionId);

        var input = new CandidateApplyFingerprintInput(
            scope.ToFingerprintScope(), candidateId, expectedBaseRevisionId, landRevisionId,
            requirementsRevisionId, candidateGeometryHash, validationEvidenceId);
        var canonical = MutationFingerprintProfileV1.GetCanonicalBytes(input);
        AtomicMutationContractGuards.RequireCommonBinding(
            scope, currentAuthorization, mutation, ProvisionalMutationOperation.CandidateApply,
            MutationFingerprintProfileV1.Compute(input));

        return new CandidateApplyMutationRequest(
            scope, currentAuthorization, mutation, candidateId, expectedBaseRevisionId, landRevisionId,
            requirementsRevisionId, candidateGeometryHash, validationEvidenceId,
            preparedRevision.Copy(), canonical);
    }
}

public sealed class CandidateRejectMutationRequest
{
    private readonly byte[] _canonicalBytes;
    private CandidateRejectMutationRequest(
        AtomicMutationScope scope, AuthorizationBinding authorization, MutationIdentity mutation,
        Guid candidateId, string? rejectionReasonCode, byte[] canonicalBytes)
    {
        Scope = scope; CurrentAuthorization = authorization; Mutation = mutation;
        CandidateId = candidateId; RejectionReasonCode = rejectionReasonCode; _canonicalBytes = canonicalBytes;
    }

    public AtomicMutationScope Scope { get; }
    public AuthorizationBinding CurrentAuthorization { get; }
    public MutationIdentity Mutation { get; }
    public Guid CandidateId { get; }
    public string? RejectionReasonCode { get; }
    public byte[] GetCanonicalFingerprintBytes() => (byte[])_canonicalBytes.Clone();

    public static CandidateRejectMutationRequest Create(
        AtomicMutationScope scope, AuthorizationBinding currentAuthorization, MutationIdentity mutation,
        Guid candidateId, string? rejectionReasonCode = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentAuthorization);
        ArgumentNullException.ThrowIfNull(mutation);
        AtomicMutationContractGuards.RequireOperation(scope, ProvisionalMutationOperation.CandidateReject);
        AtomicMutationContractGuards.RequireId(candidateId, nameof(candidateId));
        if (rejectionReasonCode is not null)
            AtomicMutationContractGuards.RequireText(rejectionReasonCode, nameof(rejectionReasonCode));
        var frozenReason = rejectionReasonCode is null ? null : string.Concat(rejectionReasonCode);
        var input = new CandidateRejectFingerprintInput(scope.ToFingerprintScope(), candidateId, frozenReason);
        var canonical = MutationFingerprintProfileV1.GetCanonicalBytes(input);
        AtomicMutationContractGuards.RequireCommonBinding(
            scope, currentAuthorization, mutation, ProvisionalMutationOperation.CandidateReject,
            MutationFingerprintProfileV1.Compute(input));
        return new CandidateRejectMutationRequest(
            scope, currentAuthorization, mutation, candidateId, frozenReason, canonical);
    }
}

public sealed class CandidateExpireMutationRequest
{
    private readonly byte[] _canonicalBytes;
    private CandidateExpireMutationRequest(
        AtomicMutationScope scope, AuthorizationBinding authorization, MutationIdentity mutation,
        Guid candidateId, DateTimeOffset expiresAtUtc, byte[] canonicalBytes)
    {
        Scope = scope; CurrentAuthorization = authorization; Mutation = mutation;
        CandidateId = candidateId; ExpiresAtUtc = expiresAtUtc; _canonicalBytes = canonicalBytes;
    }

    public AtomicMutationScope Scope { get; }
    public AuthorizationBinding CurrentAuthorization { get; }
    public MutationIdentity Mutation { get; }
    public Guid CandidateId { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public byte[] GetCanonicalFingerprintBytes() => (byte[])_canonicalBytes.Clone();

    public static CandidateExpireMutationRequest Create(
        AtomicMutationScope scope, AuthorizationBinding currentAuthorization, MutationIdentity mutation,
        Guid candidateId, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentAuthorization);
        ArgumentNullException.ThrowIfNull(mutation);
        AtomicMutationContractGuards.RequireOperation(scope, ProvisionalMutationOperation.CandidateExpire);
        AtomicMutationContractGuards.RequireId(candidateId, nameof(candidateId));
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(expiresAtUtc, nameof(expiresAtUtc));
        var input = new CandidateExpireFingerprintInput(scope.ToFingerprintScope(), candidateId, expiresAtUtc);
        var canonical = MutationFingerprintProfileV1.GetCanonicalBytes(input);
        AtomicMutationContractGuards.RequireCommonBinding(
            scope, currentAuthorization, mutation, ProvisionalMutationOperation.CandidateExpire,
            MutationFingerprintProfileV1.Compute(input));
        return new CandidateExpireMutationRequest(
            scope, currentAuthorization, mutation, candidateId, expiresAtUtc, canonical);
    }
}

public sealed class PlanRevisionAppendMutationRequest
{
    private readonly byte[] _canonicalBytes;
    private PlanRevisionAppendMutationRequest(
        AtomicMutationScope scope, AuthorizationBinding authorization, MutationIdentity mutation,
        PreparedRevisionSnapshot preparedRevision, byte[] canonicalBytes)
    {
        Scope = scope; CurrentAuthorization = authorization; Mutation = mutation;
        PreparedRevision = preparedRevision; _canonicalBytes = canonicalBytes;
    }

    public AtomicMutationScope Scope { get; }
    public AuthorizationBinding CurrentAuthorization { get; }
    public MutationIdentity Mutation { get; }
    public PreparedRevisionSnapshot PreparedRevision { get; }
    public byte[] GetCanonicalFingerprintBytes() => (byte[])_canonicalBytes.Clone();

    public static PlanRevisionAppendMutationRequest Create(
        AtomicMutationScope scope, AuthorizationBinding currentAuthorization, MutationIdentity mutation,
        PreparedRevisionSnapshot preparedRevision)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentAuthorization);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(preparedRevision);
        AtomicMutationContractGuards.RequireOperation(scope, ProvisionalMutationOperation.PlanRevisionAppend);
        AtomicMutationContractGuards.RequireGeometryScope(
            preparedRevision.Geometry, scope,
            preparedRevision.LandRevisionId, preparedRevision.RequirementsRevisionId);
        var input = new PlanRevisionAppendFingerprintInput(
            scope.ToFingerprintScope(), preparedRevision.ExpectedBaseRevisionId,
            preparedRevision.Geometry.GeometryHash, preparedRevision.LandRevisionId,
            preparedRevision.RequirementsRevisionId, preparedRevision.Validation.ValidationId,
            preparedRevision.Geometry.SchemaVersion);
        var canonical = MutationFingerprintProfileV1.GetCanonicalBytes(input);
        AtomicMutationContractGuards.RequireCommonBinding(
            scope, currentAuthorization, mutation, ProvisionalMutationOperation.PlanRevisionAppend,
            MutationFingerprintProfileV1.Compute(input));
        return new PlanRevisionAppendMutationRequest(
            scope, currentAuthorization, mutation, preparedRevision.Copy(), canonical);
    }
}

public sealed class FinalPlanConfirmationMutationRequest
{
    private readonly byte[] _canonicalBytes;
    private FinalPlanConfirmationMutationRequest(
        AtomicMutationScope scope, AuthorizationBinding authorization, MutationIdentity mutation,
        PreparedConfirmationSnapshot confirmation, byte[] canonicalBytes)
    {
        Scope = scope; CurrentAuthorization = authorization; Mutation = mutation;
        Confirmation = confirmation; _canonicalBytes = canonicalBytes;
    }

    public AtomicMutationScope Scope { get; }
    public AuthorizationBinding CurrentAuthorization { get; }
    public MutationIdentity Mutation { get; }
    public PreparedConfirmationSnapshot Confirmation { get; }
    public byte[] GetCanonicalFingerprintBytes() => (byte[])_canonicalBytes.Clone();

    public static FinalPlanConfirmationMutationRequest Create(
        AtomicMutationScope scope, AuthorizationBinding currentAuthorization, MutationIdentity mutation,
        PreparedConfirmationSnapshot confirmation)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentAuthorization);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(confirmation);
        AtomicMutationContractGuards.RequireOperation(scope, ProvisionalMutationOperation.FinalPlanConfirmation);
        var input = new FinalPlanConfirmationFingerprintInput(
            scope.ToFingerprintScope(), confirmation.ExactRevisionId, confirmation.GeometryHash,
            confirmation.ValidationEvidenceId, confirmation.LandRevisionId,
            confirmation.RequirementsRevisionId, confirmation.WarningAcknowledged);
        var canonical = MutationFingerprintProfileV1.GetCanonicalBytes(input);
        AtomicMutationContractGuards.RequireCommonBinding(
            scope, currentAuthorization, mutation, ProvisionalMutationOperation.FinalPlanConfirmation,
            MutationFingerprintProfileV1.Compute(input));
        return new FinalPlanConfirmationMutationRequest(
            scope, currentAuthorization, mutation, confirmation.Copy(), canonical);
    }
}

/// <summary>Fresh disclosure authority plus the exact historical mutation authority being reconciled.</summary>
public sealed class AtomicMutationReconciliationRequest
{
    private AtomicMutationReconciliationRequest(
        ReconciliationReference reference, AuthorizationBinding historicalAuthorization)
    {
        Reference = reference;
        HistoricalAuthorization = historicalAuthorization;
    }

    public ReconciliationReference Reference { get; }
    public AuthorizationBinding HistoricalAuthorization { get; }

    public static AtomicMutationReconciliationRequest Create(
        ReconciliationReference reference,
        AuthorizationBinding historicalAuthorization)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(historicalAuthorization);
        if (reference.Authorization.ProjectId != historicalAuthorization.ProjectId)
            throw new ArgumentException(
                "Current and historical authorization must refer to the same project.",
                nameof(historicalAuthorization));
        return new AtomicMutationReconciliationRequest(reference, historicalAuthorization);
    }
}

public sealed class AtomicCandidatePayload
{
    private AtomicCandidatePayload(
        Guid candidateId, Guid projectId, Guid planId, CandidatePreviewStatus state,
        string geometryHash, Guid validationEvidenceId)
    {
        CandidateId = candidateId; ProjectId = projectId; PlanId = planId; State = state;
        GeometryHash = geometryHash; ValidationEvidenceId = validationEvidenceId;
    }

    public Guid CandidateId { get; }
    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public CandidatePreviewStatus State { get; }
    public string GeometryHash { get; }
    public Guid ValidationEvidenceId { get; }

    public static AtomicCandidatePayload Create(
        Guid candidateId, Guid projectId, Guid planId, CandidatePreviewStatus state,
        string geometryHash, Guid validationEvidenceId)
    {
        AtomicMutationContractGuards.RequireId(candidateId, nameof(candidateId));
        AtomicMutationContractGuards.RequireId(projectId, nameof(projectId));
        AtomicMutationContractGuards.RequireId(planId, nameof(planId));
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        AtomicMutationContractGuards.RequireCanonicalHash(geometryHash, nameof(geometryHash));
        AtomicMutationContractGuards.RequireId(validationEvidenceId, nameof(validationEvidenceId));
        return new AtomicCandidatePayload(
            candidateId, projectId, planId, state, geometryHash, validationEvidenceId);
    }
}

public sealed class AtomicPlanRevisionPayload
{
    private AtomicPlanRevisionPayload(
        Guid revisionId, Guid projectId, Guid planId, FrozenGeometrySnapshot geometry,
        Guid validationEvidenceId, Guid landRevisionId, Guid requirementsRevisionId)
    {
        RevisionId = revisionId; ProjectId = projectId; PlanId = planId; Geometry = geometry;
        ValidationEvidenceId = validationEvidenceId; LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
    }

    public Guid RevisionId { get; }
    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public FrozenGeometrySnapshot Geometry { get; }
    public Guid ValidationEvidenceId { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }

    public static AtomicPlanRevisionPayload Create(
        Guid revisionId, Guid projectId, Guid planId, FrozenGeometrySnapshot geometry,
        Guid validationEvidenceId, Guid landRevisionId, Guid requirementsRevisionId)
    {
        AtomicMutationContractGuards.RequireId(revisionId, nameof(revisionId));
        AtomicMutationContractGuards.RequireId(projectId, nameof(projectId));
        AtomicMutationContractGuards.RequireId(planId, nameof(planId));
        ArgumentNullException.ThrowIfNull(geometry);
        AtomicMutationContractGuards.RequireId(validationEvidenceId, nameof(validationEvidenceId));
        AtomicMutationContractGuards.RequireId(landRevisionId, nameof(landRevisionId));
        AtomicMutationContractGuards.RequireId(requirementsRevisionId, nameof(requirementsRevisionId));
        if (geometry.ProjectId != projectId || geometry.PlanId != planId ||
            geometry.LandRevisionId != landRevisionId ||
            geometry.RequirementsRevisionId != requirementsRevisionId)
            throw new ArgumentException(
                "Revision geometry does not match its project, plan and applicable inputs.",
                nameof(geometry));
        return new AtomicPlanRevisionPayload(
            revisionId, projectId, planId, geometry.Copy(), validationEvidenceId,
            landRevisionId, requirementsRevisionId);
    }
}

public sealed class AtomicFinalConfirmationPayload
{
    private AtomicFinalConfirmationPayload(
        Guid confirmationId, Guid projectId, Guid planId, Guid exactRevisionId,
        string geometryHash, Guid validationEvidenceId, Guid confirmedBy, bool warningAcknowledged)
    {
        ConfirmationId = confirmationId; ProjectId = projectId; PlanId = planId;
        ExactRevisionId = exactRevisionId; GeometryHash = geometryHash;
        ValidationEvidenceId = validationEvidenceId; ConfirmedBy = confirmedBy;
        WarningAcknowledged = warningAcknowledged;
    }

    public Guid ConfirmationId { get; }
    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public Guid ExactRevisionId { get; }
    public string GeometryHash { get; }
    public Guid ValidationEvidenceId { get; }
    public Guid ConfirmedBy { get; }
    public bool WarningAcknowledged { get; }

    public static AtomicFinalConfirmationPayload Create(
        Guid confirmationId, Guid projectId, Guid planId, Guid exactRevisionId,
        string geometryHash, Guid validationEvidenceId, Guid confirmedBy, bool warningAcknowledged)
    {
        AtomicMutationContractGuards.RequireId(confirmationId, nameof(confirmationId));
        AtomicMutationContractGuards.RequireId(projectId, nameof(projectId));
        AtomicMutationContractGuards.RequireId(planId, nameof(planId));
        AtomicMutationContractGuards.RequireId(exactRevisionId, nameof(exactRevisionId));
        AtomicMutationContractGuards.RequireCanonicalHash(geometryHash, nameof(geometryHash));
        AtomicMutationContractGuards.RequireId(validationEvidenceId, nameof(validationEvidenceId));
        AtomicMutationContractGuards.RequireId(confirmedBy, nameof(confirmedBy));
        return new AtomicFinalConfirmationPayload(
            confirmationId, projectId, planId, exactRevisionId, geometryHash,
            validationEvidenceId, confirmedBy, warningAcknowledged);
    }
}

public sealed class CandidateCreateMutationResponse
{
    private CandidateCreateMutationResponse(AtomicMutationResult result, PreparedCandidateSnapshot? candidate)
    { Result = result; Candidate = candidate; }
    public AtomicMutationResult Result { get; }
    public PreparedCandidateSnapshot? Candidate { get; }
    public static CandidateCreateMutationResponse Succeeded(CandidateCreateMutationRequest request, AtomicMutationResult result, PreparedCandidateSnapshot candidate) =>
        CreateSuccess(request, result, candidate, AtomicMutationStatus.Succeeded);
    public static CandidateCreateMutationResponse MatchingDuplicate(CandidateCreateMutationRequest request, AtomicMutationResult result, PreparedCandidateSnapshot candidate) =>
        CreateSuccess(request, result, candidate, AtomicMutationStatus.MatchingDuplicate);
    public static CandidateCreateMutationResponse Failed(AtomicMutationResult result) => new(AtomicMutationResponseGuards.Failure(result), null);
    public static CandidateCreateMutationResponse OutcomeUnknown(CandidateCreateMutationRequest request, AtomicMutationResult result) => new(AtomicMutationResponseGuards.Unknown(request.Scope, request.Mutation, result), null);
    private static CandidateCreateMutationResponse CreateSuccess(
        CandidateCreateMutationRequest request, AtomicMutationResult result,
        PreparedCandidateSnapshot candidate, AtomicMutationStatus status)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        AtomicMutationResponseGuards.Success(
            request.Scope, request.Mutation, result, status, candidate.CandidateId);
        AtomicMutationContractGuards.RequireGeometryScope(
            candidate.Geometry, request.Scope, request.LandRevisionId, request.RequirementsRevisionId);
        if (candidate.OwnerId != request.Scope.TrustedActorId ||
            candidate.BaseRevisionId != request.ExpectedBaseRevisionId ||
            candidate.LandRevisionId != request.LandRevisionId ||
            candidate.RequirementsRevisionId != request.RequirementsRevisionId)
            throw new ArgumentException(
                "Provider-derived candidate does not match the exact creation intent.", nameof(candidate));
        return new CandidateCreateMutationResponse(result, candidate.Copy());
    }
}

public sealed class CandidateApplyMutationResponse
{
    private CandidateApplyMutationResponse(
        AtomicMutationResult result, AtomicCandidatePayload? candidate, AtomicPlanRevisionPayload? revision)
    { Result = result; Candidate = candidate; Revision = revision; }
    public AtomicMutationResult Result { get; }
    public AtomicCandidatePayload? Candidate { get; }
    public AtomicPlanRevisionPayload? Revision { get; }
    public static CandidateApplyMutationResponse Succeeded(
        CandidateApplyMutationRequest request, AtomicMutationResult result,
        AtomicCandidatePayload candidate, AtomicPlanRevisionPayload revision) =>
        CreateSuccess(request, result, candidate, revision, AtomicMutationStatus.Succeeded);
    public static CandidateApplyMutationResponse MatchingDuplicate(
        CandidateApplyMutationRequest request, AtomicMutationResult result,
        AtomicCandidatePayload candidate, AtomicPlanRevisionPayload revision) =>
        CreateSuccess(request, result, candidate, revision, AtomicMutationStatus.MatchingDuplicate);
    public static CandidateApplyMutationResponse Failed(AtomicMutationResult result) => new(AtomicMutationResponseGuards.Failure(result), null, null);
    public static CandidateApplyMutationResponse OutcomeUnknown(CandidateApplyMutationRequest request, AtomicMutationResult result) => new(AtomicMutationResponseGuards.Unknown(request.Scope, request.Mutation, result), null, null);
    private static CandidateApplyMutationResponse CreateSuccess(
        CandidateApplyMutationRequest request, AtomicMutationResult result, AtomicCandidatePayload candidate,
        AtomicPlanRevisionPayload revision, AtomicMutationStatus status)
    {
        var scope = request.Scope;
        var boundCandidate = AtomicMutationResponseGuards.Candidate(
            scope, request.Mutation, result, candidate, status, CandidatePreviewStatus.Applied, false);
        AtomicMutationResponseGuards.Success(scope, request.Mutation, result, status, revision.RevisionId);
        AtomicMutationResponseGuards.RequireScope(scope, revision.ProjectId, revision.PlanId);
        var prepared = request.PreparedRevision;
        if (boundCandidate.CandidateId != request.CandidateId ||
            !string.Equals(boundCandidate.GeometryHash, request.CandidateGeometryHash, StringComparison.Ordinal) ||
            boundCandidate.ValidationEvidenceId != request.ValidationEvidenceId ||
            !string.Equals(boundCandidate.GeometryHash, revision.Geometry.GeometryHash, StringComparison.Ordinal) ||
            boundCandidate.ValidationEvidenceId != revision.ValidationEvidenceId ||
            !string.Equals(revision.Geometry.ExactJson, prepared.Geometry.ExactJson, StringComparison.Ordinal) ||
            !string.Equals(revision.Geometry.SchemaVersion, prepared.Geometry.SchemaVersion, StringComparison.Ordinal) ||
            revision.LandRevisionId != prepared.LandRevisionId ||
            revision.RequirementsRevisionId != prepared.RequirementsRevisionId)
            throw new ArgumentException("Applied candidate and revision payloads do not match.", nameof(revision));
        return new CandidateApplyMutationResponse(result, boundCandidate, revision);
    }
}

public sealed class CandidateStateMutationResponse
{
    private CandidateStateMutationResponse(AtomicMutationResult result, AtomicCandidatePayload? candidate)
    { Result = result; Candidate = candidate; }
    public AtomicMutationResult Result { get; }
    public AtomicCandidatePayload? Candidate { get; }
    public static CandidateStateMutationResponse Succeeded(
        CandidateRejectMutationRequest request, AtomicMutationResult result, AtomicCandidatePayload candidate) =>
        new(result, State(request.Scope, request.Mutation, request.CandidateId, result, candidate, AtomicMutationStatus.Succeeded));
    public static CandidateStateMutationResponse Succeeded(
        CandidateExpireMutationRequest request, AtomicMutationResult result, AtomicCandidatePayload candidate) =>
        new(result, State(request.Scope, request.Mutation, request.CandidateId, result, candidate, AtomicMutationStatus.Succeeded));
    public static CandidateStateMutationResponse MatchingDuplicate(
        CandidateRejectMutationRequest request, AtomicMutationResult result, AtomicCandidatePayload candidate) =>
        new(result, State(request.Scope, request.Mutation, request.CandidateId, result, candidate, AtomicMutationStatus.MatchingDuplicate));
    public static CandidateStateMutationResponse MatchingDuplicate(
        CandidateExpireMutationRequest request, AtomicMutationResult result, AtomicCandidatePayload candidate) =>
        new(result, State(request.Scope, request.Mutation, request.CandidateId, result, candidate, AtomicMutationStatus.MatchingDuplicate));
    public static CandidateStateMutationResponse Failed(AtomicMutationResult result) => new(AtomicMutationResponseGuards.Failure(result), null);
    public static CandidateStateMutationResponse OutcomeUnknown(CandidateRejectMutationRequest request, AtomicMutationResult result) => new(AtomicMutationResponseGuards.Unknown(request.Scope, request.Mutation, result), null);
    public static CandidateStateMutationResponse OutcomeUnknown(CandidateExpireMutationRequest request, AtomicMutationResult result) => new(AtomicMutationResponseGuards.Unknown(request.Scope, request.Mutation, result), null);
    private static AtomicCandidatePayload State(
        AtomicMutationScope scope, MutationIdentity expectedMutation, Guid expectedCandidateId,
        AtomicMutationResult result, AtomicCandidatePayload candidate,
        AtomicMutationStatus status)
    {
        var expected = scope.Operation switch
        {
            ProvisionalMutationOperation.CandidateReject => CandidatePreviewStatus.Rejected,
            ProvisionalMutationOperation.CandidateExpire => CandidatePreviewStatus.Expired,
            _ => throw new ArgumentException("A candidate-state response requires Reject or Expire scope.", nameof(scope))
        };
        var bound = AtomicMutationResponseGuards.Candidate(
            scope, expectedMutation, result, candidate, status, expected, true);
        if (bound.CandidateId != expectedCandidateId)
            throw new ArgumentException("Candidate payload does not match the requested candidate.", nameof(candidate));
        return bound;
    }
}

public sealed class PlanRevisionMutationResponse
{
    private PlanRevisionMutationResponse(AtomicMutationResult result, AtomicPlanRevisionPayload? revision)
    { Result = result; Revision = revision; }
    public AtomicMutationResult Result { get; }
    public AtomicPlanRevisionPayload? Revision { get; }
    public static PlanRevisionMutationResponse Succeeded(PlanRevisionAppendMutationRequest request, AtomicMutationResult result, AtomicPlanRevisionPayload revision) =>
        new(result, Success(request, result, revision, AtomicMutationStatus.Succeeded));
    public static PlanRevisionMutationResponse MatchingDuplicate(PlanRevisionAppendMutationRequest request, AtomicMutationResult result, AtomicPlanRevisionPayload revision) =>
        new(result, Success(request, result, revision, AtomicMutationStatus.MatchingDuplicate));
    public static PlanRevisionMutationResponse Failed(AtomicMutationResult result) => new(AtomicMutationResponseGuards.Failure(result), null);
    public static PlanRevisionMutationResponse OutcomeUnknown(PlanRevisionAppendMutationRequest request, AtomicMutationResult result) => new(AtomicMutationResponseGuards.Unknown(request.Scope, request.Mutation, result), null);
    private static AtomicPlanRevisionPayload Success(
        PlanRevisionAppendMutationRequest request, AtomicMutationResult result, AtomicPlanRevisionPayload revision,
        AtomicMutationStatus status)
    {
        var scope = request.Scope;
        AtomicMutationResponseGuards.Success(scope, request.Mutation, result, status, revision.RevisionId);
        AtomicMutationResponseGuards.RequireScope(scope, revision.ProjectId, revision.PlanId);
        var prepared = request.PreparedRevision;
        if (!string.Equals(revision.Geometry.ExactJson, prepared.Geometry.ExactJson, StringComparison.Ordinal) ||
            !string.Equals(revision.Geometry.SchemaVersion, prepared.Geometry.SchemaVersion, StringComparison.Ordinal) ||
            revision.ValidationEvidenceId != prepared.Validation.ValidationId ||
            revision.LandRevisionId != prepared.LandRevisionId ||
            revision.RequirementsRevisionId != prepared.RequirementsRevisionId)
            throw new ArgumentException("Revision payload does not match the frozen prepared revision.", nameof(revision));
        return revision;
    }
}

public sealed class FinalConfirmationMutationResponse
{
    private FinalConfirmationMutationResponse(AtomicMutationResult result, AtomicFinalConfirmationPayload? confirmation)
    { Result = result; Confirmation = confirmation; }
    public AtomicMutationResult Result { get; }
    public AtomicFinalConfirmationPayload? Confirmation { get; }
    public static FinalConfirmationMutationResponse Succeeded(FinalPlanConfirmationMutationRequest request, AtomicMutationResult result, AtomicFinalConfirmationPayload confirmation) =>
        new(result, Success(request, result, confirmation, AtomicMutationStatus.Succeeded));
    public static FinalConfirmationMutationResponse MatchingDuplicate(FinalPlanConfirmationMutationRequest request, AtomicMutationResult result, AtomicFinalConfirmationPayload confirmation) =>
        new(result, Success(request, result, confirmation, AtomicMutationStatus.MatchingDuplicate));
    public static FinalConfirmationMutationResponse Failed(AtomicMutationResult result) => new(AtomicMutationResponseGuards.Failure(result), null);
    public static FinalConfirmationMutationResponse OutcomeUnknown(FinalPlanConfirmationMutationRequest request, AtomicMutationResult result) => new(AtomicMutationResponseGuards.Unknown(request.Scope, request.Mutation, result), null);
    private static AtomicFinalConfirmationPayload Success(
        FinalPlanConfirmationMutationRequest request, AtomicMutationResult result, AtomicFinalConfirmationPayload confirmation,
        AtomicMutationStatus status)
    {
        var scope = request.Scope;
        AtomicMutationResponseGuards.Success(scope, request.Mutation, result, status, confirmation.ConfirmationId);
        AtomicMutationResponseGuards.RequireScope(scope, confirmation.ProjectId, confirmation.PlanId);
        var prepared = request.Confirmation;
        if (confirmation.ConfirmedBy != scope.TrustedActorId ||
            confirmation.ExactRevisionId != prepared.ExactRevisionId ||
            !string.Equals(confirmation.GeometryHash, prepared.GeometryHash, StringComparison.Ordinal) ||
            confirmation.ValidationEvidenceId != prepared.ValidationEvidenceId ||
            confirmation.WarningAcknowledged != prepared.WarningAcknowledged)
            throw new ArgumentException("Confirmation payload does not match the frozen request.", nameof(confirmation));
        return confirmation;
    }
}

/// <summary>
/// Unregistered database-free F02 port. Implementations must preserve cancellation and uncertainty;
/// this interface supplies no persistence, retry, locking, transaction or authorization implementation.
/// </summary>
public interface IAtomicPlanMutationPort
{
    Task<CandidateCreateMutationResponse> CreateCandidateAsync(
        CandidateCreateMutationRequest request, CancellationToken cancellationToken);
    Task<CandidateApplyMutationResponse> ApplyCandidateAsync(
        CandidateApplyMutationRequest request, CancellationToken cancellationToken);
    Task<CandidateStateMutationResponse> RejectCandidateAsync(
        CandidateRejectMutationRequest request, CancellationToken cancellationToken);
    Task<CandidateStateMutationResponse> ExpireCandidateAsUserAsync(
        CandidateExpireMutationRequest request, CancellationToken cancellationToken);
    Task<PlanRevisionMutationResponse> AppendPlanRevisionAsync(
        PlanRevisionAppendMutationRequest request, CancellationToken cancellationToken);
    Task<FinalConfirmationMutationResponse> ConfirmFinalPlanAsync(
        FinalPlanConfirmationMutationRequest request, CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(
        AtomicMutationReconciliationRequest request, CancellationToken cancellationToken);
}

/// <summary>Database-independent boundary helper; persistence must make the authoritative clock decision.</summary>
public static class CandidateExpiryBoundary
{
    public static bool CanApplyOrReject(DateTimeOffset authoritativeUtcNow, DateTimeOffset expiresAtUtc)
    {
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(authoritativeUtcNow, nameof(authoritativeUtcNow));
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(expiresAtUtc, nameof(expiresAtUtc));
        return authoritativeUtcNow < expiresAtUtc;
    }

    public static bool CanExpire(DateTimeOffset authoritativeUtcNow, DateTimeOffset expiresAtUtc)
    {
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(authoritativeUtcNow, nameof(authoritativeUtcNow));
        AtomicMutationContractGuards.RequireWholeUtcMicroseconds(expiresAtUtc, nameof(expiresAtUtc));
        return authoritativeUtcNow >= expiresAtUtc;
    }
}

internal static class AtomicMutationResponseGuards
{
    public static AtomicMutationResult Success(
        AtomicMutationScope scope, MutationIdentity expectedMutation, AtomicMutationResult result,
        AtomicMutationStatus expectedStatus, Guid expectedDurableOutcomeId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != expectedStatus)
            throw new ArgumentException("The result status does not match the response factory.", nameof(result));
        if (AtomicMutationResultValidator.Validate(
                result, scope.PlanId,
                new AuthorizationBinding(scope.ProjectId, scope.TrustedActorId, scope.AuthorizationVersion),
                expectedMutation) !=
            AtomicMutationResultValidationStatus.Valid)
            throw new ArgumentException("Atomic result evidence does not match its scope.", nameof(result));
        if (result.Evidence!.Mutation.OperationId != scope.OperationId ||
            result.Evidence.Mutation.Operation != scope.Operation ||
            result.Evidence.Mutation.FingerprintProfileVersion != MutationFingerprintProfileV1.ProfileNumber)
            throw new ArgumentException("Atomic result mutation identity does not match its scope.", nameof(result));
        if (result.Evidence.DurableOutcomeId != expectedDurableOutcomeId)
            throw new ArgumentException("Durable outcome identity does not match the protected payload.", nameof(expectedDurableOutcomeId));
        return result;
    }

    public static AtomicCandidatePayload Candidate(
        AtomicMutationScope scope, MutationIdentity expectedMutation, AtomicMutationResult result, AtomicCandidatePayload candidate,
        AtomicMutationStatus status, CandidatePreviewStatus expectedState, bool candidateIsDurableOutcome)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        RequireScope(scope, candidate.ProjectId, candidate.PlanId);
        if (candidate.State != expectedState)
            throw new ArgumentException("Candidate state does not match the operation.", nameof(candidate));
        Success(scope, expectedMutation, result, status,
            candidateIsDurableOutcome ? candidate.CandidateId : result.Evidence!.DurableOutcomeId);
        return candidate;
    }

    public static AtomicMutationResult Failure(AtomicMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate or
            AtomicMutationStatus.TransactionOutcomeUnknown || result.Evidence is not null || result.Reconciliation is not null)
            throw new ArgumentException("A definite failure cannot contain success or reconciliation evidence.", nameof(result));
        return result;
    }

    public static AtomicMutationResult Unknown(AtomicMutationScope scope, MutationIdentity expectedMutation, AtomicMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != AtomicMutationStatus.TransactionOutcomeUnknown ||
            result.Evidence is not null || result.Reconciliation is null || result.AllowsAutomaticRetry)
            throw new ArgumentException("An unknown outcome requires only a non-retryable reconciliation reference.", nameof(result));
        var expected = new ReconciliationReference(
            scope.PlanId,
            new AuthorizationBinding(scope.ProjectId, scope.TrustedActorId, scope.AuthorizationVersion),
            expectedMutation);
        if (result.Reconciliation.Mutation.OperationId != scope.OperationId ||
            result.Reconciliation.Mutation.Operation != scope.Operation ||
            result.Reconciliation.Mutation.FingerprintProfileVersion != MutationFingerprintProfileV1.ProfileNumber ||
            AtomicMutationResultValidator.Validate(result, scope.PlanId, expected.Authorization, expected.Mutation) !=
                AtomicMutationResultValidationStatus.Valid)
            throw new ArgumentException("Reconciliation reference does not match the exact mutation scope.", nameof(result));
        return result;
    }

    public static void RequireScope(AtomicMutationScope scope, Guid projectId, Guid planId)
    {
        if (projectId != scope.ProjectId || planId != scope.PlanId)
            throw new ArgumentException("Protected payload does not match the project and plan scope.");
    }
}

internal static class AtomicMutationContractGuards
{
    /// <summary>
    /// Approved L-01 limits. Geometry is measured as strict UTF-8 bytes. Text lengths are
    /// measured as Unicode scalar values; malformed UTF-16 is rejected rather than replaced.
    /// </summary>
    internal const int MaxGeometryJsonUtf8Bytes = 4 * 1024 * 1024;
    internal const int MaxValidationIssueCodes = 1_000;
    internal const int MaxIssueCodeCharacters = 64;
    internal const int MaxValidatorVersionCharacters = 64;
    internal const int MaxRegulatoryEvidenceStatusCharacters = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly HashSet<string> KnownRegulatoryEvidenceStatuses = new(StringComparer.Ordinal)
    {
        "VERIFIED",
        "NOT_VERIFIED",
        "MISSING",
        "UNVERIFIED",
        "CONFLICTING",
        "REVIEWED",
    };

    public static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("A non-empty identity is required.", name);
    }

    public static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", name);
    }

    public static void RequireBoundedText(string? value, int maximumCharacters, string name)
    {
        RequireText(value, name);

        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out _, out var consumed);
            if (status != OperationStatus.Done)
                throw new ArgumentException("Well-formed Unicode text is required.", name);

            count++;
            if (count > maximumCharacters)
                throw new ArgumentException(
                    $"The value cannot exceed {maximumCharacters} Unicode scalar values.", name);
            remaining = remaining[consumed..];
        }
    }

    public static void RequireGeometryJsonSize(string value, string name)
    {
        // Every well-formed UTF-16 code unit contributes at least one UTF-8 byte, so this
        // inexpensive check rejects obvious oversize input before full encoding validation.
        if (value.Length > MaxGeometryJsonUtf8Bytes)
            throw new ArgumentException(
                $"Geometry JSON cannot exceed {MaxGeometryJsonUtf8Bytes} UTF-8 bytes.", name);

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Geometry JSON must contain well-formed Unicode.", name, exception);
        }

        if (byteCount > MaxGeometryJsonUtf8Bytes)
            throw new ArgumentException(
                $"Geometry JSON cannot exceed {MaxGeometryJsonUtf8Bytes} UTF-8 bytes.", name);
    }

    public static string[] CopyBoundedIssueCodes(IEnumerable<string>? issueCodes, string name)
    {
        if (issueCodes is null) return [];

        var copied = new List<string>();
        foreach (var code in issueCodes)
        {
            if (copied.Count == MaxValidationIssueCodes)
                throw new ArgumentException(
                    $"Validation evidence cannot contain more than {MaxValidationIssueCodes} issue codes.",
                    name);
            RequireBoundedText(code, MaxIssueCodeCharacters, name);
            copied.Add(code);
        }
        return copied.ToArray();
    }

    public static void RequireKnownRegulatoryEvidenceStatus(string? value, string name)
    {
        RequireBoundedText(value, MaxRegulatoryEvidenceStatusCharacters, name);
        if (!KnownRegulatoryEvidenceStatuses.Contains(value!))
            throw new ArgumentException("An approved regulatory evidence status is required.", name);
    }

    public static void RequireCanonicalHash(string? value, string name)
    {
        const string prefix = "sha256:";
        if (value is null || value.Length != 71 || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            value.AsSpan(prefix.Length).IndexOfAnyExcept("0123456789abcdef") >= 0)
            throw new ArgumentException("A canonical lowercase SHA-256 digest is required.", name);
    }

    public static void RequireWholeUtcMicroseconds(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("A UTC timestamp is required.", name);
        if (value.UtcDateTime.Ticks % 10 != 0)
            throw new ArgumentException("A whole-microsecond timestamp is required.", name);
    }

    public static void RequireOperation(AtomicMutationScope scope, ProvisionalMutationOperation operation)
    {
        if (scope.Operation != operation)
            throw new ArgumentException($"This request requires operation {operation}.", nameof(scope));
    }

    public static void RequireCommonBinding(
        AtomicMutationScope scope, AuthorizationBinding authorization, MutationIdentity mutation,
        ProvisionalMutationOperation operation, CommandFingerprint recomputedFingerprint)
    {
        RequireOperation(scope, operation);
        if (AuthorizationBindingValidator.Validate(
                authorization, scope.ProjectId, scope.TrustedActorId, scope.AuthorizationVersion) !=
            AuthorizationBindingValidationStatus.Valid)
            throw new ArgumentException("Authorization evidence does not match the exact mutation scope.", nameof(authorization));
        if (mutation.OperationId != scope.OperationId || mutation.Operation != scope.Operation ||
            mutation.FingerprintProfileVersion != MutationFingerprintProfileV1.ProfileNumber ||
            !mutation.Fingerprint.Matches(recomputedFingerprint))
            throw new ArgumentException("Mutation identity does not match the typed V1 fingerprint.", nameof(mutation));
    }

    public static void RequireValidationBinding(
        FrozenGeometrySnapshot geometry, FrozenValidationEvidence validation,
        Guid landRevisionId, Guid requirementsRevisionId)
    {
        if (geometry.LandRevisionId != landRevisionId ||
            geometry.RequirementsRevisionId != requirementsRevisionId ||
            !string.Equals(geometry.GeometryHash, validation.GeometryHash, StringComparison.Ordinal) ||
            validation.LandRevisionId != landRevisionId ||
            validation.RequirementsRevisionId != requirementsRevisionId)
            throw new ArgumentException("Validation evidence does not match geometry and applicable inputs.", nameof(validation));
    }

    public static void RequireMutationSafeValidation(FrozenValidationEvidence validation)
    {
        if (!validation.GeometryValid || validation.HasBlockingIssues)
            throw new ArgumentException(
                "Mutation-bound validation must confirm valid geometry without blocking issues.",
                nameof(validation));
    }

    public static void RequireGeometryScope(
        FrozenGeometrySnapshot geometry,
        AtomicMutationScope scope,
        Guid landRevisionId,
        Guid requirementsRevisionId)
    {
        if (geometry.ProjectId != scope.ProjectId || geometry.PlanId != scope.PlanId ||
            geometry.LandRevisionId != landRevisionId ||
            geometry.RequirementsRevisionId != requirementsRevisionId)
            throw new ArgumentException(
                "Geometry does not match the exact project, plan and applicable inputs.",
                nameof(geometry));
    }

    public static void ValidateStrictJson(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Geometry JSON must have an object root.", name);
            ValidateElement(document.RootElement, name);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Strict JSON is required.", name, exception);
        }
    }

    private static void ValidateElement(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ArgumentException("Duplicate JSON property names are not allowed.", name);
                ValidateElement(property.Value, name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) ValidateElement(item, name);
        }
        else if (element.ValueKind == JsonValueKind.Number &&
                 !element.TryGetInt64(out _) && !element.TryGetDecimal(out _) && !element.TryGetDouble(out _))
        {
            throw new ArgumentException("Invalid JSON number.", name);
        }
    }
}
