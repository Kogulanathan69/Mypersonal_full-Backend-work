using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Confirmation;

namespace BuildMind.Application.Features.PlanEditor.Versioning;

public sealed class PlanRevisionService(
    IProjectOwnershipGate ownership,
    IPlanRevisionStore store,
    IPlanValidationEvidenceVerifier validationEvidenceVerifier,
    TimeProvider timeProvider)
{
    public async Task<AppendRevisionResult> CreateAsync(
        CreatePlanRevisionCommand command,
        Guid authenticatedCreatorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        cancellationToken.ThrowIfCancellationRequested();
        if (authenticatedCreatorId == Guid.Empty)
            return new(AppendRevisionStatus.Unauthorized);
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedCreatorId, cancellationToken))
                return new(AppendRevisionStatus.Unauthorized);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception) { return new(AppendRevisionStatus.UnexpectedInternalFailure); }
        PlanEditorContractValidator.Validate(command.Geometry);
        if (command.Geometry.ProjectId != command.ProjectId || command.Geometry.PlanId != command.PlanId)
            throw new ArgumentException("Geometry identity does not match the revision command.");

        PlanRevisionSnapshot? current;
        try { current = await store.GetAsync(
            command.ProjectId, command.PlanId, command.ExpectedBaseRevisionId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception) { return new(AppendRevisionStatus.UnexpectedInternalFailure); }
        if (current is null) return new(AppendRevisionStatus.NotFound);

        if (ReadVerifiedGeometry(
                current,
                command.ProjectId,
                command.PlanId,
                command.ExpectedBaseRevisionId) is null)
            return new(AppendRevisionStatus.InvalidDependencyResponse);

        var expectedBaseValidationJson = JsonSerializer.Serialize(
            current.Validation, PlanEditorJson.StrictOptions);

        var json = JsonSerializer.Serialize(command.Geometry, PlanEditorJson.StrictOptions);
        var hash = ComputeGeometryHash(json);
        if (command.Validation is null || command.Validation.ValidationId == Guid.Empty)
            throw new ArgumentException("A valid validation evidence identity is required.", nameof(command));
        if (!string.Equals(command.Validation.GeometryHash, hash, StringComparison.Ordinal))
            throw new ArgumentException("Validation evidence does not match the exact geometry snapshot.");
        if (!command.Validation.GeometryValid || command.Validation.HasBlockingIssues)
            throw new ArgumentException("A revision cannot be created from blocking validation evidence.");
        ValidationEvidenceStatus evidenceStatus;
        try { evidenceStatus = await validationEvidenceVerifier.VerifyAsync(
            command.ProjectId, command.PlanId, command.Validation, hash, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception) { return new(AppendRevisionStatus.UnexpectedInternalFailure); }
        if (evidenceStatus == ValidationEvidenceStatus.Unavailable)
            return new(AppendRevisionStatus.ValidationUnavailable);
        if (evidenceStatus == ValidationEvidenceStatus.Rejected)
            return new(AppendRevisionStatus.ValidationRejected);
        if (evidenceStatus != ValidationEvidenceStatus.Verified)
            return new(AppendRevisionStatus.InvalidDependencyResponse);

        var revision = new PlanRevisionSnapshot(
            Guid.NewGuid(), command.PlanId, command.ProjectId, current.RevisionId,
            checked(current.VersionNumber + 1), command.Geometry.SchemaVersion, json, hash,
            command.Geometry.LandRevisionId, command.Geometry.RequirementsRevisionId,
            command.Validation, command.EditCommandId, authenticatedCreatorId,
            timeProvider.GetUtcNow(), command.ChangeSummary);

        // Revalidate the current immutable base after evidence verification.
        // Atomic enforcement remains a production persistence responsibility.
        cancellationToken.ThrowIfCancellationRequested();

        PlanRevisionSnapshot? latest;

        try
        {
            latest = await store.GetCurrentAsync(
                command.ProjectId, command.PlanId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException)
        { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception)
        { return new(AppendRevisionStatus.UnexpectedInternalFailure); }

        cancellationToken.ThrowIfCancellationRequested();

        if (latest is null)
            return new(AppendRevisionStatus.InvalidDependencyResponse);

        if (ReadVerifiedGeometry(
                latest,
                command.ProjectId,
                command.PlanId,
                latest.RevisionId) is null)
            return new(AppendRevisionStatus.InvalidDependencyResponse);

        if (latest.RevisionId != current.RevisionId)
            return new(AppendRevisionStatus.RevisionConflict);

        if (!RevisionMatches(latest, current, expectedBaseValidationJson))
            return new(AppendRevisionStatus.InvalidDependencyResponse);

        // Reduce the authorization race before the mutation.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedCreatorId, cancellationToken))
                return new(AppendRevisionStatus.Unauthorized);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception) { return new(AppendRevisionStatus.UnexpectedInternalFailure); }

        var expectedValidationJson = JsonSerializer.Serialize(
            revision.Validation, PlanEditorJson.StrictOptions);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await store.TryAppendAtomicallyAsync(
                command.ExpectedBaseRevisionId,
                revision,
                cancellationToken);

            return ValidateAppendResult(
                revision, expectedValidationJson, result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation does not establish rollback.
            throw;
        }
        catch (Exception)
        {
            // Mutation may have committed before acknowledgement loss.
            return UnknownAppendOutcome(revision);
        }
    }

    public async Task<AppendRevisionResult> RestoreAsync(
        Guid projectId,
        Guid planId,
        Guid revisionToRestoreId,
        Guid expectedCurrentRevisionId,
        PlanValidationReferenceDto freshValidation,
        Guid authenticatedRestorerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authenticatedRestorerId == Guid.Empty)
            return new(AppendRevisionStatus.Unauthorized);
        try
        {
            if (!await ownership.OwnsProjectAsync(projectId, authenticatedRestorerId, cancellationToken))
                return new(AppendRevisionStatus.Unauthorized);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception) { return new(AppendRevisionStatus.UnexpectedInternalFailure); }

        PlanRevisionSnapshot? source;
        try { source = await store.GetAsync(projectId, planId, revisionToRestoreId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AppendRevisionStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(AppendRevisionStatus.DependencyUnavailable); }
        catch (Exception) { return new(AppendRevisionStatus.UnexpectedInternalFailure); }
        if (source is null) return new(AppendRevisionStatus.NotFound);
        var geometry = ReadVerifiedGeometry(
            source, projectId, planId, revisionToRestoreId);

        if (geometry is null)
            return new(AppendRevisionStatus.InvalidDependencyResponse);

        return await CreateAsync(
            new(projectId, planId, expectedCurrentRevisionId, geometry, freshValidation, null,
                $"Restore revision {revisionToRestoreId}"), authenticatedRestorerId, cancellationToken);
    }

    // Verify stored snapshots using the existing provisional M3 contract.
    // This does not replace authoritative M6 validation.
    private static PlanGeometryDto? ReadVerifiedGeometry(
        PlanRevisionSnapshot snapshot,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid expectedRevisionId)
    {
        if (snapshot.RevisionId == Guid.Empty ||
            snapshot.RevisionId != expectedRevisionId ||
            snapshot.ProjectId != expectedProjectId ||
            snapshot.PlanId != expectedPlanId ||
            snapshot.GeometrySchemaVersion != GeometrySchemaInfo.DraftVersion ||
            snapshot.VersionNumber <= 0 ||
            snapshot.VersionNumber == long.MaxValue ||
            string.IsNullOrWhiteSpace(snapshot.GeometryJson) ||
            string.IsNullOrWhiteSpace(snapshot.GeometryHash) ||
            snapshot.Validation is null ||
            snapshot.Validation.ValidationId == Guid.Empty)
            return null;

        if (!string.Equals(
                snapshot.Validation.GeometryHash,
                snapshot.GeometryHash,
                StringComparison.Ordinal))
            return null;

        try
        {
            var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(
                snapshot.GeometryJson, PlanEditorJson.StrictOptions);

            if (geometry is null)
                return null;

            PlanEditorContractValidator.Validate(geometry);

            if (geometry.ProjectId != snapshot.ProjectId ||
                geometry.PlanId != snapshot.PlanId ||
                geometry.SchemaVersion != snapshot.GeometrySchemaVersion ||
                geometry.LandRevisionId != snapshot.LandRevisionId ||
                geometry.RequirementsRevisionId != snapshot.RequirementsRevisionId)
                return null;

            var storedHash = ComputeGeometryHash(snapshot.GeometryJson);

            if (!string.Equals(
                    storedHash,
                    snapshot.GeometryHash,
                    StringComparison.Ordinal))
                return null;

            return geometry;
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException
                or NullReferenceException)
        {
            // Malformed dependency data must not reach the append operation.
            return null;
        }
    }

    private static AppendRevisionResult UnknownAppendOutcome(
        PlanRevisionSnapshot expected) =>
        new(AppendRevisionStatus.TransactionOutcomeUnknown)
        {
            ExpectedRevisionId = expected.RevisionId,
            ExpectedEditCommandId = expected.EditCommandId
        };

    private static AppendRevisionResult ValidateAppendResult(
        PlanRevisionSnapshot expected,
        string expectedValidationJson,
        AppendRevisionResult? result)
    {
        if (result is null ||
            result.Status == AppendRevisionStatus.TransactionOutcomeUnknown)
            return UnknownAppendOutcome(expected);

        if (result.ExpectedRevisionId is not null ||
            result.ExpectedEditCommandId is not null)
            return UnknownAppendOutcome(expected);

        if (result.Status == AppendRevisionStatus.Created)
        {
            return RevisionMatches(
                result.Revision, expected, expectedValidationJson)
                ? new(AppendRevisionStatus.Created, result.Revision)
                : UnknownAppendOutcome(expected);
        }

        if (result.Revision is not null)
            return UnknownAppendOutcome(expected);

        // Only recognized provisional non-success outcomes.
        return result.Status switch
        {
            AppendRevisionStatus.RevisionConflict or
            AppendRevisionStatus.DuplicateCommand or
            AppendRevisionStatus.NotFound or
            AppendRevisionStatus.Unauthorized =>
                new AppendRevisionResult(result.Status),

            _ => UnknownAppendOutcome(expected)
        };
    }

    private static bool RevisionMatches(
        PlanRevisionSnapshot? actual,
        PlanRevisionSnapshot expected,
        string expectedValidationJson) =>
        actual is not null &&
        actual.RevisionId == expected.RevisionId &&
        actual.ProjectId == expected.ProjectId &&
        actual.PlanId == expected.PlanId &&
        actual.ParentRevisionId == expected.ParentRevisionId &&
        actual.VersionNumber == expected.VersionNumber &&
        actual.GeometrySchemaVersion == expected.GeometrySchemaVersion &&
        actual.GeometryJson == expected.GeometryJson &&
        actual.GeometryHash == expected.GeometryHash &&
        actual.LandRevisionId == expected.LandRevisionId &&
        actual.RequirementsRevisionId == expected.RequirementsRevisionId &&
        actual.EditCommandId == expected.EditCommandId &&
        actual.CreatedBy == expected.CreatedBy &&
        actual.CreatedAtUtc.EqualsExact(expected.CreatedAtUtc) &&
        actual.ChangeSummary == expected.ChangeSummary &&
        actual.Validation is not null &&
        string.Equals(
            JsonSerializer.Serialize(
                actual.Validation, PlanEditorJson.StrictOptions),
            expectedValidationJson,
            StringComparison.Ordinal);

    public static string ComputeGeometryHash(string geometryJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(geometryJson));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
