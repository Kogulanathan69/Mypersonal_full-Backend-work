using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Confirmation;

public sealed record ConfirmPlanCommand(
    Guid ProjectId,
    Guid PlanId,
    Guid RevisionId,
    string ExpectedGeometryHash,
    bool AcknowledgedWarnings);

public sealed record ApplicableInputRevisions(Guid LandRevisionId, Guid RequirementsRevisionId);

public sealed record PlanConfirmationSnapshot(
    Guid ConfirmationId,
    Guid ProjectId,
    Guid PlanId,
    Guid ConfirmedRevisionId,
    string GeometryHash,
    Guid ValidationId,
    Guid ConfirmedBy,
    DateTimeOffset ConfirmedAtUtc,
    bool WarningAcknowledgement);

public enum ConfirmationStatus
{
    Confirmed,
    Unauthorized,
    RevisionNotFound,
    GeometryHashMismatch,
    StaleInputs,
    ValidationStale,
    NotCurrentRevision,
    BlockingIssues,
    WarningAcknowledgementRequired,
    AlreadyConfirmed,
    TransactionOutcomeUnknown,
    InvalidDependencyResponse,
    DependencyTimeout,
    DependencyUnavailable,
    UnexpectedInternalFailure
}

// Provisional M3 states. Durable reconciliation and delivery remain M4/M5 responsibilities.
public enum ConfirmationPublicationStatus { NotAttempted, Acknowledged, OutcomeUnknown }

public sealed record ConfirmationResult(ConfirmationStatus Status, PlanConfirmationSnapshot? Confirmation = null)
{
    public Guid? ExpectedConfirmationId { get; init; }
    public Guid? ExpectedRevisionId { get; init; }
    public ConfirmationPublicationStatus PublicationStatus { get; init; } =
        ConfirmationPublicationStatus.NotAttempted;
}

public interface IProjectOwnershipGate
{
    Task<bool> OwnsProjectAsync(Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken);
}

public interface IApplicableInputRevisionSource
{
    Task<ApplicableInputRevisions> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken);
}

public interface IValidationFreshnessPolicy
{
    bool IsFresh(PlanRevisionSnapshot revision, DateTimeOffset now);
}

public interface IFinalConfirmationTransaction
{
    /// <summary>
    /// The M4 adapter must atomically create the immutable confirmation and
    /// apply the approved M1 lifecycle transition, with idempotency enforced.
    /// </summary>
    Task<ConfirmationResult> ConfirmAtomicallyAsync(
        PlanConfirmationSnapshot confirmation,
        PlanRevisionSnapshot exactRevision,
        CancellationToken cancellationToken);
}

public interface IConfirmedPlanSnapshotSink
{
    Task PublishAsync(PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision, CancellationToken cancellationToken);
}

