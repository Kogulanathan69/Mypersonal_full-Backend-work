using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Editing;

public sealed class AiEditOrchestrationService(
    IProjectOwnershipGate ownership,
    IPlanRevisionStore revisions,
    IApplicableInputRevisionSource currentInputs,
    IAiEditProposalPort aiProposals,
    CandidatePreviewService candidatePreviews)
{
    public async Task<AiEditOrchestrationResult> PreviewAsync(
        AiEditPreviewCommand command,
        Guid authenticatedActorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidRequest(command))
            return Failure(
                AiEditOrchestrationStatus.InvalidRequest,
                PlanEditorErrorCodes.InvalidEditCommand,
                "The AI edit request is invalid.",
                command?.CorrelationId);

        if (authenticatedActorId == Guid.Empty)
            return FromPreviewFailure(CandidatePreviewResultStatus.Unauthorized, command.CorrelationId);
        PlanRevisionSnapshot? current;
        ApplicableInputRevisions? applicable;
        var preflightOperation = PlanEditorDependencyOperation.Ownership;
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedActorId, cancellationToken))
                return FromPreviewFailure(CandidatePreviewResultStatus.Unauthorized, command.CorrelationId);
            cancellationToken.ThrowIfCancellationRequested();
            preflightOperation = PlanEditorDependencyOperation.RevisionRead;
            current = await revisions.GetCurrentAsync(command.ProjectId, command.PlanId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            preflightOperation = PlanEditorDependencyOperation.ApplicableInputsRead;
            applicable = await currentInputs.GetCurrentAsync(command.ProjectId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return DependencyFailure(AiEditOrchestrationStatus.DependencyTimeout,
            preflightOperation, exception, command.CorrelationId); }
        catch (HttpRequestException exception)
        { return DependencyFailure(AiEditOrchestrationStatus.DependencyUnavailable,
            preflightOperation, exception, command.CorrelationId); }
        catch (Exception exception)
        { return DependencyFailure(AiEditOrchestrationStatus.UnexpectedInternalFailure,
            preflightOperation, exception, command.CorrelationId); }
        if (current is null)
            return FromPreviewFailure(CandidatePreviewResultStatus.RevisionNotFound, command.CorrelationId);
        if (!CandidatePreviewService.StoredRevisionIsValid(
                current, command.ProjectId, command.PlanId))
            return FromPreviewFailure(
                CandidatePreviewResultStatus.InvalidDependencyResponse, command.CorrelationId);
        if (current.RevisionId != command.ExpectedBaseRevisionId)
            return FromPreviewFailure(CandidatePreviewResultStatus.StaleBaseRevision, command.CorrelationId);

        if (applicable is null || applicable.LandRevisionId == Guid.Empty ||
            applicable.RequirementsRevisionId == Guid.Empty)
            return FromPreviewFailure(CandidatePreviewResultStatus.InvalidDependencyResponse, command.CorrelationId);
        if (current.LandRevisionId != applicable.LandRevisionId ||
            current.RequirementsRevisionId != applicable.RequirementsRevisionId)
            return FromPreviewFailure(CandidatePreviewResultStatus.StaleInputs, command.CorrelationId);

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await ownership.OwnsProjectAsync(command.ProjectId, authenticatedActorId, cancellationToken))
                return FromPreviewFailure(CandidatePreviewResultStatus.Unauthorized, command.CorrelationId);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return DependencyFailure(AiEditOrchestrationStatus.DependencyTimeout,
            PlanEditorDependencyOperation.Ownership, exception, command.CorrelationId); }
        catch (HttpRequestException exception)
        { return DependencyFailure(AiEditOrchestrationStatus.DependencyUnavailable,
            PlanEditorDependencyOperation.Ownership, exception, command.CorrelationId); }
        catch (Exception exception)
        { return DependencyFailure(AiEditOrchestrationStatus.UnexpectedInternalFailure,
            PlanEditorDependencyOperation.Ownership, exception, command.CorrelationId); }

        cancellationToken.ThrowIfCancellationRequested();
        AiEditProposal? proposal;
        try
        {
            proposal = await aiProposals.ProposeAsync(
                command.ProjectId,
                command.PlanId,
                command.ExpectedBaseRevisionId,
                command.CustomerInstruction,
                command.CorrelationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return DependencyFailure(
                AiEditOrchestrationStatus.DependencyTimeout, PlanEditorDependencyOperation.AiProposal,
                exception, command.CorrelationId);
        }
        catch (HttpRequestException exception)
        {
            return DependencyFailure(
                AiEditOrchestrationStatus.DependencyUnavailable, PlanEditorDependencyOperation.AiProposal,
                exception, command.CorrelationId);
        }
        catch (Exception exception)
        {
            return DependencyFailure(
                AiEditOrchestrationStatus.UnexpectedInternalFailure, PlanEditorDependencyOperation.AiProposal,
                exception, command.CorrelationId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (proposal is null || !Enum.IsDefined(proposal.Status))
            return InvalidProposal(command.CorrelationId);
        if (proposal.Status == AiEditProposalStatus.Unavailable)
            return Failure(
                AiEditOrchestrationStatus.ProposalUnavailable,
                PlanEditorErrorCodes.DependencyUnavailable,
                "The AI editing service is temporarily unavailable.",
                command.CorrelationId,
                PlanEditorRetrySafety.Safe);
        if (proposal.Status == AiEditProposalStatus.Rejected)
            return Failure(
                AiEditOrchestrationStatus.ProposalRejected,
                PlanEditorErrorCodes.AiProposalInvalid,
                "The AI edit proposal was rejected.",
                command.CorrelationId);

        if (proposal.Commands is null || proposal.Commands.Count != 1 || proposal.Commands[0] is null)
            return InvalidProposal(command.CorrelationId);
        var proposedCommand = proposal.Commands[0];
        if (proposedCommand.TargetId == Guid.Empty)
            return InvalidProposal(command.CorrelationId);
        try
        {
            PlanEditorContractValidator.Validate(new PlanEditRequestDto
            {
                SchemaVersion = current.GeometrySchemaVersion,
                ProjectId = command.ProjectId,
                PlanId = command.PlanId,
                BaseRevisionId = command.ExpectedBaseRevisionId,
                Command = proposedCommand
            });
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
        {
            return InvalidProposal(command.CorrelationId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        CandidatePreviewResult preview;
        try
        {
            preview = await candidatePreviews.PreviewAsync(
                new CreateCandidatePreviewCommand(
                    command.ProjectId,
                    command.PlanId,
                    command.ExpectedBaseRevisionId,
                    proposedCommand,
                    command.CorrelationId),
                authenticatedActorId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The delegated workflow can write a candidate. An escaping exception
            // does not establish whether persistence was attempted or completed.
            return DependencyFailure(
                AiEditOrchestrationStatus.CandidatePersistenceOutcomeUnknown,
                PlanEditorDependencyOperation.CandidatePersistence,
                exception, command.CorrelationId);
        }

        if (preview.Status == CandidatePreviewResultStatus.Created && preview.Candidate is not null)
            return new(AiEditOrchestrationStatus.CandidateCreated, preview.Candidate);
        if (preview.Status == CandidatePreviewResultStatus.PersistenceOutcomeUnknown && preview.Candidate is not null)
            return new(
                AiEditOrchestrationStatus.CandidatePersistenceOutcomeUnknown,
                preview.Candidate,
                PlanEditorFailureMapper.FromCandidatePreview(preview.Status, command.CorrelationId));
        return FromPreviewFailure(preview.Status, command.CorrelationId);
    }

    private static bool ValidRequest(AiEditPreviewCommand? command) =>
        command is not null &&
        command.ProjectId != Guid.Empty &&
        command.PlanId != Guid.Empty &&
        command.ExpectedBaseRevisionId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(command.CustomerInstruction) &&
        !string.IsNullOrWhiteSpace(command.CorrelationId);

    private static AiEditOrchestrationResult InvalidProposal(string? correlationId) =>
        Failure(
            AiEditOrchestrationStatus.ProposalInvalid,
            PlanEditorErrorCodes.AiProposalInvalid,
            "The AI edit proposal could not be accepted.",
            correlationId);

    private static AiEditOrchestrationResult DependencyFailure(
        AiEditOrchestrationStatus status,
        PlanEditorDependencyOperation operation,
        Exception exception,
        string? correlationId) =>
        CreateDependencyResult(status, PlanEditorFailureMapper.FromDependencyException(
            operation, exception, correlationId));

    private static AiEditOrchestrationResult CreateDependencyResult(
        AiEditOrchestrationStatus status, PlanEditorDiagnostic diagnostic) =>
        new(diagnostic.Code == PlanEditorErrorCodes.InvalidDependencyResponse
            ? AiEditOrchestrationStatus.InvalidDependencyResponse
            : status, Diagnostic: diagnostic);

    private static AiEditOrchestrationResult FromPreviewFailure(
        CandidatePreviewResultStatus status,
        string? correlationId) => status switch
    {
        CandidatePreviewResultStatus.Unauthorized => new(
            AiEditOrchestrationStatus.Unauthorized,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.RevisionNotFound => new(
            AiEditOrchestrationStatus.RevisionNotFound,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.StaleBaseRevision => new(
            AiEditOrchestrationStatus.StaleBaseRevision,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.StaleInputs => new(
            AiEditOrchestrationStatus.StaleInputs,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.InvalidDependencyResponse => new(
            AiEditOrchestrationStatus.InvalidDependencyResponse,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.ValidationUnavailable => new(
            AiEditOrchestrationStatus.ValidationUnavailable,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.ValidationRejected => new(
            AiEditOrchestrationStatus.ValidationRejected,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.DependencyUnavailable => new(
            AiEditOrchestrationStatus.DependencyUnavailable,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.DependencyTimeout => new(
            AiEditOrchestrationStatus.DependencyTimeout,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.UnexpectedInternalFailure => new(
            AiEditOrchestrationStatus.UnexpectedInternalFailure,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        CandidatePreviewResultStatus.PersistenceOutcomeUnknown => new(
            AiEditOrchestrationStatus.CandidatePersistenceOutcomeUnknown,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(status, correlationId)),
        _ => new(
            AiEditOrchestrationStatus.CandidateRejected,
            Diagnostic: PlanEditorFailureMapper.FromCandidatePreview(
                status == CandidatePreviewResultStatus.Created
                    ? CandidatePreviewResultStatus.CandidateRejected
                    : status,
                correlationId))
    };

    private static AiEditOrchestrationResult Failure(
        AiEditOrchestrationStatus status,
        string code,
        string message,
        string? correlationId,
        PlanEditorRetrySafety retrySafety = PlanEditorRetrySafety.Never) =>
        new(status, Diagnostic: new PlanEditorDiagnostic(
            code,
            message,
            PlanEditorDiagnosticSeverity.Error,
            true,
            correlationId,
            null,
            retrySafety));
}
