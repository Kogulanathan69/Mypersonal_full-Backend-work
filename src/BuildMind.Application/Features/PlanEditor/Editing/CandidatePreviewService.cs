using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Editing;

public sealed class CandidatePreviewService(
    IProjectOwnershipGate ownership,
    IPlanRevisionStore revisions,
    IApplicableInputRevisionSource currentInputs,
    IConfirmedLandRevisionPort landRevisions,
    IAuthorizedRequirementRevisionPort requirementRevisions,
    IPlanEditorComputeClient computeClient,
    ICandidateValidationEvidenceSource validationEvidenceSource,
    IPlanValidationEvidenceVerifier validationVerifier,
    ICandidatePreviewStore candidates,
    ICandidatePreviewTransaction transaction,
    TimeProvider timeProvider,
    TimeSpan candidateLifetime)
{
    public async Task<CandidatePreviewResult> PreviewAsync(
        CreateCandidatePreviewCommand command,
        Guid authenticatedOwnerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authenticatedOwnerId == Guid.Empty)
            return new(CandidatePreviewResultStatus.Unauthorized);
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedOwnerId, cancellationToken))
                return new(CandidatePreviewResultStatus.Unauthorized);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidatePreviewResultStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(CandidatePreviewResultStatus.DependencyUnavailable); }
        catch (Exception) { return new(CandidatePreviewResultStatus.UnexpectedInternalFailure); }
        EnsureConfiguredLifetime();

        PlanRevisionSnapshot? current;
        ApplicableInputRevisions? applicable;
        ConfirmedLandRevision? land;
        RequirementRevisionAccessResult? requirementAccess;
        try
        {
            current = await revisions.GetCurrentAsync(command.ProjectId, command.PlanId, cancellationToken);
            if (current is null) return new(CandidatePreviewResultStatus.RevisionNotFound);
            applicable = await currentInputs.GetCurrentAsync(command.ProjectId, cancellationToken);
            if (applicable is null || applicable.LandRevisionId == Guid.Empty ||
                applicable.RequirementsRevisionId == Guid.Empty)
                return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
            land = await landRevisions.GetAsync(command.ProjectId, applicable.LandRevisionId, cancellationToken);
            requirementAccess = await requirementRevisions.GetAsync(
                command.ProjectId, applicable.RequirementsRevisionId, authenticatedOwnerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidatePreviewResultStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(CandidatePreviewResultStatus.DependencyUnavailable); }
        catch (InvalidDependencyResponseException)
        { return new(CandidatePreviewResultStatus.InvalidDependencyResponse); }
        catch (Exception) { return new(CandidatePreviewResultStatus.UnexpectedInternalFailure); }

        if (requirementAccess is null)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (requirementAccess.Status == RequirementRevisionAccessStatus.Unauthorized)
            return new(CandidatePreviewResultStatus.Unauthorized);
        if (requirementAccess.Status == RequirementRevisionAccessStatus.InvalidDependencyResponse)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (!AuthorizationMatches(
                requirementAccess.Authorization, command.ProjectId, authenticatedOwnerId))
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (requirementAccess.Status == RequirementRevisionAccessStatus.NotFound)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (requirementAccess.Status != RequirementRevisionAccessStatus.Found ||
            requirementAccess.Revision is not { } requirements ||
            requirements.ProjectId != command.ProjectId ||
            requirements.RevisionId != applicable.RequirementsRevisionId)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);

        if (current is null) return new(CandidatePreviewResultStatus.RevisionNotFound);
        if (current.ProjectId != command.ProjectId || current.PlanId != command.PlanId)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (!StoredRevisionIsValid(current, command.ProjectId, command.PlanId))
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (current.RevisionId != command.BaseRevisionId)
            return new(CandidatePreviewResultStatus.StaleBaseRevision);

        if (current.LandRevisionId != applicable.LandRevisionId ||
            current.RequirementsRevisionId != applicable.RequirementsRevisionId)
            return new(CandidatePreviewResultStatus.StaleInputs);
        if (land is null)
            return new(CandidatePreviewResultStatus.DependencyUnavailable);
        if (land.ProjectId != command.ProjectId || requirements.ProjectId != command.ProjectId ||
            land.RevisionId != applicable.LandRevisionId || requirements.RevisionId != applicable.RequirementsRevisionId)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);

        try
        {
            PlanEditorContractValidator.Validate(new PlanEditRequestDto
            {
                SchemaVersion = current.GeometrySchemaVersion,
                ProjectId = command.ProjectId,
                PlanId = command.PlanId,
                BaseRevisionId = command.BaseRevisionId,
                Command = command.Command
            });
        }
        catch (ArgumentException)
        {
            return new(CandidatePreviewResultStatus.InvalidCommand);
        }

        PlanGeometryDto sourceGeometry;
        try
        {
            sourceGeometry = JsonSerializer.Deserialize<PlanGeometryDto>(
                current.GeometryJson, PlanEditorJson.StrictOptions)
                ?? throw new JsonException("Current geometry snapshot was null.");
            PlanEditorContractValidator.Validate(sourceGeometry);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        }
        var sourceHash = PlanRevisionService.ComputeGeometryHash(current.GeometryJson);
        if (sourceGeometry.ProjectId != current.ProjectId ||
            sourceGeometry.PlanId != current.PlanId ||
            sourceGeometry.LandRevisionId != current.LandRevisionId ||
            sourceGeometry.RequirementsRevisionId != current.RequirementsRevisionId ||
            sourceGeometry.SchemaVersion != current.GeometrySchemaVersion ||
            current.RevisionId == Guid.Empty || current.VersionNumber <= 0 ||
            current.Validation is null || current.Validation.ValidationId == Guid.Empty ||
            !string.Equals(sourceHash, current.GeometryHash, StringComparison.Ordinal) ||
            !string.Equals(current.Validation.GeometryHash, current.GeometryHash, StringComparison.Ordinal))
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        PlanEditResultDto computed;
        try
        {
            computed = await computeClient.PreviewAsync(
                new PlanEditorComputeRequest(sourceGeometry, command.Command, command.CorrelationId), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return new(CandidatePreviewResultStatus.DependencyTimeout);
        }
        catch (HttpRequestException)
        {
            return new(CandidatePreviewResultStatus.DependencyUnavailable);
        }
        catch (JsonException)
        {
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        }
        catch (Exception)
        {
            return new(CandidatePreviewResultStatus.UnexpectedInternalFailure);
        }
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            PlanEditorContractValidator.Validate(computed);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        }
        if (computed.CommandId != command.Command.CommandId || computed.BaseRevisionId != command.BaseRevisionId)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (computed.Diagnostics is null || computed.ChangedEntityIds is null)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (computed.Status is not (PlanEditStatus.Accepted or PlanEditStatus.AcceptedWithWarnings))
        {
            if (computed.CandidateGeometry is not null)
                return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
            return new(CandidatePreviewResultStatus.CandidateRejected, Diagnostics: computed.Diagnostics);
        }
        if (computed.CandidateGeometry is null)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (computed.CandidateGeometry.ProjectId != command.ProjectId ||
            computed.CandidateGeometry.PlanId != command.PlanId ||
            computed.CandidateGeometry.LandRevisionId != applicable.LandRevisionId ||
            computed.CandidateGeometry.RequirementsRevisionId != applicable.RequirementsRevisionId ||
            computed.CandidateGeometry.SchemaVersion != current.GeometrySchemaVersion)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);

        var geometryJson = JsonSerializer.Serialize(computed.CandidateGeometry, PlanEditorJson.StrictOptions);
        var geometryHash = PlanRevisionService.ComputeGeometryHash(geometryJson);
        PlanValidationReferenceDto? evidence;
        try
        {
            evidence = await validationEvidenceSource.GetAsync(
                command.ProjectId, command.PlanId, geometryHash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidatePreviewResultStatus.DependencyTimeout); }
        catch (HttpRequestException)
        { return new(CandidatePreviewResultStatus.DependencyUnavailable); }
        catch (Exception)
        { return new(CandidatePreviewResultStatus.UnexpectedInternalFailure); }
        if (evidence is null) return new(CandidatePreviewResultStatus.ValidationUnavailable);
        if (evidence.ValidationId == Guid.Empty)
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (!string.Equals(evidence.GeometryHash, geometryHash, StringComparison.Ordinal))
            return new(CandidatePreviewResultStatus.ValidationRejected);
        ValidationEvidenceStatus validationStatus;
        try
        {
            validationStatus = await validationVerifier.VerifyAsync(
                command.ProjectId, command.PlanId, evidence, geometryHash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidatePreviewResultStatus.DependencyTimeout); }
        catch (HttpRequestException)
        { return new(CandidatePreviewResultStatus.DependencyUnavailable); }
        catch (Exception)
        { return new(CandidatePreviewResultStatus.UnexpectedInternalFailure); }
        if (validationStatus == ValidationEvidenceStatus.Unavailable)
            return new(CandidatePreviewResultStatus.ValidationUnavailable);
        if (!Enum.IsDefined(validationStatus))
            return new(CandidatePreviewResultStatus.InvalidDependencyResponse);
        if (validationStatus != ValidationEvidenceStatus.Verified || !evidence.GeometryValid || evidence.HasBlockingIssues)
            return new(CandidatePreviewResultStatus.ValidationRejected);

        // This application-level recheck narrows the race window. M4 must still verify
        // the same context atomically with candidate creation in production persistence.
        var contextFailure = await RevalidatePreviewContextAsync(
            command, authenticatedOwnerId, current, applicable, cancellationToken);
        if (contextFailure is not null) return new(contextFailure.Value);

        var now = timeProvider.GetUtcNow();
        var candidate = new CandidatePreviewSnapshot(
            Guid.NewGuid(), command.ProjectId, command.PlanId, authenticatedOwnerId,
            command.BaseRevisionId, applicable.LandRevisionId, applicable.RequirementsRevisionId,
            command.Command.CommandId, geometryJson, geometryHash, computed.CandidateGeometry.SchemaVersion,
            evidence, now, now.Add(candidateLifetime), CandidatePreviewStatus.Pending);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await candidates.CreateAsync(candidate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(CandidatePreviewResultStatus.PersistenceOutcomeUnknown, candidate);
        }
        return new(CandidatePreviewResultStatus.Created, candidate, computed.Diagnostics);
    }

    public async Task<CandidateActionResult> ApplyAsync(
        ApplyCandidateCommand command,
        Guid authenticatedOwnerId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await GetAuthorizedCandidateAsync(
            command.CandidateId, command.ProjectId, command.PlanId, authenticatedOwnerId, cancellationToken);
        if (candidate.Result is not null) return candidate.Result;
        var snapshot = candidate.Snapshot!;
        var now = timeProvider.GetUtcNow();
        var terminal = await ResolveTerminalAsync(
            snapshot, now, authenticatedOwnerId, cancellationToken);
        if (terminal is not null) return terminal;

        PlanRevisionSnapshot? current;
        ApplicableInputRevisions? applicable;
        try
        {
            current = await revisions.GetCurrentAsync(command.ProjectId, command.PlanId, cancellationToken);
            applicable = await currentInputs.GetCurrentAsync(command.ProjectId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidateActionStatus.DependencyTimeout, snapshot); }
        catch (HttpRequestException)
        { return new(CandidateActionStatus.DependencyUnavailable, snapshot); }
        catch (Exception)
        { return new(CandidateActionStatus.UnexpectedInternalFailure, snapshot); }
        if (current is null)
            return new(CandidateActionStatus.StaleBaseRevision, snapshot);
        if (current.ProjectId != command.ProjectId || current.PlanId != command.PlanId)
            return new(CandidateActionStatus.InvalidDependencyResponse, snapshot);
        if (!StoredRevisionIsValid(current, command.ProjectId, command.PlanId))
            return new(CandidateActionStatus.InvalidDependencyResponse, snapshot);
        if (current.RevisionId != snapshot.BaseRevisionId)
            return new(CandidateActionStatus.StaleBaseRevision, snapshot);
        if (current.LandRevisionId != snapshot.LandRevisionId ||
            current.RequirementsRevisionId != snapshot.RequirementsRevisionId)
            return new(CandidateActionStatus.StaleInputs, snapshot);
        if (current.GeometrySchemaVersion != snapshot.GeometrySchemaVersion)
            return new(CandidateActionStatus.InvalidDependencyResponse, snapshot);
        if (applicable is null || applicable.LandRevisionId == Guid.Empty ||
            applicable.RequirementsRevisionId == Guid.Empty)
            return new(CandidateActionStatus.InvalidDependencyResponse, snapshot);
        if (applicable.LandRevisionId != snapshot.LandRevisionId ||
            applicable.RequirementsRevisionId != snapshot.RequirementsRevisionId)
            return new(CandidateActionStatus.StaleInputs, snapshot);

        PlanGeometryDto geometry;
        try
        {
            geometry = JsonSerializer.Deserialize<PlanGeometryDto>(snapshot.GeometryJson, PlanEditorJson.StrictOptions)
                ?? throw new JsonException("Candidate geometry was null.");
            PlanEditorContractValidator.Validate(geometry);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new(CandidateActionStatus.GeometryTampered, snapshot);
        }
        var exactHash = PlanRevisionService.ComputeGeometryHash(
            snapshot.GeometryJson);
        if (!string.Equals(exactHash, snapshot.GeometryHash, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Validation.GeometryHash, exactHash, StringComparison.Ordinal))
            return new(CandidateActionStatus.GeometryTampered, snapshot);
        if (geometry.ProjectId != snapshot.ProjectId ||
            geometry.PlanId != snapshot.PlanId ||
            geometry.LandRevisionId != snapshot.LandRevisionId ||
            geometry.RequirementsRevisionId != snapshot.RequirementsRevisionId ||
            geometry.SchemaVersion != snapshot.GeometrySchemaVersion)
            return new(CandidateActionStatus.GeometryTampered, snapshot);

        ValidationEvidenceStatus validationStatus;
        try
        {
            validationStatus = await validationVerifier.VerifyAsync(
                snapshot.ProjectId, snapshot.PlanId, snapshot.Validation, exactHash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidateActionStatus.DependencyTimeout, snapshot); }
        catch (HttpRequestException)
        { return new(CandidateActionStatus.DependencyUnavailable, snapshot); }
        catch (Exception)
        { return new(CandidateActionStatus.UnexpectedInternalFailure, snapshot); }
        if (validationStatus == ValidationEvidenceStatus.Unavailable)
            return new(CandidateActionStatus.ValidationUnavailable, snapshot);
        if (!Enum.IsDefined(validationStatus))
            return new(CandidateActionStatus.InvalidDependencyResponse, snapshot);
        if (validationStatus != ValidationEvidenceStatus.Verified)
            return new(CandidateActionStatus.ValidationRejected, snapshot);
        if (!snapshot.Validation.GeometryValid || snapshot.Validation.HasBlockingIssues)
            return new(CandidateActionStatus.BlockingIssues, snapshot);
        var hasWarnings = snapshot.Validation.RequirementsMatch is not true ||
            !string.Equals(snapshot.Validation.RegulatoryEvidenceStatus, "VERIFIED", StringComparison.OrdinalIgnoreCase);
        if (hasWarnings && !command.AcknowledgedWarnings)
            return new(CandidateActionStatus.WarningAcknowledgementRequired, snapshot);

        var finalContextFailure = await RevalidateApplyContextAsync(
            snapshot, current, authenticatedOwnerId, cancellationToken);
        if (finalContextFailure is not null)
            return finalContextFailure == CandidateActionStatus.Unauthorized
                ? new(CandidateActionStatus.NotFound)
                : new(finalContextFailure.Value, snapshot);

        var revision = new PlanRevisionSnapshot(
            Guid.NewGuid(), snapshot.PlanId, snapshot.ProjectId, snapshot.BaseRevisionId,
            checked(current.VersionNumber + 1), snapshot.GeometrySchemaVersion, snapshot.GeometryJson,
            snapshot.GeometryHash, snapshot.LandRevisionId, snapshot.RequirementsRevisionId,
            snapshot.Validation, snapshot.CommandId, authenticatedOwnerId, now, command.ChangeSummary);
        try
        {
            var result = await transaction.TryApplyAtomicallyAsync(
                snapshot, revision, now, cancellationToken);
            return ValidateApplyResult(snapshot, revision, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(CandidateActionStatus.TransactionOutcomeUnknown, snapshot);
        }
    }

    public async Task<CandidateActionResult> RejectAsync(
        CandidateActionCommand command,
        Guid authenticatedOwnerId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await GetAuthorizedCandidateAsync(
            command.CandidateId, command.ProjectId, command.PlanId, authenticatedOwnerId, cancellationToken);
        if (candidate.Result is not null) return candidate.Result;
        var snapshot = candidate.Snapshot!;
        var now = timeProvider.GetUtcNow();
        var terminal = await ResolveTerminalAsync(
            snapshot, now, authenticatedOwnerId, cancellationToken);
        if (terminal is not null) return terminal;
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedOwnerId, cancellationToken))
                return new(CandidateActionStatus.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidateActionStatus.DependencyTimeout, snapshot); }
        catch (HttpRequestException)
        { return new(CandidateActionStatus.DependencyUnavailable, snapshot); }
        catch (Exception)
        { return new(CandidateActionStatus.UnexpectedInternalFailure, snapshot); }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await transaction.TryRejectAtomicallyAsync(
                snapshot, now, cancellationToken);
            return ValidateRejectResult(snapshot, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(CandidateActionStatus.TransactionOutcomeUnknown, snapshot);
        }
    }

    private async Task<(CandidatePreviewSnapshot? Snapshot, CandidateActionResult? Result)> GetAuthorizedCandidateAsync(
        Guid candidateId, Guid projectId, Guid planId, Guid authenticatedOwnerId,
        CancellationToken cancellationToken)
    {
        if (authenticatedOwnerId == Guid.Empty)
            return (null, new(CandidateActionStatus.Unauthorized));
        try
        {
            if (!await ownership.OwnsProjectAsync(projectId, authenticatedOwnerId, cancellationToken))
                return (null, new(CandidateActionStatus.Unauthorized));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return (null, new(CandidateActionStatus.DependencyTimeout)); }
        catch (HttpRequestException)
        { return (null, new(CandidateActionStatus.DependencyUnavailable)); }
        catch (Exception)
        { return (null, new(CandidateActionStatus.UnexpectedInternalFailure)); }
        CandidatePreviewSnapshot? candidate;
        try
        {
            candidate = await candidates.GetAsync(candidateId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return (null, new(CandidateActionStatus.DependencyTimeout)); }
        catch (HttpRequestException)
        { return (null, new(CandidateActionStatus.DependencyUnavailable)); }
        catch (Exception)
        { return (null, new(CandidateActionStatus.UnexpectedInternalFailure)); }
        if (candidate is null) return (null, new(CandidateActionStatus.NotFound));
        if (candidate.CandidateId != candidateId)
            return (null, new(CandidateActionStatus.NotFound));
        if (candidate.OwnerId != authenticatedOwnerId) return (null, new(CandidateActionStatus.NotFound));
        if (candidate.ProjectId != projectId || candidate.PlanId != planId)
            return (null, new(CandidateActionStatus.NotFound));
        if (!Enum.IsDefined(candidate.Status))
            return (null, new(CandidateActionStatus.InvalidDependencyResponse));
        if (candidate.Validation is null ||
            candidate.Validation.ValidationId == Guid.Empty)
            return (null, new(CandidateActionStatus.InvalidDependencyResponse));
        return (candidate, null);
    }

    private static CandidateActionResult NormalizeProtectedLookupResult(CandidateActionResult result) =>
        result.Status is CandidateActionStatus.Unauthorized or CandidateActionStatus.WrongProjectOrPlan or
            CandidateActionStatus.NotFound
            ? new(CandidateActionStatus.NotFound)
            : result;

    private static CandidateActionResult ValidateApplyResult(
        CandidatePreviewSnapshot expectedCandidate,
        PlanRevisionSnapshot expectedRevision,
        CandidateActionResult? result)
    {
        if (result is null) return UnknownTransaction(expectedCandidate);
        if (result.Status is CandidateActionStatus.Unauthorized or
            CandidateActionStatus.WrongProjectOrPlan or CandidateActionStatus.NotFound)
            return new(CandidateActionStatus.NotFound);
        if (result.Status == CandidateActionStatus.TransactionOutcomeUnknown)
            return UnknownTransaction(expectedCandidate);
        if (result.Status == CandidateActionStatus.Applied)
            return CandidateMatches(result.Candidate, expectedCandidate, CandidatePreviewStatus.Applied) &&
                RevisionMatches(result.Revision, expectedRevision)
                ? result
                : UnknownTransaction(expectedCandidate);
        if (result.Status is CandidateActionStatus.AlreadyApplied or
            CandidateActionStatus.AlreadyRejected or CandidateActionStatus.Expired)
        {
            var expectedStatus = result.Status switch
            {
                CandidateActionStatus.AlreadyApplied => CandidatePreviewStatus.Applied,
                CandidateActionStatus.AlreadyRejected => CandidatePreviewStatus.Rejected,
                _ => CandidatePreviewStatus.Expired
            };
            return result.Revision is null && CandidateMatches(result.Candidate, expectedCandidate, expectedStatus)
                ? result
                : UnknownTransaction(expectedCandidate);
        }
        if (result.Status is CandidateActionStatus.StaleBaseRevision or CandidateActionStatus.StaleInputs or
            CandidateActionStatus.PersistenceFailure)
            return result.Revision is null &&
                (result.Candidate is null || CandidateMatches(result.Candidate, expectedCandidate, CandidatePreviewStatus.Pending))
                ? result with { Candidate = result.Candidate is null ? null : expectedCandidate }
                : UnknownTransaction(expectedCandidate);
        return UnknownTransaction(expectedCandidate);
    }

    private static CandidateActionResult ValidateRejectResult(
        CandidatePreviewSnapshot expectedCandidate,
        CandidateActionResult? result)
    {
        if (result is null) return UnknownTransaction(expectedCandidate);
        if (result.Status is CandidateActionStatus.Unauthorized or
            CandidateActionStatus.WrongProjectOrPlan or CandidateActionStatus.NotFound)
            return new(CandidateActionStatus.NotFound);
        if (result.Status == CandidateActionStatus.TransactionOutcomeUnknown)
            return UnknownTransaction(expectedCandidate);
        if (result.Status == CandidateActionStatus.Rejected)
            return result.Revision is null &&
                CandidateMatches(result.Candidate, expectedCandidate, CandidatePreviewStatus.Rejected)
                ? result
                : UnknownTransaction(expectedCandidate);
        if (result.Status is CandidateActionStatus.AlreadyApplied or
            CandidateActionStatus.AlreadyRejected or CandidateActionStatus.Expired)
        {
            var expectedStatus = result.Status switch
            {
                CandidateActionStatus.AlreadyApplied => CandidatePreviewStatus.Applied,
                CandidateActionStatus.AlreadyRejected => CandidatePreviewStatus.Rejected,
                _ => CandidatePreviewStatus.Expired
            };
            return result.Revision is null && CandidateMatches(result.Candidate, expectedCandidate, expectedStatus)
                ? result
                : UnknownTransaction(expectedCandidate);
        }
        if (result.Status == CandidateActionStatus.PersistenceFailure)
            return result.Revision is null &&
                (result.Candidate is null || CandidateMatches(result.Candidate, expectedCandidate, CandidatePreviewStatus.Pending))
                ? result with { Candidate = result.Candidate is null ? null : expectedCandidate }
                : UnknownTransaction(expectedCandidate);
        return UnknownTransaction(expectedCandidate);
    }

    private async Task<CandidatePreviewResultStatus?> RevalidatePreviewContextAsync(
        CreateCandidatePreviewCommand command,
        Guid authenticatedOwnerId,
        PlanRevisionSnapshot verifiedRevision,
        ApplicableInputRevisions verifiedInputs,
        CancellationToken cancellationToken)
    {
        PlanRevisionSnapshot? current;
        ApplicableInputRevisions? applicable;
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedOwnerId, cancellationToken))
                return CandidatePreviewResultStatus.Unauthorized;
            current = await revisions.GetCurrentAsync(command.ProjectId, command.PlanId, cancellationToken);
            applicable = await currentInputs.GetCurrentAsync(command.ProjectId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return CandidatePreviewResultStatus.DependencyTimeout; }
        catch (HttpRequestException)
        { return CandidatePreviewResultStatus.DependencyUnavailable; }
        catch (Exception)
        { return CandidatePreviewResultStatus.UnexpectedInternalFailure; }
        if (current is null) return CandidatePreviewResultStatus.RevisionNotFound;
        if (current.ProjectId != command.ProjectId || current.PlanId != command.PlanId)
            return CandidatePreviewResultStatus.InvalidDependencyResponse;
        if (!StoredRevisionIsValid(current, command.ProjectId, command.PlanId))
            return CandidatePreviewResultStatus.InvalidDependencyResponse;
        if (current.RevisionId != command.BaseRevisionId)
            return CandidatePreviewResultStatus.StaleBaseRevision;
        if (!RevisionSnapshotsMatch(current, verifiedRevision))
            return CandidatePreviewResultStatus.InvalidDependencyResponse;
        if (applicable is null || applicable.LandRevisionId == Guid.Empty ||
            applicable.RequirementsRevisionId == Guid.Empty)
            return CandidatePreviewResultStatus.InvalidDependencyResponse;
        if (applicable.LandRevisionId != verifiedInputs.LandRevisionId ||
            applicable.RequirementsRevisionId != verifiedInputs.RequirementsRevisionId)
            return CandidatePreviewResultStatus.StaleInputs;

        cancellationToken.ThrowIfCancellationRequested();

        // Recheck ownership after all context reads and validation.
        // Production persistence must still enforce ownership atomically.
        try
        {
            if (!await ownership.OwnsProjectAsync(
                command.ProjectId, authenticatedOwnerId, cancellationToken))
                return CandidatePreviewResultStatus.Unauthorized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return CandidatePreviewResultStatus.DependencyTimeout; }
        catch (HttpRequestException)
        { return CandidatePreviewResultStatus.DependencyUnavailable; }
        catch (Exception)
        { return CandidatePreviewResultStatus.UnexpectedInternalFailure; }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<CandidateActionStatus?> RevalidateApplyContextAsync(
        CandidatePreviewSnapshot candidate,
        PlanRevisionSnapshot verifiedRevision,
        Guid authenticatedOwnerId,
        CancellationToken cancellationToken)
    {
        PlanRevisionSnapshot? current;
        ApplicableInputRevisions? applicable;
        try
        {
            current = await revisions.GetCurrentAsync(candidate.ProjectId, candidate.PlanId, cancellationToken);
            applicable = await currentInputs.GetCurrentAsync(candidate.ProjectId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return CandidateActionStatus.DependencyTimeout; }
        catch (HttpRequestException)
        { return CandidateActionStatus.DependencyUnavailable; }
        catch (Exception)
        { return CandidateActionStatus.UnexpectedInternalFailure; }
        if (current is null)
            return CandidateActionStatus.StaleBaseRevision;
        if (current.ProjectId != candidate.ProjectId || current.PlanId != candidate.PlanId)
            return CandidateActionStatus.InvalidDependencyResponse;
        if (!StoredRevisionIsValid(current, candidate.ProjectId, candidate.PlanId))
            return CandidateActionStatus.InvalidDependencyResponse;
        if (current.RevisionId != candidate.BaseRevisionId)
            return CandidateActionStatus.StaleBaseRevision;
        if (!RevisionSnapshotsMatch(current, verifiedRevision))
            return CandidateActionStatus.InvalidDependencyResponse;
        if (current.LandRevisionId != candidate.LandRevisionId ||
            current.RequirementsRevisionId != candidate.RequirementsRevisionId)
            return CandidateActionStatus.StaleInputs;
        if (current.GeometrySchemaVersion != candidate.GeometrySchemaVersion)
            return CandidateActionStatus.InvalidDependencyResponse;
        if (applicable is null || applicable.LandRevisionId == Guid.Empty ||
            applicable.RequirementsRevisionId == Guid.Empty)
            return CandidateActionStatus.InvalidDependencyResponse;
        if (applicable.LandRevisionId != candidate.LandRevisionId ||
            applicable.RequirementsRevisionId != candidate.RequirementsRevisionId
            ) return CandidateActionStatus.StaleInputs;
        try
        {
            if (!await ownership.OwnsProjectAsync(candidate.ProjectId, authenticatedOwnerId, cancellationToken))
                return CandidateActionStatus.Unauthorized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return CandidateActionStatus.DependencyTimeout; }
        catch (HttpRequestException)
        { return CandidateActionStatus.DependencyUnavailable; }
        catch (Exception)
        { return CandidateActionStatus.UnexpectedInternalFailure; }
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<CandidateActionResult?> ResolveTerminalAsync(
        CandidatePreviewSnapshot candidate,
        DateTimeOffset now,
        Guid authenticatedOwnerId,
        CancellationToken cancellationToken)
    {
        var status = candidate.Status switch
        {
            CandidatePreviewStatus.Applied => CandidateActionStatus.AlreadyApplied,
            CandidatePreviewStatus.Rejected => CandidateActionStatus.AlreadyRejected,
            CandidatePreviewStatus.Expired => CandidateActionStatus.Expired,
            _ => (CandidateActionStatus?)null
        };
        if (status is not null) return new(status.Value, candidate);
        if (now < candidate.ExpiresAtUtc) return null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await ownership.OwnsProjectAsync(
                candidate.ProjectId, authenticatedOwnerId, cancellationToken))
                return new(CandidateActionStatus.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(CandidateActionStatus.DependencyTimeout, candidate); }
        catch (HttpRequestException)
        { return new(CandidateActionStatus.DependencyUnavailable, candidate); }
        catch (Exception)
        { return new(CandidateActionStatus.UnexpectedInternalFailure, candidate); }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await transaction.TryExpireAtomicallyAsync(
                candidate, now, cancellationToken);
            return ValidateExpirationResult(candidate, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(CandidateActionStatus.TransactionOutcomeUnknown, candidate);
        }
    }

    internal static bool StoredRevisionIsValid(
        PlanRevisionSnapshot revision,
        Guid expectedProjectId,
        Guid expectedPlanId)
    {
        if (revision.RevisionId == Guid.Empty || revision.ProjectId != expectedProjectId ||
            revision.PlanId != expectedPlanId || revision.VersionNumber <= 0 || revision.VersionNumber == long.MaxValue ||
            string.IsNullOrWhiteSpace(revision.GeometrySchemaVersion) ||
            string.IsNullOrWhiteSpace(revision.GeometryJson) ||
            string.IsNullOrWhiteSpace(revision.GeometryHash) || revision.LandRevisionId == Guid.Empty ||
            revision.RequirementsRevisionId == Guid.Empty || revision.Validation is null ||
            revision.Validation.ValidationId == Guid.Empty ||
            !string.Equals(PlanRevisionService.ComputeGeometryHash(revision.GeometryJson),
                revision.GeometryHash, StringComparison.Ordinal) ||
            !string.Equals(revision.Validation.GeometryHash, revision.GeometryHash, StringComparison.Ordinal))
            return false;

        try
        {
            var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(
                revision.GeometryJson, PlanEditorJson.StrictOptions);
            if (geometry is null) return false;
            PlanEditorContractValidator.Validate(geometry);
            return geometry.ProjectId == expectedProjectId && geometry.PlanId == expectedPlanId &&
                geometry.LandRevisionId == revision.LandRevisionId &&
                geometry.RequirementsRevisionId == revision.RequirementsRevisionId &&
                string.Equals(geometry.SchemaVersion, revision.GeometrySchemaVersion, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool AuthorizationMatches(
        ProjectAuthorizationSnapshot? authorization,
        Guid projectId,
        Guid authenticatedActorId) =>
        authorization is not null &&
        authorization.ProjectId == projectId &&
        authorization.ActorId == authenticatedActorId &&
        authorization.Version.IsValid;

    private static bool RevisionSnapshotsMatch(
        PlanRevisionSnapshot actual,
        PlanRevisionSnapshot expected) =>
        actual.RevisionId == expected.RevisionId && actual.PlanId == expected.PlanId &&
        actual.ProjectId == expected.ProjectId && actual.ParentRevisionId == expected.ParentRevisionId &&
        actual.VersionNumber == expected.VersionNumber &&
        string.Equals(actual.GeometrySchemaVersion, expected.GeometrySchemaVersion, StringComparison.Ordinal) &&
        string.Equals(actual.GeometryJson, expected.GeometryJson, StringComparison.Ordinal) &&
        string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal) &&
        actual.LandRevisionId == expected.LandRevisionId &&
        actual.RequirementsRevisionId == expected.RequirementsRevisionId &&
        ValidationMatches(actual.Validation, expected.Validation) &&
        actual.EditCommandId == expected.EditCommandId && actual.CreatedBy == expected.CreatedBy &&
        actual.CreatedAtUtc.EqualsExact(expected.CreatedAtUtc) &&
        string.Equals(actual.ChangeSummary, expected.ChangeSummary, StringComparison.Ordinal);

    private static bool ValidationMatches(
        PlanValidationReferenceDto actual,
        PlanValidationReferenceDto expected) =>
        actual.ValidationId == expected.ValidationId &&
        string.Equals(actual.ValidatorVersion, expected.ValidatorVersion, StringComparison.Ordinal) &&
        string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal) &&
        actual.GeometryValid == expected.GeometryValid && actual.RequirementsMatch == expected.RequirementsMatch &&
        string.Equals(actual.RegulatoryEvidenceStatus, expected.RegulatoryEvidenceStatus, StringComparison.Ordinal) &&
        actual.HasBlockingIssues == expected.HasBlockingIssues &&
        actual.ValidatedAtUtc.EqualsExact(expected.ValidatedAtUtc);

    private static CandidateActionResult ValidateExpirationResult(
        CandidatePreviewSnapshot expected,
        CandidateActionResult? result)
    {
        if (result is null) return UnknownTransaction(expected);
        if (result.Status is CandidateActionStatus.Unauthorized or
            CandidateActionStatus.WrongProjectOrPlan or CandidateActionStatus.NotFound)
            return new(CandidateActionStatus.NotFound);
        if (result.Status == CandidateActionStatus.TransactionOutcomeUnknown)
            return UnknownTransaction(expected);
        if (result.Status == CandidateActionStatus.PersistenceFailure)
            return result.Revision is null &&
                (result.Candidate is null || CandidateMatches(result.Candidate, expected, CandidatePreviewStatus.Pending))
                ? result with { Candidate = result.Candidate is null ? null : expected }
                : UnknownTransaction(expected);
        if (result.Revision is not null) return UnknownTransaction(expected);
        var expectedStatus = result.Status switch
        {
            CandidateActionStatus.Expired => CandidatePreviewStatus.Expired,
            CandidateActionStatus.AlreadyApplied => CandidatePreviewStatus.Applied,
            CandidateActionStatus.AlreadyRejected => CandidatePreviewStatus.Rejected,
            _ => (CandidatePreviewStatus?)null
        };
        return expectedStatus is not null && CandidateMatches(result.Candidate, expected, expectedStatus.Value)
            ? result
            : UnknownTransaction(expected);
    }

    private static CandidateActionResult UnknownTransaction(CandidatePreviewSnapshot expected) =>
        new(CandidateActionStatus.TransactionOutcomeUnknown, expected);

    private static bool CandidateMatches(
        CandidatePreviewSnapshot? actual,
        CandidatePreviewSnapshot expected,
        CandidatePreviewStatus expectedStatus) =>
        actual is not null &&
        actual.CandidateId == expected.CandidateId &&
        actual.ProjectId == expected.ProjectId &&
        actual.PlanId == expected.PlanId &&
        actual.OwnerId == expected.OwnerId &&
        actual.BaseRevisionId == expected.BaseRevisionId &&
        actual.LandRevisionId == expected.LandRevisionId &&
        actual.RequirementsRevisionId == expected.RequirementsRevisionId &&
        actual.CommandId == expected.CommandId &&
        actual.GeometryJson == expected.GeometryJson &&
        actual.GeometryHash == expected.GeometryHash &&
        actual.GeometrySchemaVersion == expected.GeometrySchemaVersion &&
        actual.Validation is not null &&
        expected.Validation is not null &&
        ValidationMatches(actual.Validation, expected.Validation) &&
        actual.CreatedAtUtc.EqualsExact(expected.CreatedAtUtc) &&
        actual.ExpiresAtUtc.EqualsExact(expected.ExpiresAtUtc) &&
        actual.Status == expectedStatus;

    private static bool RevisionMatches(
        PlanRevisionSnapshot? actual,
        PlanRevisionSnapshot expected) =>
        actual is not null &&
        actual.RevisionId == expected.RevisionId &&
        actual.PlanId == expected.PlanId &&
        actual.ProjectId == expected.ProjectId &&
        actual.ParentRevisionId == expected.ParentRevisionId &&
        actual.VersionNumber == expected.VersionNumber &&
        actual.GeometrySchemaVersion == expected.GeometrySchemaVersion &&
        actual.GeometryJson == expected.GeometryJson &&
        actual.GeometryHash == expected.GeometryHash &&
        actual.LandRevisionId == expected.LandRevisionId &&
        actual.RequirementsRevisionId == expected.RequirementsRevisionId &&
        actual.Validation is not null &&
        expected.Validation is not null &&
        ValidationMatches(actual.Validation, expected.Validation) &&
        actual.EditCommandId == expected.EditCommandId &&
        actual.CreatedBy == expected.CreatedBy &&
        actual.CreatedAtUtc.EqualsExact(expected.CreatedAtUtc) &&
        actual.ChangeSummary == expected.ChangeSummary;

    private void EnsureConfiguredLifetime()
    {
        if (candidateLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Candidate lifetime must be supplied by trusted configuration.");
    }
}