public sealed class FinalPlanConfirmationService(
    IProjectOwnershipGate ownership,
    IPlanRevisionStore revisions,
    IApplicableInputRevisionSource inputs,
    IValidationFreshnessPolicy freshnessPolicy,
    IFinalConfirmationTransaction transaction,
    IConfirmedPlanSnapshotSink reportSink,
    TimeProvider timeProvider)
{
    public async Task<ConfirmationResult> ConfirmAsync(
        ConfirmPlanCommand command,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authenticatedUserId == Guid.Empty)
            return new(ConfirmationStatus.Unauthorized);
        PlanRevisionSnapshot? revision;
        PlanRevisionSnapshot? currentRevision;
        ApplicableInputRevisions? currentInputs;
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedUserId, cancellationToken))
                return new(ConfirmationStatus.Unauthorized);
            revision = await revisions.GetAsync(command.ProjectId, command.PlanId, command.RevisionId, cancellationToken);
            currentRevision = await revisions.GetCurrentAsync(command.ProjectId, command.PlanId, cancellationToken);
            currentInputs = await inputs.GetCurrentAsync(command.ProjectId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(ConfirmationStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(ConfirmationStatus.DependencyUnavailable); }
        catch (Exception) { return new(ConfirmationStatus.UnexpectedInternalFailure); }
        if (revision is null) return new(ConfirmationStatus.RevisionNotFound);
        if (!TryReadVerifiedGeometry(revision, command.ProjectId, command.PlanId, command.RevisionId, out _))
            return new(ConfirmationStatus.InvalidDependencyResponse);
        if (currentRevision is null ||
            !TryReadVerifiedGeometry(
                currentRevision, command.ProjectId, command.PlanId, currentRevision.RevisionId, out _))
            return new(ConfirmationStatus.InvalidDependencyResponse);
        if (currentRevision.RevisionId != revision.RevisionId)
            return new(ConfirmationStatus.NotCurrentRevision);
        if (!RevisionMatches(currentRevision, revision))
            return new(ConfirmationStatus.InvalidDependencyResponse);
        if (!string.Equals(revision.GeometryHash, command.ExpectedGeometryHash, StringComparison.Ordinal))
            return new(ConfirmationStatus.GeometryHashMismatch);
        if (!string.Equals(revision.Validation.GeometryHash, revision.GeometryHash, StringComparison.Ordinal))
            return new(ConfirmationStatus.ValidationStale);
        cancellationToken.ThrowIfCancellationRequested();

        bool isFresh;
        try
        {
            isFresh = freshnessPolicy.IsFresh(revision, timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return new(ConfirmationStatus.DependencyTimeout);
        }
        catch (HttpRequestException)
        {
            return new(ConfirmationStatus.DependencyUnavailable);
        }
        catch (Exception)
        {
            return new(ConfirmationStatus.UnexpectedInternalFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!isFresh)
            return new(ConfirmationStatus.ValidationStale);
        if (!revision.Validation.GeometryValid || revision.Validation.HasBlockingIssues)
            return new(ConfirmationStatus.BlockingIssues);

        if (currentInputs is null || currentInputs.LandRevisionId == Guid.Empty ||
            currentInputs.RequirementsRevisionId == Guid.Empty)
            return new(ConfirmationStatus.InvalidDependencyResponse);
        if (revision.LandRevisionId != currentInputs.LandRevisionId
            || revision.RequirementsRevisionId != currentInputs.RequirementsRevisionId)
            return new(ConfirmationStatus.StaleInputs);

        var hasWarnings = !string.Equals(
            revision.Validation.RegulatoryEvidenceStatus, "VERIFIED", StringComparison.OrdinalIgnoreCase)
            || revision.Validation.RequirementsMatch is not true;
        if (hasWarnings && !command.AcknowledgedWarnings)
            return new(ConfirmationStatus.WarningAcknowledgementRequired);

        var confirmation = new PlanConfirmationSnapshot(
            Guid.NewGuid(), command.ProjectId, command.PlanId, revision.RevisionId,
            revision.GeometryHash, revision.Validation.ValidationId, authenticatedUserId,
            timeProvider.GetUtcNow(), command.AcknowledgedWarnings);

        ConfirmationStatus? contextFailure;
        try { contextFailure = await RevalidateContextAsync(
            revision, authenticatedUserId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(ConfirmationStatus.DependencyTimeout); }
        catch (HttpRequestException) { return new(ConfirmationStatus.DependencyUnavailable); }
        catch (Exception) { return new(ConfirmationStatus.UnexpectedInternalFailure); }
        if (contextFailure is not null) return new(contextFailure.Value);

        cancellationToken.ThrowIfCancellationRequested();

        ConfirmationResult? result;
        try
        {
            result = await transaction.ConfirmAtomicallyAsync(
                confirmation, revision, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return UnknownTransaction(confirmation);
        }

        var verified = ValidateTransactionResult(confirmation, result);
        if (verified.Status != ConfirmationStatus.Confirmed)
            return verified;

        try
        {
            await reportSink.PublishAsync(confirmation, revision, cancellationToken);
            return verified with { PublicationStatus = ConfirmationPublicationStatus.Acknowledged };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return verified with { PublicationStatus = ConfirmationPublicationStatus.OutcomeUnknown };
        }
    }

    private async Task<ConfirmationStatus?> RevalidateContextAsync(
        PlanRevisionSnapshot expected,
        Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await ownership.OwnsProjectAsync(
                expected.ProjectId, authenticatedUserId, cancellationToken))
            return ConfirmationStatus.Unauthorized;

        var current = await revisions.GetCurrentAsync(
            expected.ProjectId, expected.PlanId, cancellationToken);
        if (current is null ||
            !TryReadVerifiedGeometry(
                current, expected.ProjectId, expected.PlanId, current.RevisionId, out _))
            return ConfirmationStatus.InvalidDependencyResponse;
        if (current.RevisionId != expected.RevisionId)
            return ConfirmationStatus.NotCurrentRevision;
        if (!RevisionMatches(current, expected))
            return ConfirmationStatus.InvalidDependencyResponse;
        var applicable = await inputs.GetCurrentAsync(expected.ProjectId, cancellationToken);
        if (applicable is null || applicable.LandRevisionId == Guid.Empty ||
            applicable.RequirementsRevisionId == Guid.Empty)
            return ConfirmationStatus.InvalidDependencyResponse;
        if (applicable.LandRevisionId != expected.LandRevisionId ||
            applicable.RequirementsRevisionId != expected.RequirementsRevisionId)
            return ConfirmationStatus.StaleInputs;

        cancellationToken.ThrowIfCancellationRequested();

        // Reduce the authorization race after all dependency reads.
        // Production persistence must enforce the same context atomically.
        if (!await ownership.OwnsProjectAsync(
                expected.ProjectId, authenticatedUserId, cancellationToken))
            return ConfirmationStatus.Unauthorized;

        cancellationToken.ThrowIfCancellationRequested();

        if (!freshnessPolicy.IsFresh(expected, timeProvider.GetUtcNow()))
            return ConfirmationStatus.ValidationStale;

        cancellationToken.ThrowIfCancellationRequested();

        return null;
    }

    private static ConfirmationResult ValidateTransactionResult(
        PlanConfirmationSnapshot expected,
        ConfirmationResult? result)
    {
        if (result is null || !Enum.IsDefined(result.Status) ||
            result.Status == ConfirmationStatus.TransactionOutcomeUnknown)
            return UnknownTransaction(expected);
        if (result.ExpectedConfirmationId is not null || result.ExpectedRevisionId is not null ||
            result.PublicationStatus != ConfirmationPublicationStatus.NotAttempted)
            return UnknownTransaction(expected);
        if (result.Status == ConfirmationStatus.Confirmed)
            return ConfirmationMatches(result.Confirmation, expected)
                ? new ConfirmationResult(ConfirmationStatus.Confirmed, expected)
                : UnknownTransaction(expected);
        if (result.Confirmation is not null)
            return UnknownTransaction(expected);

        return result.Status switch
        {
            ConfirmationStatus.Unauthorized or ConfirmationStatus.RevisionNotFound or
            ConfirmationStatus.GeometryHashMismatch or ConfirmationStatus.StaleInputs or
            ConfirmationStatus.ValidationStale or ConfirmationStatus.NotCurrentRevision or
            ConfirmationStatus.BlockingIssues or ConfirmationStatus.WarningAcknowledgementRequired or
            ConfirmationStatus.AlreadyConfirmed or ConfirmationStatus.InvalidDependencyResponse =>
                new ConfirmationResult(result.Status),
            _ => UnknownTransaction(expected)
        };
    }

    private static ConfirmationResult UnknownTransaction(PlanConfirmationSnapshot expected) =>
        new(ConfirmationStatus.TransactionOutcomeUnknown)
        {
            ExpectedConfirmationId = expected.ConfirmationId,
            ExpectedRevisionId = expected.ConfirmedRevisionId
        };

    private static bool ConfirmationMatches(
        PlanConfirmationSnapshot? actual,
        PlanConfirmationSnapshot expected) =>
        actual is not null &&
        actual.ConfirmationId == expected.ConfirmationId &&
        actual.ProjectId == expected.ProjectId &&
        actual.PlanId == expected.PlanId &&
        actual.ConfirmedRevisionId == expected.ConfirmedRevisionId &&
        actual.GeometryHash == expected.GeometryHash &&
        actual.ValidationId == expected.ValidationId &&
        actual.ConfirmedBy == expected.ConfirmedBy &&
        actual.ConfirmedAtUtc.EqualsExact(expected.ConfirmedAtUtc) &&
        actual.WarningAcknowledgement == expected.WarningAcknowledgement;

    private static bool RevisionMatches(PlanRevisionSnapshot actual, PlanRevisionSnapshot expected) =>
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
        actual.EditCommandId == expected.EditCommandId &&
        actual.CreatedBy == expected.CreatedBy &&
        actual.CreatedAtUtc.EqualsExact(expected.CreatedAtUtc) &&
        actual.ChangeSummary == expected.ChangeSummary &&
        actual.Validation is not null && expected.Validation is not null &&
        JsonSerializer.Serialize(actual.Validation, PlanEditorJson.StrictOptions) ==
            JsonSerializer.Serialize(expected.Validation, PlanEditorJson.StrictOptions);

    private static bool TryReadVerifiedGeometry(
        PlanRevisionSnapshot snapshot,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid expectedRevisionId,
        out PlanGeometryDto? geometry)
    {
        geometry = null;
        if (snapshot.RevisionId == Guid.Empty || snapshot.RevisionId != expectedRevisionId ||
            snapshot.ProjectId != expectedProjectId || snapshot.PlanId != expectedPlanId ||
            snapshot.VersionNumber <= 0 ||
            snapshot.GeometrySchemaVersion != GeometrySchemaInfo.DraftVersion ||
            string.IsNullOrWhiteSpace(snapshot.GeometryJson) ||
            string.IsNullOrWhiteSpace(snapshot.GeometryHash) || snapshot.Validation is null ||
            snapshot.Validation.ValidationId == Guid.Empty ||
            !string.Equals(snapshot.Validation.GeometryHash, snapshot.GeometryHash, StringComparison.Ordinal))
            return false;
        try
        {
            geometry = JsonSerializer.Deserialize<PlanGeometryDto>(
                snapshot.GeometryJson, PlanEditorJson.StrictOptions);
            if (geometry is null) return false;
            PlanEditorContractValidator.Validate(geometry);
            return geometry.ProjectId == snapshot.ProjectId &&
                geometry.PlanId == snapshot.PlanId &&
                geometry.SchemaVersion == snapshot.GeometrySchemaVersion &&
                geometry.LandRevisionId == snapshot.LandRevisionId &&
                geometry.RequirementsRevisionId == snapshot.RequirementsRevisionId &&
                string.Equals(
                    PlanRevisionService.ComputeGeometryHash(snapshot.GeometryJson),
                    snapshot.GeometryHash,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            geometry = null;
            return false;
        }
    }
}
