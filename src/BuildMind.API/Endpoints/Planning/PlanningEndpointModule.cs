using System.Text.Json;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;

namespace BuildMind.API.Endpoints.Planning;

/// <summary>
/// Captain planning routes (land boundary, generation jobs, saving a candidate as a plan). Registered only by the
/// Development-only Captain profile; production activation is NOT approved. Every response is <c>no-store</c>.
/// Results are PRELIMINARY 2D layouts: rectangles without openings or connectivity checks, never construction-ready.
/// </summary>
public sealed class PlanningEndpointModule : IApiEndpointModule
{
    public const string PreliminaryNotice =
        "Preliminary 2D layout for discussion only. Room rectangles without doors, windows or circulation checks; " +
        "not construction-ready, not reviewed by an architect and not a regulatory approval.";

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var project = apiV1.MapGroup("/projects/{projectId}").WithTags("Planning").RequireAuthorization();
        project.MapGet("/land", GetLandAsync).WithName("GetProjectLand");
        project.MapPut("/land", PutLandAsync).WithName("ReplaceProjectLand");
        project.MapPost("/generation-jobs", StartGenerationAsync).WithName("StartGeneration").RequireExpensiveOperationLimit();
        project.MapGet("/generation-jobs/{jobId}", GetGenerationAsync).WithName("GetGenerationJob");
        project.MapPost("/generation-jobs/{jobId}/candidates/{candidateId}/save", SaveCandidateAsync).WithName("SaveGeneratedCandidate");
    }

    private static async Task<IResult> GetLandAsync(string projectId, HttpContext context, LandService service, CancellationToken token)
    {
        NoStore(context);
        if (!TryId(projectId, out var project)) return Invalid("projectId");
        var result = await service.GetCurrentAsync(project, token);
        return result.Status == PlanningStatus.InputsMissing ? Results.NoContent() : Map(result, Land);
    }

    private static async Task<IResult> PutLandAsync(string projectId, HttpContext context, LandService service, CancellationToken token)
    {
        NoStore(context);
        if (!TryId(projectId, out var project)) return Invalid("projectId");
        var body = await ReadAsync<PutLandHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value!;
        if (request.Exterior is null) return Invalid("exterior");
        var result = await service.SubmitAsync(project, new SubmitLandCommand(
            request.Exterior.Select(p => p is null ? null! : new CoordinatePointDto { X = p.X, Y = p.Y }).ToArray(),
            request.ExpectedCurrentLandRevisionId, request.SurveyId), token);
        return Map(result, Land, created: true);
    }

    private static async Task<IResult> StartGenerationAsync(string projectId, HttpContext context, GenerationService service, CancellationToken token)
    {
        NoStore(context);
        if (!TryId(projectId, out var project)) return Invalid("projectId");
        var body = await ReadAsync<StartGenerationHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value!;
        var result = await service.StartAsync(project, new StartGenerationCommand(request.IdempotencyKey,
            request.ExpectedLandRevisionId ?? Guid.Empty, request.ExpectedRequirementsRevisionId ?? Guid.Empty, request.Seed), token);
        return Map(result, Job, created: true);
    }

    private static async Task<IResult> GetGenerationAsync(string projectId, string jobId, HttpContext context, GenerationService service,
        CancellationToken token)
    {
        NoStore(context);
        if (!TryId(projectId, out var project)) return Invalid("projectId");
        if (!TryId(jobId, out var job)) return Invalid("jobId");
        return Map(await service.GetAsync(project, job, token), Job);
    }

    private static async Task<IResult> SaveCandidateAsync(string projectId, string jobId, string candidateId, HttpContext context,
        PlanSaveService service, CancellationToken token)
    {
        NoStore(context);
        if (!TryId(projectId, out var project)) return Invalid("projectId");
        if (!TryId(jobId, out var job)) return Invalid("jobId");
        if (!TryId(candidateId, out var candidate)) return Invalid("candidateId");
        SaveCandidateHttpRequest? request = new(null);
        if (context.Request.ContentLength is > 0 || context.Request.HasJsonContentType())
        {
            var body = await ReadAsync<SaveCandidateHttpRequest>(context, token);
            if (body.Failure is not null) return body.Failure;
            request = body.Value;
        }
        var result = await service.SaveCandidateAsync(project, job, candidate, new SaveCandidateCommand(request!.ChangeSummary), token);
        return Map(result, Revision, created: true);
    }

    internal static IResult Map<T>(PlanningResult<T> result, Func<T, object> body, bool created = false)
    {
        if (result.Status == PlanningStatus.Success)
            return created ? Results.Json(body(result.Value!), statusCode: StatusCodes.Status201Created) : Results.Ok(body(result.Value!));
        var (status, code) = result.Status switch
        {
            PlanningStatus.Unauthenticated => (401, "UNAUTHENTICATED"),
            PlanningStatus.NotFound => (404, "NOT_FOUND"),
            PlanningStatus.ValidationFailed => (422, result.Code ?? "VALIDATION_FAILED"),
            PlanningStatus.Conflict => (409, result.Code ?? "CONFLICT"),
            PlanningStatus.StaleInputs => (409, result.Code ?? "STALE_INPUT_REVISIONS"),
            PlanningStatus.InputsMissing => (422, result.Code ?? "INPUTS_MISSING"),
            PlanningStatus.GenerationInProgress => (409, "GENERATION_IN_PROGRESS"),
            PlanningStatus.GenerationUnsuccessful => (422, "GENERATION_UNSUCCESSFUL"),
            PlanningStatus.PolicyUnavailable => (503, result.Code ?? "POLICY_UNAVAILABLE"),
            PlanningStatus.DependencyTimeout => (504, "DEPENDENCY_TIMEOUT"),
            _ => (503, "DEPENDENCY_UNAVAILABLE"),
        };
        var extensions = new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code };
        if (result.Status is PlanningStatus.ValidationFailed or PlanningStatus.GenerationUnsuccessful && result.Code is not null)
            extensions["reasonCode"] = result.Code;
        if (result.Diagnostics is { Count: > 0 } diagnostics)
            extensions["diagnostics"] = diagnostics.Select(d => new { d.Code, d.Severity, d.Message }).ToArray();
        if (result.Status == PlanningStatus.GenerationUnsuccessful && result.Value is GenerationJobView view)
            extensions["job"] = Job(view);
        return Results.Problem(statusCode: status, type: ProblemDetailsDefaults.GetTypeForCode(code), extensions: extensions);
    }

    private static object Land(LandRevision land) => new
    {
        landRevisionId = land.Id, land.ProjectId, land.RevisionNumber, exterior = land.Land.Boundary.Exterior,
        coordinateSystem = land.Land.CoordinateSystem, areaM2 = land.AreaM2, land.EdgeCount, land.SurveyId, land.CreatedAtUtc,
        provenance = "User-confirmed boundary in a local metric frame; not a cadastral survey verification.",
    };

    private static object Job(GenerationJobView view) => new
    {
        jobId = view.Job.Id, view.Job.ProjectId, status = view.Job.Status.ToString(), view.Job.Outcome, view.Job.ReasonCode,
        view.Job.AlgorithmVersion, view.Job.EvidenceMode, view.Job.EvidencePackId, view.Job.Seed, view.Job.LandRevisionId, view.Job.RequirementsRevisionId,
        view.Job.CreatedAtUtc, view.Job.CompletedAtUtc,
        diagnostics = view.Job.Diagnostics.Select(d => new { d.Code, d.Severity, d.Message }),
        notice = PreliminaryNotice,
        candidates = view.Candidates.Select(c => new
        {
            candidateId = c.Id, c.Ordinal, rank = c.Ordinal + 1, c.GeometryHash, geometry = c.Geometry, metrics = c.Metrics,
            validation = new
            {
                c.Validation.ValidationId, c.Validation.GeometryValid, c.Validation.RequirementsMatch,
                regulatoryEvidenceStatus = view.Job.EvidenceMode == PlanningSchemas.EvidenceModeSynthetic
                    ? PlanningSchemas.SyntheticEvidenceStatus : c.Validation.RegulatoryEvidenceStatus,
                c.Validation.HasBlockingIssues, c.Validation.ValidatorVersion,
                diagnostics = c.Validation.Diagnostics.Select(d => new { d.Code, d.Severity, d.Message }),
            },
            c.DiversitySignature,
        }),
    };

    private static object Revision(PlanRevisionSnapshot revision) => new
    {
        planId = revision.PlanId, revision.ProjectId, revisionId = revision.RevisionId, revision.VersionNumber, revision.GeometryHash,
        revision.LandRevisionId, revision.RequirementsRevisionId, validation = revision.Validation, revision.CreatedAtUtc,
        revision.ChangeSummary, notice = PreliminaryNotice,
    };

    private static async Task<(T? Value, IResult? Failure)> ReadAsync<T>(HttpContext context, CancellationToken token) where T : class
    {
        if (!context.Request.HasJsonContentType())
            return (null, Results.Problem(statusCode: 415, type: ProblemDetailsDefaults.GetTypeForCode("UNSUPPORTED_MEDIA_TYPE"),
                extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = "UNSUPPORTED_MEDIA_TYPE" }));
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(StrictJson, token);
            return value is null ? (null, Invalid("request")) : (value, null);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return (null, Invalid("request"));
        }
    }

    private static bool TryId(string value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;

    private static IResult Invalid(string field) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]> { [field] = ["A valid value is required."] });

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}

public sealed record PointHttpInput(double X, double Y);
public sealed record PutLandHttpRequest(PointHttpInput?[]? Exterior, Guid? ExpectedCurrentLandRevisionId, Guid? SurveyId);
public sealed record StartGenerationHttpRequest(string? IdempotencyKey, Guid? ExpectedLandRevisionId,
    Guid? ExpectedRequirementsRevisionId, int? Seed);
public sealed record SaveCandidateHttpRequest(string? ChangeSummary);
