using System.Text.Json;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.ArchitectRequests.Contracts;
using BuildMind.Application.Features.ArchitectRequests.Services;
using BuildMind.Application.Features.Reports.Services;

namespace BuildMind.API.Endpoints.Planning;

/// <summary>
/// Captain routes over the existing M5 report and architect candidates: generate a preliminary PDF from a confirmed
/// plan revision, read status, download privately, and create/list/read/withdraw architect requests with explicit
/// consent. Development-only profile; nothing is dispatched to external parties.
/// </summary>
public sealed class DeliverablesEndpointModule : IApiEndpointModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var project = apiV1.MapGroup("/projects/{projectId}").WithTags("Reports and Architect Requests").RequireAuthorization();
        project.MapPost("/reports", GenerateAsync).WithName("GenerateReport").RequireExpensiveOperationLimit();
        project.MapGet("/reports/{reportId}", StatusAsync).WithName("GetReportStatus");
        project.MapGet("/reports/{reportId}/download", DownloadAsync).WithName("DownloadReport");
        project.MapPost("/architect-requests", CreateRequestAsync).WithName("CreateArchitectRequest");
        project.MapGet("/architect-requests", ListRequestsAsync).WithName("ListArchitectRequests");
        project.MapGet("/architect-requests/{requestId}", GetRequestAsync).WithName("GetArchitectRequest");
        project.MapPost("/architect-requests/{requestId}/withdraw", WithdrawAsync).WithName("WithdrawArchitectRequest");
    }

    private static async Task<IResult> GenerateAsync(string projectId, HttpContext context, ICurrentUser user,
        ReportGenerationCandidate reports, ReportReadCandidate reader, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project)) return Invalid("projectId");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var body = await ReadAsync<GenerateReportHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var r = body.Value!;
        if (r.PlanId is not { } plan || plan == Guid.Empty || r.PlanRevisionId is not { } revision || revision == Guid.Empty
            || r.ConfirmationId is not { } confirmation || confirmation == Guid.Empty || string.IsNullOrWhiteSpace(r.GeometryHash)
            || r.ReportId == Guid.Empty)
            return Invalid("request");
        var reportId = r.ReportId ?? Guid.NewGuid(); // client-supplied report id makes the request idempotent
        var outcome = await reports.GenerateAsync(actor, new ReportGenerationRequest(project, reportId, plan, revision, confirmation, r.GeometryHash), token);
        if (outcome is ReportGenerationOutcome.Published or ReportGenerationOutcome.Existing)
        {
            var status = await reader.GetStatusAsync(actor, project, reportId, token);
            if (status.Outcome == ReportAccessOutcome.Available && status.Metadata is { } m)
                return Results.Json(new { reportId = m.ReportId, m.ProjectId, status = m.Status.ToString(), m.StateVersion,
                    existing = outcome == ReportGenerationOutcome.Existing, notice = CaptainReportNotice },
                    statusCode: outcome == ReportGenerationOutcome.Published ? 201 : 200);
            return Problem(404, "NOT_FOUND");
        }
        return outcome switch
        {
            ReportGenerationOutcome.Rejected => Problem(404, "NOT_FOUND"),
            ReportGenerationOutcome.Unavailable => Problem(503, "DEPENDENCY_UNAVAILABLE"),
            ReportGenerationOutcome.Failed => Problem(422, "REPORT_RENDER_FAILED"),
            _ => Problem(500, "INTERNAL_ERROR"),
        };
    }

    private const string CaptainReportNotice =
        "Preliminary planning report - not a construction drawing, not an architectural approval and not a regulatory approval.";

    private static async Task<IResult> StatusAsync(string projectId, string reportId, HttpContext context, ICurrentUser user,
        ReportReadCandidate reader, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project) || !Id(reportId, out var report)) return Invalid("id");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var status = await reader.GetStatusAsync(actor, project, report, token);
        return status.Outcome switch
        {
            ReportAccessOutcome.Available when status.Metadata is { } m =>
                Results.Ok(new { reportId = m.ReportId, m.ProjectId, status = m.Status.ToString(), m.StateVersion }),
            ReportAccessOutcome.Hidden => Problem(404, "NOT_FOUND"),
            _ => Problem(503, "DEPENDENCY_UNAVAILABLE"),
        };
    }

    private static async Task<IResult> DownloadAsync(string projectId, string reportId, HttpContext context, ICurrentUser user,
        ReportReadCandidate reader, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project) || !Id(reportId, out var report)) return Invalid("id");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var result = await reader.DownloadAsync(actor, project, report, token);
        return result.Outcome switch
        {
            ReportAccessOutcome.Available => Results.File(result.Bytes!, result.ContentType, result.FileName),
            ReportAccessOutcome.Hidden => Problem(404, "NOT_FOUND"),
            ReportAccessOutcome.NotReady => Problem(409, "REPORT_NOT_READY"),
            _ => Problem(503, "DEPENDENCY_UNAVAILABLE"),
        };
    }

    private static async Task<IResult> CreateRequestAsync(string projectId, HttpContext context, ICurrentUser user,
        ArchitectWorkflowCandidate architects, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project)) return Invalid("projectId");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var body = await ReadAsync<CreateArchitectRequestHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        var r = body.Value!;
        var result = await architects.CreateAsync(actor, project, new CreateArchitectRequestDto(r.RecipientId ?? Guid.Empty, r.ReportId,
            r.DocumentIds ?? [], r.ExplicitConsent == true, r.AcknowledgedPolicyVersion), token);
        return Map(result.Outcome, result.Request, created: true);
    }

    private static async Task<IResult> ListRequestsAsync(string projectId, HttpContext context, ICurrentUser user,
        ArchitectWorkflowCandidate architects, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project)) return Invalid("projectId");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var result = await architects.ListAsync(actor, project, token);
        return result.Outcome == ArchitectOutcome.Available
            ? Results.Ok(new { requests = result.Requests!.Select(View) })
            : Map(result.Outcome, null);
    }

    private static async Task<IResult> GetRequestAsync(string projectId, string requestId, HttpContext context, ICurrentUser user,
        ArchitectWorkflowCandidate architects, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project) || !Id(requestId, out var request)) return Invalid("id");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var result = await architects.GetAsync(actor, project, request, token);
        return Map(result.Outcome, result.Request);
    }

    private static async Task<IResult> WithdrawAsync(string projectId, string requestId, HttpContext context, ICurrentUser user,
        ArchitectWorkflowCandidate architects, CancellationToken token)
    {
        NoStore(context);
        if (!Id(projectId, out var project) || !Id(requestId, out var request)) return Invalid("id");
        if (user.UserId is not { } actor) return Problem(401, "UNAUTHENTICATED");
        var body = await ReadAsync<WithdrawArchitectRequestHttpRequest>(context, token);
        if (body.Failure is not null) return body.Failure;
        if (body.Value!.ExpectedStateVersion is not { } version) return Invalid("expectedStateVersion");
        var result = await architects.WithdrawAsync(actor, project, request, version, token);
        return Map(result.Outcome, result.Request);
    }

    private static IResult Map(ArchitectOutcome outcome, ArchitectRequestDto? request, bool created = false) => outcome switch
    {
        ArchitectOutcome.Available when request is not null => created ? Results.Json(View(request), statusCode: 201) : Results.Ok(View(request)),
        ArchitectOutcome.Hidden => Problem(404, "NOT_FOUND"),
        ArchitectOutcome.Invalid => Problem(422, "EXPLICIT_CONSENT_REQUIRED"),
        ArchitectOutcome.Conflict => Problem(409, "CONFLICT"),
        ArchitectOutcome.NotEligible => Problem(422, "NOT_ELIGIBLE"),
        ArchitectOutcome.Unavailable => Problem(503, "DEPENDENCY_UNAVAILABLE"),
        _ => Problem(500, "INTERNAL_ERROR"),
    };

    private static object View(ArchitectRequestDto r) => new
    {
        requestId = r.Id, r.ProjectId, r.RecipientId, r.ReportId, r.DocumentIds, r.PolicyVersion, r.ConsentedAtUtc, r.RequestedAtUtc,
        r.StateVersion, status = r.Status.ToString(), r.WithdrawnAtUtc,
        delivery = "Recorded only. No project data has been sent to any external party by this development build.",
    };

    private static async Task<(T? Value, IResult? Failure)> ReadAsync<T>(HttpContext context, CancellationToken token) where T : class
    {
        if (!context.Request.HasJsonContentType()) return (null, Problem(415, "UNSUPPORTED_MEDIA_TYPE"));
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

    private static bool Id(string value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;
    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
    private static IResult Invalid(string field) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]> { [field] = ["A valid value is required."] });
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        type: ProblemDetailsDefaults.GetTypeForCode(code), detail: status == 500 ? ProblemDetailsDefaults.InternalErrorDetail : null,
        extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
}

public sealed record GenerateReportHttpRequest(Guid? ReportId, Guid? PlanId, Guid? PlanRevisionId, Guid? ConfirmationId, string? GeometryHash);
public sealed record CreateArchitectRequestHttpRequest(Guid? RecipientId, Guid? ReportId, Guid[]? DocumentIds, bool? ExplicitConsent,
    string? AcknowledgedPolicyVersion);
public sealed record WithdrawArchitectRequestHttpRequest(long? ExpectedStateVersion);
