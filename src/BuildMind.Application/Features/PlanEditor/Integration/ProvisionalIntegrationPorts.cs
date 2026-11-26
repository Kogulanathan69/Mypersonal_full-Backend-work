using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;

namespace BuildMind.Application.Features.PlanEditor.Integration;

// These ports are provisional consumer requirements. They intentionally have
// no Infrastructure implementations or DI registration in the M3 branch.

public sealed record ProjectLifecycleEligibility(bool ProjectExists, bool CanConfirm, string? BlockingCode);

public interface IProjectLifecycleEligibilityPort
{
    Task<ProjectLifecycleEligibility> GetConfirmationEligibilityAsync(
        Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken);
}

public sealed record ConfirmedLandRevision(
    Guid ProjectId,
    Guid RevisionId,
    string GeometrySchemaVersion,
    JsonElement BoundaryGeometry);

public interface IConfirmedLandRevisionPort
{
    Task<ConfirmedLandRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken);
}

public sealed record RequirementRevisionReference(Guid ProjectId, Guid RevisionId, string SchemaVersion);

public interface IRequirementRevisionPort
{
    Task<RequirementRevisionReference?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken);
}

public sealed record PlanEditorComputeRequest(
    PlanGeometryDto SourceGeometry,
    PlanEditCommandDto Command,
    string CorrelationId);

public interface IPlanEditorComputeClient
{
    Task<PlanEditResultDto> PreviewAsync(PlanEditorComputeRequest request, CancellationToken cancellationToken);
}

public enum AiEditProposalStatus { Proposed, Unavailable, Rejected }

public sealed record AiEditProposal(
    AiEditProposalStatus Status,
    IReadOnlyList<PlanEditCommandDto> Commands,
    IReadOnlyList<PlanEditDiagnosticDto> Diagnostics);

public interface IAiEditProposalPort
{
    Task<AiEditProposal> ProposeAsync(
        Guid projectId,
        Guid planId,
        Guid baseRevisionId,
        string customerInstruction,
        string correlationId,
        CancellationToken cancellationToken);
}
