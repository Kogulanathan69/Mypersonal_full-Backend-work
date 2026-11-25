using System.Text.Json;
using BuildMind.API.Composition;
using BuildMind.API.Endpoints.Administration;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Administration;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.CaptainIntegration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using P = BuildMind.API.Endpoints.Planning.PlanningEndpointModule;

namespace BuildMind.API.Endpoints.Planning;

/// <summary>
/// Captain completion routes (final order, phase 02): history, listing and recovery operations the approved workflows need
/// and the earlier route set lacked. Each is backed by data the platform already persists; no duplicate of an existing
/// operation is added (admin list oversight already exists as /admin/monitoring/{kind}).
/// Project routes: owner + authorization-version check (PlanningAccess) before any read; a foreign or unknown id is 404.
/// Session routes: the authenticated caller's own sessions only. Admin routes: fresh privilege read (AsAdmin).
/// </summary>
public sealed class CaptainCompletionEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var project = apiV1.MapGroup("/projects/{projectId}").WithTags("Planning").RequireAuthorization();

        project.MapGet("/surveys", (string projectId, HttpContext c, PlanningAccess a, CaptainReadModels r, CancellationToken t) =>
            Owned(projectId, c, a, t, async pid => Results.Ok(await r.SurveysAsync(pid, t))))
            .WithName("ListProjectSurveys").Produces<SurveyListDto>();
        project.MapGet("/land/revisions", (string projectId, HttpContext c, PlanningAccess a, CaptainReadModels r, CancellationToken t) =>
            Owned(projectId, c, a, t, async pid => Results.Ok(await r.LandRevisionsAsync(pid, t))))
            .WithName("ListLandRevisions").Produces<LandRevisionListDto>();
        project.MapGet("/land/revisions/{landRevisionId}", (string projectId, string landRevisionId, HttpContext c, PlanningAccess a,
            CaptainReadModels r, CancellationToken t) => Owned(projectId, c, a, t, async pid =>
                !P.TryId(landRevisionId, out var id) ? P.Invalid("landRevisionId")
                : await r.LandRevisionAsync(pid, id, t) is { } v ? Results.Ok(v) : NotFound()))
            .WithName("GetLandRevision").Produces<LandRevisionDetailDto>();
        project.MapGet("/requirements/revisions", (string projectId, HttpContext c, PlanningAccess a, CaptainReadModels r, CancellationToken t) =>
            Owned(projectId, c, a, t, async pid => Results.Ok(await r.RequirementRevisionsAsync(pid, t))))
            .WithName("ListRequirementRevisions").Produces<RequirementRevisionListDto>();
        project.MapGet("/requirements/revisions/{revisionId}", (string projectId, string revisionId, HttpContext c, PlanningAccess a,
            CaptainReadModels r, CancellationToken t) => Owned(projectId, c, a, t, async pid =>
                !P.TryId(revisionId, out var id) ? P.Invalid("revisionId")
                : await r.RequirementRevisionAsync(pid, id, t) is { } v ? Results.Ok(v) : NotFound()))
            .WithName("GetRequirementRevision").Produces<RequirementRevisionDetailDto>();
        project.MapGet("/generation-jobs", (string projectId, HttpContext c, PlanningAccess a, CaptainReadModels r, CancellationToken t) =>
            Owned(projectId, c, a, t, async pid => Results.Ok(await r.GenerationJobsAsync(pid, t))))
            .WithName("ListGenerationJobs").Produces<GenerationJobListDto>();
        project.MapGet("/plans/{planId}/candidates", (string projectId, string planId, HttpContext c, PlanningAccess a, CaptainReadModels r,
            CancellationToken t) => Owned(projectId, c, a, t, async pid =>
                !P.TryId(planId, out var plan) ? P.Invalid("planId") : Results.Ok(await r.CandidatesAsync(pid, plan, t))))
            .WithName("ListPlanEditCandidates").Produces<EditCandidateListDto>();
        project.MapGet("/plans/{planId}/candidates/{candidateId}", (string projectId, string planId, string candidateId, HttpContext c,
            PlanningAccess a, CaptainReadModels r, CancellationToken t) => Owned(projectId, c, a, t, async pid =>
                !P.TryId(planId, out var plan) || !P.TryId(candidateId, out var id) ? P.Invalid("id")
                : await r.CandidateAsync(pid, plan, id, t) is { } v ? Results.Ok(v) : NotFound()))
            .WithName("GetPlanEditCandidate").Produces<EditCandidateDetailDto>();
        project.MapGet("/plans/{planId}/confirmation", (string projectId, string planId, HttpContext c, PlanningAccess a, CaptainReadModels r,
            CancellationToken t) => Owned(projectId, c, a, t, async pid =>
                !P.TryId(planId, out var plan) ? P.Invalid("planId")
                : await r.ConfirmationAsync(pid, plan, t) is { } v ? Results.Ok(v) : NotFound()))
            .WithName("GetPlanConfirmation").Produces<PlanConfirmationDto>();
        project.MapPost("/plans/{planId}/revert", RevertAsync).WithName("RevertPlanToRevision");

        var me = apiV1.MapGroup("/users/me/sessions").WithTags("Profile").RequireAuthorization();
        me.MapGet("", async (HttpContext c, ICurrentUser u, CaptainReadModels r, CancellationToken t) =>
        {
            NoStore(c);
            return u.UserId is { } id ? Results.Ok(await r.SessionsAsync(id, t)) : Problem(401, "UNAUTHENTICATED");
        }).WithName("ListMySessions").Produces<SessionListDto>();
        me.MapDelete("/{sessionId}", async (string sessionId, HttpContext c, ICurrentUser u, CaptainReadModels r, CancellationToken t) =>
        {
            NoStore(c);
            if (u.UserId is not { } id) return Problem(401, "UNAUTHENTICATED");
            if (!P.TryId(sessionId, out var session)) return P.Invalid("sessionId");
            return await r.RevokeSessionAsync(id, session, t) ? Results.NoContent() : NotFound();
        }).WithName("RevokeMySession");

        var admin = apiV1.MapGroup("/admin").WithTags("Admin").RequireAuthorization();
        admin.MapGet("/projects/{projectId}", (string projectId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, CaptainReadModels r,
            CancellationToken t) => AdministrationEndpointModule.AsAdmin(c, u, d, t, async _ =>
                !P.TryId(projectId, out var pid) ? P.Invalid("projectId")
                : await r.AdminProjectAsync(pid, t) is { } v ? Results.Ok(v) : NotFound()))
            .WithName("GetAdminProject").Produces<AdminProjectDetailDto>();
        admin.MapGet("/system/workers", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, WorkerStatusRegistry w, CancellationToken t) =>
            AdministrationEndpointModule.AsAdmin(c, u, d, t, _ => Task.FromResult(Results.Ok(w.Snapshot()))))
            .WithName("GetWorkerStatus").Produces<WorkerStatusListDto>();
        admin.MapGet("/system/dependencies", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, HealthCheckService health,
            CancellationToken t) => AdministrationEndpointModule.AsAdmin(c, u, d, t, async _ =>
            {
                var report = await health.CheckHealthAsync(t);
                return Results.Ok(new DependencyReportDto(report.Status.ToString(), report.TotalDuration.TotalMilliseconds,
                    report.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new DependencyCheckDto(e.Key,
                        e.Value.Status.ToString(), e.Value.Description, e.Value.Duration.TotalMilliseconds)).ToList()));
            }))
            .WithName("GetDependencyHealth").Produces<DependencyReportDto>();
    }

    /// <summary>
    /// Undo/redo through revision semantics: a NEW head revision whose geometry equals an earlier revision of the same plan.
    /// It runs through the existing save-version path (expected-head check, server validation, atomic append), so history
    /// is never rewritten and a stale caller gets 409.
    /// </summary>
    private static async Task<IResult> RevertAsync(string projectId, string planId, HttpContext context, PlanningAccess access,
        IPlanRevisionStore revisions, PlanVersionService versions, CancellationToken token)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var project) || !P.TryId(planId, out var plan)) return P.Invalid("id");
        RevertPlanHttpRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<RevertPlanHttpRequest>(token); }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return P.Invalid("request"); }
        if (request?.ExpectedBaseRevisionId is not { } expected || request.TargetRevisionId is not { } target) return P.Invalid("request");
        var (status, _, _) = await access.AuthorizeAsync(project, token);
        if (status != PlanningStatus.Success) return P.Map(new PlanningResult<object>(status), o => o);
        var snapshot = await revisions.GetAsync(project, plan, target, token);
        if (snapshot is null) return NotFound();
        PlanGeometryDto? geometry;
        try { geometry = JsonSerializer.Deserialize<PlanGeometryDto>(snapshot.GeometryJson, PlanEditorJson.StrictOptions); }
        catch (JsonException) { return Problem(500, "STORED_REVISION_UNREADABLE"); }
        var result = await versions.SaveVersionAsync(project, plan,
            new SaveVersionCommand(expected, geometry, $"Reverted to version {snapshot.VersionNumber}"), token);
        return P.Map(result, P.Revision, created: true);
    }

    private static async Task<IResult> Owned(string projectId, HttpContext context, PlanningAccess access, CancellationToken token,
        Func<Guid, Task<IResult>> action)
    {
        NoStore(context);
        if (!P.TryId(projectId, out var pid)) return P.Invalid("projectId");
        var (status, _, _) = await access.AuthorizeAsync(pid, token);
        return status == PlanningStatus.Success ? await action(pid) : P.Map(new PlanningResult<object>(status), o => o);
    }

    private static IResult NotFound() => Problem(404, "NOT_FOUND");

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        type: ProblemDetailsDefaults.GetTypeForCode(code),
        extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
}

public sealed record RevertPlanHttpRequest(Guid? ExpectedBaseRevisionId, Guid? TargetRevisionId);
public sealed record DependencyCheckDto(string Name, string Status, string? Description, double DurationMs);
public sealed record DependencyReportDto(string Status, double TotalDurationMs, IReadOnlyList<DependencyCheckDto> Checks);
