using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.Planning;
using BuildMind.Application.Features.Survey.Services;
using P = BuildMind.API.Endpoints.Planning.PlanningEndpointModule;

namespace BuildMind.API.Endpoints.Planning;

/// <summary>
/// Completes the V3 register routes (05-API-CONTRACTS) not yet mapped by the Captain profile, reusing existing services:
/// survey extraction result (R-13), land validate/confirm (R-16/R-17), generation alternatives/select (R-29/R-30),
/// create plan from an alternative (R-31), edit validation without persistence (R-33), AI edit proposals (R-34; no
/// provider configured, answered truthfully), save a server-validated version (R-35), confirm alias (R-38), report list.
/// Development-only profile; production activation is NOT approved.
/// </summary>
public sealed class V3RouteCompletionEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        // R-06/R-07 GET/PUT /users/me: the existing M1 profile handlers (same authorization). Captain profile only, so
        // the ratified M1 route inventory of the normal host is unchanged.
        var me = apiV1.MapGroup("/users/me").WithTags("Profile").RequireAuthorization();
        me.MapGet("", ProfileEndpointModule.GetAsync).WithName("GetCurrentUserV3");
        me.MapPut("", ProfileEndpointModule.UpdateAsync).WithName("UpdateCurrentUserV3");

        var project =apiV1.MapGroup("/projects/{projectId}").WithTags("Planning").RequireAuthorization();
        project.MapGet("/surveys/{surveyId}/extraction", ExtractionAsync).WithName("GetSurveyExtraction");
        project.MapPost("/land/validate", ValidateLandAsync).WithName("ValidateProjectLand");
        project.MapPost("/land/confirm", P.PutLandAsync).WithName("ConfirmProjectLand");
        project.MapGet("/generation-jobs/{jobId}/alternatives", AlternativesAsync).WithName("GetGenerationAlternatives");
        project.MapPost("/generation-jobs/{jobId}/select", SelectAsync).WithName("SelectGenerationAlternative");
        project.MapPost("/plans", CreatePlanAsync).WithName("CreatePlanFromAlternative");
        project.MapPost("/plans/{planId}/edits/validate", ValidateEditAsync).WithName("ValidatePlanEdit");
        project.MapPost("/plans/{planId}/ai-edit/proposals", AiProposalAsync).WithName("ProposeAiPlanEdit").RequireExpensiveOperationLimit();
        project.MapPost("/plans/{planId}/versions", SaveVersionAsync).WithName("SavePlanVersion");
        project.MapPost("/plans/{planId}/confirm", PlanLifecycleEndpointModule.ConfirmAsync).WithName("ConfirmPlanV3");
        project.MapGet("/reports", ListReportsAsync).WithName("ListReports");
    }

    private const string ExtractionNotice =
        "Extracted survey values are unverified evidence for your review. They are not a legal survey, do not establish " +
        "boundary validity and are never used without your explicit confirmation of the land boundary.";

    private static async Task<IResult> ExtractionAsync(string projectId, string surveyId, HttpContext context, ICurrentUser user,
        SurveyStatusService surveys, ISurveyExtractionReader extractions, CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project) || !P.TryId(surveyId, out var survey)) return P.Invalid("id");
        if (user.UserId is null) return Problem(401, "UNAUTHENTICATED");
        // Ownership and existence through the existing M2 status service (missing and unauthorized → 404).
        var status = await surveys.GetAsync(project, survey, token);
        if (status is null) return Problem(404, "NOT_FOUND");
        var extraction = await extractions.GetAsync(project, survey, token);
        using var evidence = extraction is null ? null : JsonDocument.Parse(extraction.ExtractionJson);
        return Results.Ok(new
        {
            surveyId = survey, processingStatus = status.Status, status.FailureCode,
            extraction = extraction is null ? null : new
            {
                extraction.ExtractionId, outcome = extraction.Outcome, extraction.Method, extraction.CreatedAtUtc,
                evidence = evidence!.RootElement.Clone(),
            },
            notice = ExtractionNotice,
        });
    }

    private static async Task<IResult> ValidateLandAsync(string projectId, HttpContext context, LandService lands, CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project)) return P.Invalid("projectId");
        var body = await P.ReadAsync<PutLandHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var exterior = body.Value!.Exterior?.Select(p => p is null ? null! : new CoordinatePointDto { X = p.X, Y = p.Y }).ToArray();
        var result = await lands.ValidateAsync(project, exterior, token);
        return P.Map(result, check => new { check.Valid, check.Code, check.AreaM2, check.EdgeCount, persisted = false });
    }

    private static async Task<IResult> AlternativesAsync(string projectId, string jobId, HttpContext context, GenerationService generation,
        CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project) || !P.TryId(jobId, out var job)) return P.Invalid("id");
        // The job view already carries the ranked alternatives ("candidates") with their validation and notice.
        return P.Map(await generation.GetAsync(project, job, token), P.Job);
    }

    private static Task<IResult> SelectAsync(string projectId, string jobId, HttpContext context, PlanSaveService plans,
        CancellationToken token) => SaveSelectedAsync(projectId, jobId, context, plans, token);

    private static Task<IResult> CreatePlanAsync(string projectId, HttpContext context, PlanSaveService plans, CancellationToken token) =>
        SaveSelectedAsync(projectId, null, context, plans, token);

    private static async Task<IResult> SaveSelectedAsync(string projectId, string? routeJobId, HttpContext context,
        PlanSaveService plans, CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project)) return P.Invalid("projectId");
        var body = await P.ReadAsync<SelectAlternativeHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value!;
        Guid job;
        if (routeJobId is not null)
        {
            if (!P.TryId(routeJobId, out job)) return P.Invalid("jobId");
            if (request.JobId is { } bodyJob && bodyJob != job) return P.Invalid("jobId");
        }
        else if (request.JobId is not { } bodyJob || bodyJob == Guid.Empty) return P.Invalid("jobId");
        else job = bodyJob;
        if (request.AlternativeId is not { } alternative || alternative == Guid.Empty) return P.Invalid("alternativeId");
        var result = await plans.SaveCandidateAsync(project, job, alternative, new SaveCandidateCommand(request.ChangeSummary), token);
        return P.Map(result, P.Revision, created: true);
    }

    private static async Task<IResult> ValidateEditAsync(string projectId, string planId, HttpContext context, PlanVersionService versions,
        CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project) || !P.TryId(planId, out var plan)) return P.Invalid("id");
        var body = await ReadEditAsync(context, token);
        if (body.Failure is not null) return body.Failure;
        var result = await versions.ValidateEditAsync(project, plan, body.Value!, token);
        return P.Map(result, r => new
        {
            editStatus = r.Status, candidateGeometry = r.CandidateGeometry, r.GeometryHash, persisted = false,
            validation = r.Validation is null ? null : new
            {
                r.Validation.GeometryValid, r.Validation.RequirementsMatch, r.Validation.ValidatorVersion,
                diagnostics = r.Validation.Diagnostics.Select(d => new { d.Code, d.Severity, d.Message }),
            },
            editDiagnostics = r.EditDiagnostics,
            notice = "Validation only: nothing was saved. Use POST …/versions or the candidate workflow to save.",
        });
    }

    private static async Task<IResult> SaveVersionAsync(string projectId, string planId, HttpContext context, PlanVersionService versions,
        CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project) || !P.TryId(planId, out var plan)) return P.Invalid("id");
        if (!context.Request.HasJsonContentType()) return Problem(415, "UNSUPPORTED_MEDIA_TYPE");
        SaveVersionHttpRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<SaveVersionHttpRequest>(PlanEditorJson.StrictOptions, token); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return P.Invalid("request"); }
        if (request is null || request.ExpectedBaseRevisionId is not { } baseRevision) return P.Invalid("request");
        var result = await versions.SaveVersionAsync(project, plan, new SaveVersionCommand(baseRevision, request.Geometry, request.ChangeSummary), token);
        return P.Map(result, P.Revision, created: true);
    }

    private static async Task<IResult> AiProposalAsync(string projectId, string planId, HttpContext context, ICurrentUser user,
        AiEditOrchestrationService orchestration, CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project) || !P.TryId(planId, out var plan)) return P.Invalid("id");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var body = await P.ReadAsync<AiProposalHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value!;
        if (request.ExpectedBaseRevisionId is not { } baseRevision || string.IsNullOrWhiteSpace(request.Instruction)
            || request.Instruction.Length > 2000)
            return P.Invalid("request");
        var result = await orchestration.PreviewAsync(new AiEditPreviewCommand(project, plan, baseRevision, request.Instruction,
            context.TraceIdentifier), actor, token);
        return result.Status switch
        {
            AiEditOrchestrationStatus.ProposalUnavailable => Problem(503, "AI_PROVIDER_NOT_CONFIGURED"),
            AiEditOrchestrationStatus.Unauthorized or AiEditOrchestrationStatus.RevisionNotFound => Problem(404, "NOT_FOUND"),
            AiEditOrchestrationStatus.StaleBaseRevision or AiEditOrchestrationStatus.StaleInputs => Problem(409, "STALE_BASE_REVISION"),
            AiEditOrchestrationStatus.InvalidRequest => Problem(400, "VALIDATION_FAILED"),
            AiEditOrchestrationStatus.DependencyTimeout => Problem(504, "DEPENDENCY_TIMEOUT"),
            AiEditOrchestrationStatus.DependencyUnavailable or AiEditOrchestrationStatus.ValidationUnavailable => Problem(503, "DEPENDENCY_UNAVAILABLE"),
            // A created candidate requires a configured AI provider; none exists in this build, so this is an invariant breach.
            AiEditOrchestrationStatus.CandidateCreated => Problem(500, "INTERNAL_ERROR"),
            _ => Problem(422, PlanEditorErrorCodes.InvalidEditCommand),
        };
    }

    private static async Task<IResult> ListReportsAsync(string projectId, HttpContext context, PlanningAccess access, IReportCatalog reports,
        CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project)) return P.Invalid("projectId");
        var (status, _, _) = await access.AuthorizeAsync(project, token);
        if (status != PlanningStatus.Success) return P.Map(new PlanningResult<object>(status), o => o);
        return Results.Ok(new { reports = await reports.ListAsync(project, 100, token) });
    }

    private static async Task<(PlanEditRequestDto? Value, IResult? Failure)> ReadEditAsync(HttpContext context, CancellationToken token)
    {
        if (!context.Request.HasJsonContentType()) return (null, Problem(415, "UNSUPPORTED_MEDIA_TYPE"));
        try
        {
            var value = await context.Request.ReadFromJsonAsync<PlanEditRequestDto>(PlanEditorJson.StrictOptions, token);
            return value is null ? (null, P.Invalid("request")) : (value, null);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return (null, P.Invalid("request")); }
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        type: ProblemDetailsDefaults.GetTypeForCode(code),
        extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
}

public sealed record SelectAlternativeHttpRequest(Guid? JobId, Guid? AlternativeId, string? ChangeSummary);
/// <remarks>Read with the M3 strict (case-sensitive) options, so names are explicit.</remarks>
public sealed record SaveVersionHttpRequest(
    [property: JsonPropertyName("expectedBaseRevisionId")] Guid? ExpectedBaseRevisionId,
    [property: JsonPropertyName("geometry")] PlanGeometryDto? Geometry,
    [property: JsonPropertyName("changeSummary")] string? ChangeSummary);
public sealed record AiProposalHttpRequest(Guid? ExpectedBaseRevisionId, string? Instruction);
