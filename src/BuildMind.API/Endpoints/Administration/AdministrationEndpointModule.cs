using System.Text.Json;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Administration;
using P = BuildMind.API.Endpoints.Planning.PlanningEndpointModule;

namespace BuildMind.API.Endpoints.Administration;

/// <summary>
/// Admin and delegated-staff backend (Captain profile, Development only; proposed role model, not ratified).
/// Every request re-reads roles, staff scopes and account state from the database: privileges are never taken from the
/// token, so suspension or scope removal takes effect on the next request (and also revokes sessions).
/// Errors: 401 unauthenticated; 403 <c>PERMISSION_DENIED</c> for an authenticated caller without the privilege.
/// </summary>
public sealed class AdministrationEndpointModule : IApiEndpointModule
{
    private static readonly IReadOnlyDictionary<string, string> MonitoringModules = new Dictionary<string, string>
    {
        ["projects"] = AdminModules.Projects,
        ["generation-jobs"] = AdminModules.Projects,
        ["surveys"] = AdminModules.Surveys,
        ["reports"] = AdminModules.Reports,
        ["architect-requests"] = AdminModules.ArchitectRequests,
    };

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var admin = apiV1.MapGroup("/admin").WithTags("Admin").RequireAuthorization();
        admin.MapGet("/modules", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, CancellationToken t) =>
            AsAdmin(c, u, d, t, _ => Task.FromResult(Results.Ok(new { roles = PrincipalRoles.All.Order(), modules = AdminModules.All.Order() }))));
        admin.MapGet("/dashboard", AdminDashboardAsync);
        admin.MapGet("/customers", ListCustomersAsync);
        admin.MapGet("/accounts", ListAccountsAsync);
        admin.MapGet("/accounts/{userId}", GetAccountAsync);
        admin.MapPost("/accounts/{userId}/suspend", (string userId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            CancellationToken t) => SetActiveAsync(userId, false, c, u, d, s, t)).Accepts<ReasonRequest>("application/json").Produces(200);
        admin.MapPost("/accounts/{userId}/activate", (string userId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            CancellationToken t) => SetActiveAsync(userId, true, c, u, d, s, t)).Accepts<ReasonRequest>("application/json").Produces(200);
        admin.MapPost("/accounts/{userId}/roles", SetRoleAsync).Accepts<RoleRequest>("application/json").Produces(200);
        admin.MapPost("/accounts/{userId}/invitation", ResendInvitationAsync);
        admin.MapGet("/staff", ListStaffAsync);
        admin.MapPost("/staff", InviteStaffAsync).Accepts<InviteStaffRequest>("application/json").Produces(201);
        admin.MapPut("/staff/{userId}/modules", SetModulesAsync).Accepts<ModulesRequest>("application/json").Produces(200);
        admin.MapGet("/proposals", AdminProposalsAsync);
        admin.MapPost("/proposals/{proposalId}/approve", (string proposalId, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
            IAdministrationStore s, CancellationToken t) => ReviewAsync(proposalId, true, c, u, d, s, t)).Accepts<ReviewRequest>("application/json").Produces(200);
        admin.MapPost("/proposals/{proposalId}/reject", (string proposalId, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
            IAdministrationStore s, CancellationToken t) => ReviewAsync(proposalId, false, c, u, d, s, t)).Accepts<ReviewRequest>("application/json").Produces(200);
        admin.MapGet("/tasks", AdminTasksAsync);
        admin.MapPost("/tasks", CreateTaskAsync).Accepts<CreateTaskRequest>("application/json").Produces(201);
        admin.MapGet("/tasks/{taskId}", (string taskId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            CancellationToken t) => TaskDetailAsync(taskId, true, c, u, d, s, t));
        admin.MapPost("/tasks/{taskId}/status", (string taskId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            CancellationToken t) => MoveTaskAsync(taskId, true, c, u, d, s, t)).Accepts<MoveTaskRequest>("application/json").Produces(200);
        admin.MapGet("/policies", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
            AsAdmin(c, u, d, t, async _ => Results.Ok(new { policies = await s.ListPoliciesAsync(t) })));
        admin.MapPut("/policies/{key}", SetPolicyAsync).Accepts<PolicyRequest>("application/json").Produces(200);
        admin.MapGet("/monitoring/{kind}", (string kind, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            int? page, int? pageSize, string? status, string? sort, CancellationToken t) =>
            AsAdmin(c, u, d, t, _ => MonitoringAsync(kind, s, page, pageSize, status, sort, t)));
        admin.MapGet("/audit", AuditAsync);
        admin.MapGet("/audit/verify", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
            AsAdmin(c, u, d, t, async _ => Results.Ok(await s.VerifyAuditChainAsync(t))));

        // Requirement catalogue (persistent, versioned; product preferences, not building regulations).
        admin.MapGet("/catalogues", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IRequirementCatalogueAdministration s, CancellationToken t) =>
            AsAdmin(c, u, d, t, async _ => Results.Ok(new { versions = await s.ListAsync(t) })));
        admin.MapGet("/catalogues/{version}", (string version, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
            IRequirementCatalogueAdministration s, CancellationToken t) =>
            AsAdmin(c, u, d, t, async _ => await s.GetAsync(version, t) is { } found
                ? Results.Ok(new { version = found.View, document = JsonDocument.Parse(found.Document).RootElement }) : Problem(404, "NOT_FOUND")));
        admin.MapPost("/catalogues", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IRequirementCatalogueAdministration s, CancellationToken t) =>
            AsAdmin(c, u, d, t, async p =>
            {
                var body = await P.ReadAsync<CatalogueImportRequest>(c, t);
                if (body.Failure is not null) return body.Failure;
                var r = body.Value!;
                if (r.Version is null || r.Document is not { ValueKind: JsonValueKind.Object } document) return P.Invalid("document");
                return Map(await s.CreateDraftAsync(p.UserId, r.Version, r.Provenance ?? "", document.GetRawText(), t), x => x, created: true);
            })).Accepts<CatalogueImportRequest>("application/json").Produces(201);
        admin.MapPost("/catalogues/{version}/publish", (string version, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
            IRequirementCatalogueAdministration s, CancellationToken t) => CatalogueTransitionAsync(version, "PUBLISHED", c, u, d, s, t)).Accepts<CatalogueTransitionRequest>("application/json").Produces(200);
        admin.MapPost("/catalogues/{version}/retire", (string version, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
            IRequirementCatalogueAdministration s, CancellationToken t) => CatalogueTransitionAsync(version, "RETIRED", c, u, d, s, t)).Accepts<CatalogueTransitionRequest>("application/json").Produces(200);

        var staff = apiV1.MapGroup("/staff").WithTags("Staff").RequireAuthorization();
        staff.MapGet("/dashboard", StaffDashboardAsync);
        staff.MapGet("/monitoring/{kind}", (string kind, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            int? page, int? pageSize, string? status, string? sort, CancellationToken t) =>
            AsStaff(c, u, d, t, p => MonitoringModules.TryGetValue(kind, out var module) && p.CanView(module)
                ? MonitoringAsync(kind, s, page, pageSize, status, sort, t) : Task.FromResult(Denied())));
        staff.MapGet("/policies", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
            AsStaff(c, u, d, t, async p => p.CanView(AdminModules.SystemPolicies)
                ? Results.Ok(new { policies = await s.ListPoliciesAsync(t) }) : Denied()));
        staff.MapPost("/proposals", SubmitProposalAsync).Accepts<ProposalRequest>("application/json").Produces(201);
        staff.MapGet("/proposals", StaffProposalsAsync);
        staff.MapPost("/proposals/{proposalId}/withdraw", WithdrawAsync).Accepts<ReviewRequest>("application/json").Produces(200);
        staff.MapGet("/tasks", StaffTasksAsync);
        staff.MapGet("/tasks/{taskId}", (string taskId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            CancellationToken t) => TaskDetailAsync(taskId, false, c, u, d, s, t));
        staff.MapPost("/tasks/{taskId}/status", (string taskId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
            CancellationToken t) => MoveTaskAsync(taskId, false, c, u, d, s, t)).Accepts<MoveTaskRequest>("application/json").Produces(200);
        staff.MapPost("/tasks/{taskId}/notes", AddNoteAsync).Accepts<NoteRequest>("application/json").Produces(201);
        staff.MapPost("/tasks/{taskId}/project-review", ProjectReviewAsync).Accepts<PurposeRequest>("application/json").Produces(200);
    }

    // ---------------------------------------------------------------- authorization helpers

    internal static async Task<IResult> AsAdmin(HttpContext context, ICurrentUser user, IPrincipalDirectory directory, CancellationToken token,
        Func<PrincipalContext, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (user.UserId is not { } id) return Problem(401, "UNAUTHENTICATED");
        var principal = await directory.GetAsync(id, token);
        return principal is { IsAdmin: true } ? await action(principal) : Denied();
    }

    internal static async Task<IResult> AsStaff(HttpContext context, ICurrentUser user, IPrincipalDirectory directory, CancellationToken token,
        Func<PrincipalContext, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (user.UserId is not { } id) return Problem(401, "UNAUTHENTICATED");
        var principal = await directory.GetAsync(id, token);
        return principal is { IsStaff: true } ? await action(principal) : Denied();
    }

    internal static IResult Denied() => Problem(403, "PERMISSION_DENIED");

    internal static IResult Problem(int status, string code, object? extra = null)
    {
        var extensions = new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code };
        if (extra is not null) extensions["details"] = extra;
        return Results.Problem(statusCode: status, type: ProblemDetailsDefaults.GetTypeForCode(code), extensions: extensions);
    }

    internal static IResult Map<T>(AdminResult<T> result, Func<T, object> body, bool created = false) => result.Status switch
    {
        AdminStatus.Success => created ? Results.Json(body(result.Value!), statusCode: 201) : Results.Ok(body(result.Value!)),
        AdminStatus.NotFound => Problem(404, result.Code ?? "NOT_FOUND"),
        AdminStatus.Forbidden => Problem(403, result.Code ?? "PERMISSION_DENIED"),
        AdminStatus.Conflict => Problem(409, result.Code ?? "CONFLICT"),
        _ => Problem(422, result.Code ?? "VALIDATION_FAILED"),
    };

    private static PageRequest Page(int? page, int? pageSize, string? search, string? status, string? sort) =>
        PageRequest.Normalize(page, pageSize, search, status, sort);

    private static object PageBody<T>(PagedResult<T> page) => new { items = page.Items, page = page.Page, pageSize = page.PageSize, total = page.Total };

    // ---------------------------------------------------------------- admin

    private static Task<IResult> AdminDashboardAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(await s.GetStatisticsAsync(t)));

    private static Task<IResult> ListCustomersAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? search, string? status, string? sort, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(PageBody(await s.ListAccountsAsync(Page(page, pageSize, search, status, sort), true, t))));

    private static Task<IResult> ListAccountsAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? search, string? status, string? sort, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(PageBody(await s.ListAccountsAsync(Page(page, pageSize, search, status, sort), false, t))));

    private static Task<IResult> GetAccountAsync(string userId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => !P.TryId(userId, out var id) ? P.Invalid("userId")
            : await s.GetAccountAsync(id, t) is { } detail ? Results.Ok(detail) : Problem(404, "NOT_FOUND"));

    private static Task<IResult> SetActiveAsync(string userId, bool active, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
        IAdministrationStore s, CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            if (!P.TryId(userId, out var id)) return P.Invalid("userId");
            var body = await P.ReadAsync<ReasonRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            return Map(await s.SetAccountActiveAsync(p.UserId, id, active, body.Value!.Reason ?? "", t), a => a);
        });

    private static Task<IResult> SetRoleAsync(string userId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            if (!P.TryId(userId, out var id)) return P.Invalid("userId");
            var body = await P.ReadAsync<RoleRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.Role is not { } role || body.Value.Granted is not { } granted) return P.Invalid("role");
            return Map(await s.SetRoleAsync(p.UserId, id, role, granted, t), changed => new { role, granted, changed });
        });

    private static Task<IResult> ListStaffAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? search, string? status, string? sort, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(PageBody(await s.ListStaffAsync(Page(page, pageSize, search, status, sort), t))));

    private static Task<IResult> InviteStaffAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, StaffInvitationService invitations,
        CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            var body = await P.ReadAsync<InviteStaffRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            var r = body.Value!;
            return Map(await invitations.InviteAsync(p.UserId, r.Email ?? "", r.FullName, r.Modules ?? [], t),
                x => new { x.UserId, x.InvitationSent, x.InvitationExpiresAtUtc }, created: true);
        });

    private static Task<IResult> ResendInvitationAsync(string userId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        StaffInvitationService invitations, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ =>
        {
            if (!P.TryId(userId, out var id)) return P.Invalid("userId");
            var account = await s.GetAccountAsync(id, t);
            if (account is null || !account.Account.Roles.Contains(PrincipalRoles.Staff)) return Problem(404, "NOT_FOUND");
            if (!account.Account.IsActive) return Problem(409, "ACCOUNT_SUSPENDED");
            var (sent, expires) = await invitations.SendSetPasswordAsync(id, account.Account.Email, t);
            return Results.Ok(new { invitationSent = sent, invitationExpiresAtUtc = expires });
        });

    private static Task<IResult> SetModulesAsync(string userId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            if (!P.TryId(userId, out var id)) return P.Invalid("userId");
            var body = await P.ReadAsync<ModulesRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.ExpectedScopeVersion is not { } version) return P.Invalid("expectedScopeVersion");
            return Map(await s.SetStaffModulesAsync(p.UserId, id, body.Value.Modules ?? [], version, t), x => x);
        });

    private static Task<IResult> AdminProposalsAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? status, string? sort, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(PageBody(await s.ListProposalsAsync(Page(page, pageSize, null, status, sort), null, null, t))));

    private static Task<IResult> ReviewAsync(string proposalId, bool approve, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
        IAdministrationStore s, CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            if (!P.TryId(proposalId, out var id)) return P.Invalid("proposalId");
            var body = await P.ReadAsync<ReviewRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.ExpectedRowVersion is not { } version) return P.Invalid("expectedRowVersion");
            var result = await s.ReviewProposalAsync(p.UserId, id, approve, version, body.Value.Note, t);
            // A stale proposal is recorded as STALE (live state unchanged) and reported as a conflict.
            return result is { Status: AdminStatus.Success, Value.Status: ProposalStatuses.Stale }
                ? Problem(409, "STALE_PROPOSAL", result.Value) : Map(result, x => x);
        });

    private static Task<IResult> AdminTasksAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? status, string? sort, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(PageBody(await s.ListTasksAsync(Page(page, pageSize, null, status, sort), null, t))));

    private static Task<IResult> CreateTaskAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            var body = await P.ReadAsync<CreateTaskRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            var r = body.Value!;
            if (r.AssigneeId is not { } assignee) return P.Invalid("assigneeId");
            return Map(await s.CreateTaskAsync(p.UserId, assignee, r.Title ?? "", r.Description, r.Module ?? "", r.ProjectId, t), x => x, created: true);
        });

    private static Task<IResult> SetPolicyAsync(string key, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            var body = await P.ReadAsync<PolicyRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.Value is not { ValueKind: not JsonValueKind.Undefined } value) return P.Invalid("value");
            return Map(await s.SetPolicyAsync(p.UserId, key, value.GetRawText(), body.Value.ExpectedVersion, t), x => x);
        });

    private static async Task<IResult> MonitoringAsync(string kind, IAdministrationStore s, int? page, int? pageSize, string? status, string? sort,
        CancellationToken t) =>
        MonitoringModules.ContainsKey(kind)
            ? Results.Ok(PageBody(await s.ListMonitoringAsync(kind, Page(page, pageSize, null, status, sort), t)))
            : Problem(404, "NOT_FOUND");

    private static Task<IResult> AuditAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? action, CancellationToken t) =>
        AsAdmin(c, u, d, t, async _ => Results.Ok(PageBody(await s.ListAuditAsync(Page(page, pageSize, action, null, null), t))));

    private static Task<IResult> CatalogueTransitionAsync(string version, string target, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
        IRequirementCatalogueAdministration s, CancellationToken t) =>
        AsAdmin(c, u, d, t, async p =>
        {
            var body = await P.ReadAsync<CatalogueTransitionRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.ExpectedRowVersion is not { } rowVersion) return P.Invalid("expectedRowVersion");
            return Map(await s.TransitionAsync(p.UserId, version, target, rowVersion, body.Value.EffectiveFromUtc, t), x => x);
        });

    // ---------------------------------------------------------------- staff

    private static Task<IResult> StaffDashboardAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
        AsStaff(c, u, d, t, async p =>
        {
            var open = await s.ListTasksAsync(Page(1, 1, null, TaskStatuses.Open, null), p.UserId, t);
            var inProgress = await s.ListTasksAsync(Page(1, 1, null, TaskStatuses.InProgress, null), p.UserId, t);
            var escalated = await s.ListTasksAsync(Page(1, 1, null, TaskStatuses.Escalated, null), p.UserId, t);
            var pending = await s.ListProposalsAsync(Page(1, 1, null, ProposalStatuses.Pending, null), p.UserId, null, t);
            return Results.Ok(new
            {
                modules = p.StaffModules.Order(),
                tasks = new { open = open.Total, inProgress = inProgress.Total, escalated = escalated.Total },
                pendingProposals = pending.Total,
            });
        });

    private static Task<IResult> SubmitProposalAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s, CancellationToken t) =>
        AsStaff(c, u, d, t, async p =>
        {
            var body = await P.ReadAsync<ProposalRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            var r = body.Value!;
            if (r.Module is not { } module || !p.StaffModules.Contains(module)) return Denied();
            var payload = r.Payload is { ValueKind: not JsonValueKind.Undefined } element ? element.GetRawText() : null;
            return Map(await s.SubmitProposalAsync(p.UserId, module,
                new SubmitProposalCommand(r.TargetType, r.TargetKey, r.ChangeType, r.BaseVersion, payload), t), x => x, created: true);
        });

    private static Task<IResult> StaffProposalsAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? status, CancellationToken t) =>
        AsStaff(c, u, d, t, async p => Results.Ok(PageBody(await s.ListProposalsAsync(Page(page, pageSize, null, status, null), p.UserId, null, t))));

    private static Task<IResult> WithdrawAsync(string proposalId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsStaff(c, u, d, t, async p =>
        {
            if (!P.TryId(proposalId, out var id)) return P.Invalid("proposalId");
            var body = await P.ReadAsync<ReviewRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.ExpectedRowVersion is not { } version) return P.Invalid("expectedRowVersion");
            return Map(await s.WithdrawProposalAsync(p.UserId, id, version, t), x => x);
        });

    private static Task<IResult> StaffTasksAsync(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        int? page, int? pageSize, string? status, string? sort, CancellationToken t) =>
        AsStaff(c, u, d, t, async p => Results.Ok(PageBody(await s.ListTasksAsync(Page(page, pageSize, null, status, sort), p.UserId, t))));

    /// <summary>Staff see only tasks assigned to them, in a module they still hold; admins see every task.</summary>
    private static async Task<(StaffTask? Task, IResult? Failure)> AuthorizedTaskAsync(string taskId, bool asAdmin, PrincipalContext p,
        IAdministrationStore s, CancellationToken t)
    {
        if (!P.TryId(taskId, out var id)) return (null, P.Invalid("taskId"));
        var task = await s.GetTaskAsync(id, t);
        if (task is null || (!asAdmin && task.AssignedTo != p.UserId)) return (null, Problem(404, "NOT_FOUND"));
        if (!asAdmin && !p.StaffModules.Contains(task.Module)) return (null, Denied());
        return (task, null);
    }

    private static async Task<IResult> TaskDetailAsync(string taskId, bool asAdmin, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
        IAdministrationStore s, CancellationToken t)
    {
        Func<PrincipalContext, Task<IResult>> action = async p =>
        {
            var (task, failure) = await AuthorizedTaskAsync(taskId, asAdmin, p, s, t);
            return failure ?? Results.Ok(new { task, notes = await s.ListTaskNotesAsync(task!.TaskId, t) });
        };
        return asAdmin ? await AsAdmin(c, u, d, t, action) : await AsStaff(c, u, d, t, action);
    }

    private static async Task<IResult> MoveTaskAsync(string taskId, bool asAdmin, HttpContext c, ICurrentUser u, IPrincipalDirectory d,
        IAdministrationStore s, CancellationToken t)
    {
        Func<PrincipalContext, Task<IResult>> action = async p =>
        {
            var (task, failure) = await AuthorizedTaskAsync(taskId, asAdmin, p, s, t);
            if (failure is not null) return failure;
            var body = await P.ReadAsync<MoveTaskRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            var r = body.Value!;
            if (r.Status is not { } status || r.ExpectedVersion is not { } version) return P.Invalid("status");
            // Staff cannot cancel tasks; cancellation is an admin decision.
            if (!asAdmin && status == TaskStatuses.Cancelled) return Denied();
            return Map(await s.MoveTaskAsync(p.UserId, task!.TaskId, status, version, r.Note, t), x => x);
        };
        return asAdmin ? await AsAdmin(c, u, d, t, action) : await AsStaff(c, u, d, t, action);
    }

    private static Task<IResult> AddNoteAsync(string taskId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsStaff(c, u, d, t, async p =>
        {
            var (task, failure) = await AuthorizedTaskAsync(taskId, false, p, s, t);
            if (failure is not null) return failure;
            var body = await P.ReadAsync<NoteRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            return Map(await s.AddTaskNoteAsync(p.UserId, task!.TaskId, body.Value!.Body ?? "", t), x => x, created: true);
        });

    private static Task<IResult> ProjectReviewAsync(string taskId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IAdministrationStore s,
        CancellationToken t) =>
        AsStaff(c, u, d, t, async p =>
        {
            var (task, failure) = await AuthorizedTaskAsync(taskId, false, p, s, t);
            if (failure is not null) return failure;
            var body = await P.ReadAsync<PurposeRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            return Map(await s.ReviewTaskProjectAsync(p.UserId, task!.TaskId, body.Value!.Purpose ?? "", t), x => x);
        });
}

public sealed record ReasonRequest(string? Reason);
public sealed record RoleRequest(string? Role, bool? Granted);
public sealed record InviteStaffRequest(string? Email, string? FullName, string[]? Modules);
public sealed record ModulesRequest(string[]? Modules, long? ExpectedScopeVersion);
public sealed record ReviewRequest(long? ExpectedRowVersion, string? Note);
public sealed record CreateTaskRequest(Guid? AssigneeId, string? Title, string? Description, string? Module, Guid? ProjectId);
public sealed record PolicyRequest(JsonElement? Value, long? ExpectedVersion);
public sealed record ProposalRequest(string? Module, string? TargetType, string? TargetKey, string? ChangeType, long? BaseVersion, JsonElement? Payload);
public sealed record MoveTaskRequest(string? Status, long? ExpectedVersion, string? Note);
public sealed record NoteRequest(string? Body);
public sealed record PurposeRequest(string? Purpose);
public sealed record CatalogueImportRequest(string? Version, string? Provenance, JsonElement? Document);
public sealed record CatalogueTransitionRequest(long? ExpectedRowVersion, DateTimeOffset? EffectiveFromUtc);
