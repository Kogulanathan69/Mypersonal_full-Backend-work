using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Planning;

public sealed record EditValidationResult(string Status, PlanGeometryDto? CandidateGeometry, string? GeometryHash,
    PlanCheckResult? Validation, IReadOnlyList<PlanEditDiagnosticDto> EditDiagnostics);

public sealed record SaveVersionCommand(Guid ExpectedBaseRevisionId, PlanGeometryDto? Geometry, string? ChangeSummary);

/// <summary>
/// V3 R-33 and R-35: validation-only edits (no persistence of any kind) and atomic saving of a client-proposed geometry
/// that the SERVER validates and for which the server issues evidence. Client-supplied validation is never trusted.
/// </summary>
public sealed class PlanVersionService(
    PlanningAccess access, IPlanRevisionStore revisions, ILandRevisionStore lands, IRequirementRevisionRepository requirements,
    IGenerationPolicyProvider policies, IPlanningComputeClient compute, IPlanEditorComputeClient editor, IPlanVersionAppendStore store,
    TimeProvider time)
{
    public async Task<PlanningResult<EditValidationResult>> ValidateEditAsync(Guid projectId, Guid planId, PlanEditRequestDto request,
        CancellationToken token)
    {
        var (status, _, _) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        try { PlanEditorContractValidator.Validate(request); }
        catch (ArgumentException) { return new(PlanningStatus.ValidationFailed, Code: "INVALID_EDIT_COMMAND"); }
        if (request.ProjectId != projectId || request.PlanId != planId)
            return new(PlanningStatus.ValidationFailed, Code: "ROUTE_BODY_MISMATCH");
        var (failure, context) = await ContextAsync(projectId, planId, request.BaseRevisionId, token).ConfigureAwait(false);
        if (failure is not null) return new(failure.Value.Status, Code: failure.Value.Code);
        var (head, land, requirement) = context!.Value;

        var source = JsonSerializer.Deserialize<PlanGeometryDto>(head.GeometryJson, PlanEditorJson.StrictOptions)
            ?? throw new InvalidDataException("Stored plan geometry is empty.");
        PlanEditResultDto computed;
        try
        {
            computed = await editor.PreviewAsync(new PlanEditorComputeRequest(source, request.Command, Guid.NewGuid().ToString("N")), token)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { return new(PlanningStatus.DependencyTimeout, Code: "DEPENDENCY_TIMEOUT"); }
        catch (Exception e) when (e is HttpRequestException or JsonException) { return new(PlanningStatus.DependencyUnavailable, Code: "DEPENDENCY_UNAVAILABLE"); }
        if (computed.CandidateGeometry is null)
            return PlanningResult<EditValidationResult>.Ok(new(computed.Status, null, null, null, computed.Diagnostics ?? []));

        var json = JsonSerializer.Serialize(computed.CandidateGeometry, PlanEditorJson.StrictOptions);
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var check = await PlanSaveService.ValidateAsync(compute, land, requirement, computed.CandidateGeometry,
            json, hash, SyntheticSetback(), token).ConfigureAwait(false);
        if (check.Status != PlanningStatus.Success) return new(check.Status, Code: check.Code);
        return PlanningResult<EditValidationResult>.Ok(new(computed.Status, computed.CandidateGeometry,
            hash, check.Value, computed.Diagnostics ?? []));
    }

    public async Task<PlanningResult<PlanRevisionSnapshot>> SaveVersionAsync(Guid projectId, Guid planId, SaveVersionCommand command,
        CancellationToken token)
    {
        var (status, actor, version) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        if (command.Geometry is not { } geometry || command.ExpectedBaseRevisionId == Guid.Empty || command.ChangeSummary is { Length: > 500 })
            return new(PlanningStatus.ValidationFailed, Code: "INVALID_VERSION_REQUEST");
        try { PlanEditorContractValidator.Validate(geometry); }
        catch (ArgumentException) { return new(PlanningStatus.ValidationFailed, Code: "INVALID_GEOMETRY_CONTRACT"); }
        var (failure, context) = await ContextAsync(projectId, planId, command.ExpectedBaseRevisionId, token).ConfigureAwait(false);
        if (failure is not null) return new(failure.Value.Status, Code: failure.Value.Code);
        var (head, land, requirement) = context!.Value;
        if (geometry.ProjectId != projectId || geometry.PlanId != planId || geometry.LandRevisionId != land.Id
            || geometry.RequirementsRevisionId != requirement.Id || geometry.SchemaVersion != head.GeometrySchemaVersion)
            return new(PlanningStatus.ValidationFailed, Code: "GEOMETRY_BINDING_MISMATCH");

        var setback = SyntheticSetback();
        var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var check = await PlanSaveService.ValidateAsync(compute, land, requirement, geometry,
            json, hash, setback, token).ConfigureAwait(false);
        if (check.Status != PlanningStatus.Success) return new(check.Status, Code: check.Code);
        if (!check.Value!.GeometryValid || check.Value.Diagnostics.Any(d => d.Severity == "CRITICAL"))
            return new(PlanningStatus.ValidationFailed, Code: "GEOMETRY_VALIDATION_FAILED", Diagnostics: check.Value.Diagnostics);
        var now = PlanningClock.Now(time);
        var evidence = PlanSaveService.IssueEvidence(projectId, planId, hash, check.Value,
            setback is null ? PlanningSchemas.UnverifiedEvidenceStatus : PlanningSchemas.SyntheticEvidenceStatus, now);
        var revision = new PlanRevisionSnapshot(Guid.NewGuid(), planId, projectId, head.RevisionId, checked(head.VersionNumber + 1),
            head.GeometrySchemaVersion, json, hash, land.Id, requirement.Id, evidence.Reference, null, actor, now,
            command.ChangeSummary ?? "Saved edited geometry (preliminary)");
        return await store.TryAppendWithEvidenceAsync(revision, evidence, version, token).ConfigureAwait(false) switch
        {
            ProtectedWriteStatus.Written => PlanningResult<PlanRevisionSnapshot>.Ok(revision),
            ProtectedWriteStatus.Conflict => new(PlanningStatus.Conflict, Code: "REVISION_CONFLICT"),
            _ => new(PlanningStatus.NotFound),
        };
    }

    private double? SyntheticSetback() =>
        policies.Get() is { EvidenceMode: PlanningSchemas.EvidenceModeSynthetic, SyntheticSetbackM: { } setback } ? setback : null;

    private async Task<((PlanningStatus Status, string Code)? Failure, (PlanRevisionSnapshot, LandRevision, RequirementRevision)? Context)>
        ContextAsync(Guid projectId, Guid planId, Guid baseRevisionId, CancellationToken token)
    {
        var head = await revisions.GetCurrentAsync(projectId, planId, token).ConfigureAwait(false);
        if (head is null) return ((PlanningStatus.NotFound, "NOT_FOUND"), null);
        if (head.RevisionId != baseRevisionId) return ((PlanningStatus.Conflict, "STALE_BASE_REVISION"), null);
        var land = await lands.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        var requirement = await requirements.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        if (land is null || requirement is null || land.Id != head.LandRevisionId || requirement.Id != head.RequirementsRevisionId)
            return ((PlanningStatus.StaleInputs, "STALE_INPUT_REVISIONS"), null);
        return (null, (head, land, requirement));
    }
}
