using System.Text.Json;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;

namespace BuildMind.API.Endpoints.Planning;

/// <summary>
/// Captain routes completing the M3 plan lifecycle: list a project's plans, read a plan's current revision, and
/// confirm the exact current revision (existing <see cref="FinalPlanConfirmationService"/>). Development-only profile.
/// </summary>
public sealed class PlanLifecycleEndpointModule : IApiEndpointModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var project = apiV1.MapGroup("/projects/{projectId}").WithTags("Plan Lifecycle").RequireAuthorization();
        project.MapGet("/plans", ListPlansAsync).WithName("ListProjectPlans");
        project.MapGet("/plans/{planId}", GetPlanAsync).WithName("GetCurrentPlan");
        project.MapPost("/plans/{planId}/confirmation", ConfirmAsync).WithName("ConfirmPlan").Accepts<ConfirmPlanHttpRequest>("application/json").Produces(201);
    }

    private static async Task<IResult> ListPlansAsync(string projectId, HttpContext context, PlanningAccess access,
        IPlanCatalog catalog, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(projectId, out var project) || project == Guid.Empty) return Invalid("projectId");
        var (status, _, _) = await access.AuthorizeAsync(project, token);
        if (status != PlanningStatus.Success) return PlanningEndpointModule.Map(new PlanningResult<object>(status), o => o);
        var plans = await catalog.ListAsync(project, token);
        return Results.Ok(new { plans, notice = PlanningEndpointModule.PreliminaryNotice });
    }

    private static async Task<IResult> GetPlanAsync(string projectId, string planId, HttpContext context, PlanningAccess access,
        IPlanRevisionStore revisions, IPlanCatalog catalog, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(projectId, out var project) || project == Guid.Empty) return Invalid("projectId");
        if (!Guid.TryParse(planId, out var plan) || plan == Guid.Empty) return Invalid("planId");
        var (status, _, _) = await access.AuthorizeAsync(project, token);
        if (status != PlanningStatus.Success) return PlanningEndpointModule.Map(new PlanningResult<object>(status), o => o);
        var head = await revisions.GetCurrentAsync(project, plan, token);
        if (head is null) return PlanningEndpointModule.Map(new PlanningResult<object>(PlanningStatus.NotFound), o => o);
        var confirmation = (await catalog.ListAsync(project, token)).FirstOrDefault(p => p.PlanId == plan)?.Confirmation;
        return Results.Ok(new
        {
            head.PlanId, head.ProjectId, head.RevisionId, head.VersionNumber, head.GeometryHash,
            geometry = JsonDocument.Parse(head.GeometryJson).RootElement, head.LandRevisionId, head.RequirementsRevisionId,
            validation = head.Validation, head.CreatedAtUtc, head.ChangeSummary, confirmation,
            notice = PlanningEndpointModule.PreliminaryNotice,
        });
    }

    internal static async Task<IResult> ConfirmAsync(string projectId, string planId, HttpContext context, ICurrentUser user,
        FinalPlanConfirmationService confirmations, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(projectId, out var project) || project == Guid.Empty) return Invalid("projectId");
        if (!Guid.TryParse(planId, out var plan) || plan == Guid.Empty) return Invalid("planId");
        if (user.UserId is not { } actor || actor == Guid.Empty)
            return Problem(401, "UNAUTHENTICATED");
        if (!context.Request.HasJsonContentType()) return Problem(415, "UNSUPPORTED_MEDIA_TYPE");
        ConfirmPlanHttpRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<ConfirmPlanHttpRequest>(StrictJson, token); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) { return Invalid("request"); }
        if (request is null || request.RevisionId is not { } revision || revision == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ExpectedGeometryHash) || request.AcknowledgedWarnings is null)
            return Invalid("request");

        var result = await confirmations.ConfirmAsync(new ConfirmPlanCommand(project, plan, revision,
            request.ExpectedGeometryHash, request.AcknowledgedWarnings.Value), actor, token);
        if (result.Status == ConfirmationStatus.Confirmed && result.Confirmation is { } c)
            return Results.Json(new
            {
                c.ConfirmationId, c.ProjectId, c.PlanId, c.ConfirmedRevisionId, c.GeometryHash, c.ValidationId, c.ConfirmedAtUtc,
                c.WarningAcknowledgement, publication = result.PublicationStatus.ToString(),
                notice = "Customer confirmation of a preliminary 2D layout. This is not an architectural approval, a structural " +
                         "assessment or a regulatory/planning approval.",
            }, statusCode: StatusCodes.Status201Created);
        return result.Status switch
        {
            ConfirmationStatus.Unauthorized or ConfirmationStatus.RevisionNotFound => Problem(404, "NOT_FOUND"),
            ConfirmationStatus.GeometryHashMismatch => Problem(409, "GEOMETRY_HASH_MISMATCH"),
            ConfirmationStatus.NotCurrentRevision => Problem(409, "NOT_CURRENT_REVISION"),
            ConfirmationStatus.StaleInputs => Problem(409, "STALE_INPUT_REVISIONS"),
            ConfirmationStatus.AlreadyConfirmed => Problem(409, "ALREADY_CONFIRMED"),
            ConfirmationStatus.ValidationStale => Problem(409, "VALIDATION_STALE"),
            ConfirmationStatus.BlockingIssues => Problem(422, "BLOCKING_ISSUES"),
            ConfirmationStatus.WarningAcknowledgementRequired => Problem(422, "WARNING_ACKNOWLEDGEMENT_REQUIRED"),
            ConfirmationStatus.DependencyTimeout => Problem(504, "DEPENDENCY_TIMEOUT"),
            ConfirmationStatus.DependencyUnavailable => Problem(503, "DEPENDENCY_UNAVAILABLE"),
            _ => Problem(500, ProblemDetailsDefaults.GetDefaultCode(500)!),
        };
    }

    private static IResult Invalid(string field) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]> { [field] = ["A valid value is required."] });

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        type: ProblemDetailsDefaults.GetTypeForCode(code),
        detail: status == 500 ? ProblemDetailsDefaults.InternalErrorDetail : null,
        extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
}

public sealed record ConfirmPlanHttpRequest(Guid? RevisionId, string? ExpectedGeometryHash, bool? AcknowledgedWarnings);
