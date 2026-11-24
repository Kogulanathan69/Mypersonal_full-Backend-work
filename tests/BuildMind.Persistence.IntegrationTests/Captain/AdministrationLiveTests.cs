using BuildMind.Application.Features.Administration;
using BuildMind.Infrastructure.CaptainIntegration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using Microsoft.EntityFrameworkCore;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;

namespace BuildMind.Persistence.IntegrationTests.Captain;

/// <summary>Admin/staff RBAC, proposal queue, tasks and audit chain on disposable MySQL 8.4.</summary>
public sealed class AdministrationLiveTests
{
    private static MySqlAdministrationStore Store(BuildMindDbContext context) =>
        new(context, Transactions(context), [new SystemPolicyProposalApplier()], TimeProvider.System);

    private static async Task<long> TokenVersionAsync(Support.DisposableMySqlDatabase db, Guid user)
    {
        await using var c = Context(db);
        return (await c.Set<UserRecord>().AsNoTracking().SingleAsync(u => u.Id == user)).TokenVersion;
    }

    private static async Task<Guid> AdminAsync(Support.DisposableMySqlDatabase db)
    {
        var (user, _) = await SeedAsync(db);
        await using var c = Context(db);
        var email = (await c.Set<UserRecord>().SingleAsync(u => u.Id == user)).NormalizedEmail;
        await using var c2 = Context(db);
        var result = await Store(c2).BootstrapAdminAsync(email, default);
        Assert.Equal(AdminStatus.Success, result.Status);
        return user;
    }

    private static async Task<Guid> StaffAsync(Support.DisposableMySqlDatabase db, Guid admin, params string[] modules)
    {
        await using var c = Context(db);
        var r = await Store(c).CreateStaffAsync(admin, $"staff-{Guid.NewGuid():N}@example.invalid", "Synthetic Staff", modules, "unusable-hash", default);
        Assert.Equal(AdminStatus.Success, r.Status);
        return r.Value;
    }

    [Fact]
    public async Task A01_bootstrap_grants_admin_once_and_only_to_an_existing_active_account()
    {
        await using var db = await SchemaAsync();
        await using (var c = Context(db))
            Assert.Equal("BOOTSTRAP_ACCOUNT_NOT_FOUND", (await Store(c).BootstrapAdminAsync("NOBODY@EXAMPLE.INVALID", default)).Code);
        var admin = await AdminAsync(db);
        var (other, _) = await SeedAsync(db);
        await using var c2 = Context(db);
        var email = (await c2.Set<UserRecord>().SingleAsync(u => u.Id == other)).NormalizedEmail;
        Assert.Equal("ADMIN_ALREADY_EXISTS", (await Store(c2).BootstrapAdminAsync(email, default)).Code);
        await using var c3 = Context(db);
        Assert.True((await new MySqlPrincipalDirectory(c3).GetAsync(admin, default))!.IsAdmin);
        Assert.False((await new MySqlPrincipalDirectory(c3).GetAsync(other, default))!.IsAdmin);
    }

    [Fact]
    public async Task A02_role_changes_revoke_sessions_and_protect_the_last_admin()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        var (user, _) = await SeedAsync(db);
        var before = await TokenVersionAsync(db, user);
        await using (var c = Context(db))
        {
            Assert.Equal("CANNOT_CHANGE_OWN_ROLES", (await Store(c).SetRoleAsync(admin, admin, PrincipalRoles.Admin, false, default)).Code);
            Assert.Equal("ROLE_NOT_ASSIGNABLE", (await Store(c).SetRoleAsync(admin, user, PrincipalRoles.Staff, true, default)).Code);
            Assert.True((await Store(c).SetRoleAsync(admin, user, PrincipalRoles.Admin, true, default)).Value);
            Assert.False((await Store(c).SetRoleAsync(admin, user, PrincipalRoles.Admin, true, default)).Value); // idempotent
        }
        Assert.Equal(before + 1, await TokenVersionAsync(db, user));
        await using (var c = Context(db))
        {
            Assert.Equal(AdminStatus.Success, (await Store(c).SetRoleAsync(user, admin, PrincipalRoles.Admin, false, default)).Status);
            // user is now the only admin; the former admin cannot remove them, and they cannot remove themselves
            Assert.Equal("CANNOT_CHANGE_OWN_ROLES", (await Store(c).SetRoleAsync(user, user, PrincipalRoles.Admin, false, default)).Code);
            Assert.Equal("LAST_ADMIN", (await Store(c).SetAccountActiveAsync(admin, user, false, "test suspension", default)).Code);
        }
    }

    [Fact]
    public async Task A03_concurrent_mutual_admin_revocation_never_leaves_zero_admins()
    {
        await using var db = await SchemaAsync();
        var a = await AdminAsync(db);
        var (b, _) = await SeedAsync(db);
        await using (var c = Context(db))
            await Store(c).SetRoleAsync(a, b, PrincipalRoles.Admin, true, default);
        var results = await Task.WhenAll(
            Task.Run(async () => { await using var c = Context(db); return await Store(c).SetRoleAsync(a, b, PrincipalRoles.Admin, false, default); }),
            Task.Run(async () => { await using var c = Context(db); return await Store(c).SetRoleAsync(b, a, PrincipalRoles.Admin, false, default); }));
        Assert.Single(results, r => r.Status == AdminStatus.Success);
        await using var check = Context(db);
        Assert.Equal(1, await check.Set<PrincipalRoleRecord>().CountAsync(r => r.Role == PrincipalRoles.Admin && r.IsActive));
    }

    [Fact]
    public async Task A04_suspension_revokes_sessions_and_removes_privileges_immediately()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        var staff = await StaffAsync(db, admin, AdminModules.Surveys);
        var before = await TokenVersionAsync(db, staff);
        await using (var c = Context(db))
        {
            Assert.Equal("REASON_REQUIRED", (await Store(c).SetAccountActiveAsync(admin, staff, false, " ", default)).Code);
            Assert.Equal("CANNOT_CHANGE_OWN_ACCOUNT", (await Store(c).SetAccountActiveAsync(admin, admin, false, "self", default)).Code);
            Assert.False((await Store(c).SetAccountActiveAsync(admin, staff, false, "synthetic suspension", default)).Value!.IsActive);
        }
        Assert.True(await TokenVersionAsync(db, staff) > before);
        await using var c2 = Context(db);
        var principal = (await new MySqlPrincipalDirectory(c2).GetAsync(staff, default))!;
        Assert.False(principal.IsStaff);
        Assert.False(principal.CanView(AdminModules.Surveys));
    }

    [Fact]
    public async Task A05_staff_scope_changes_are_versioned_and_revoke_sessions()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        var staff = await StaffAsync(db, admin, AdminModules.Surveys);
        await using var c = Context(db);
        Assert.Equal("MODULES_INVALID", (await Store(c).SetStaffModulesAsync(admin, staff, ["ROOT"], 1, default)).Code);
        Assert.Equal("STALE_SCOPE_VERSION", (await Store(c).SetStaffModulesAsync(admin, staff, [AdminModules.Reports], 7, default)).Code);
        var before = await TokenVersionAsync(db, staff);
        var updated = await Store(c).SetStaffModulesAsync(admin, staff, [AdminModules.Reports, AdminModules.SystemPolicies], 1, default);
        Assert.Equal(2, updated.Value!.ScopeVersion);
        Assert.Equal(before + 1, await TokenVersionAsync(db, staff));
        await using var c2 = Context(db);
        var principal = (await new MySqlPrincipalDirectory(c2).GetAsync(staff, default))!;
        Assert.False(principal.CanView(AdminModules.Surveys));
        Assert.True(principal.CanView(AdminModules.SystemPolicies));
        Assert.Equal("EMAIL_ALREADY_REGISTERED", (await Store(c2).CreateStaffAsync(admin,
            (await c2.Set<UserRecord>().SingleAsync(u => u.Id == staff)).Email, null, [AdminModules.Reports], "x", default)).Code);
    }

    [Fact]
    public async Task A06_staff_proposals_never_change_live_state_until_admin_approval_and_stale_ones_are_refused()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        var staff = await StaffAsync(db, admin, AdminModules.SystemPolicies);
        await using var c = Context(db);
        var s = Store(c);
        Assert.Equal("TARGET_TYPE_UNSUPPORTED", (await s.SubmitProposalAsync(staff, AdminModules.Surveys,
            new SubmitProposalCommand("SYSTEM_POLICY", "a.b", "CREATE", null, "1"), default)).Code);
        Assert.Equal("POLICY_KEY_INVALID", (await s.SubmitProposalAsync(staff, AdminModules.SystemPolicies,
            new SubmitProposalCommand("SYSTEM_POLICY", "Bad Key", "CREATE", null, "1"), default)).Code);
        var create = (await s.SubmitProposalAsync(staff, AdminModules.SystemPolicies,
            new SubmitProposalCommand("SYSTEM_POLICY", "report.retention_days", "CREATE", null, "{\"days\":30}"), default)).Value!;
        Assert.Empty(await s.ListPoliciesAsync(default)); // pending: live unchanged

        var rejected = await s.ReviewProposalAsync(admin, create.ProposalId, false, 1, "not yet", default);
        Assert.Equal(ProposalStatuses.Rejected, rejected.Value!.Status);
        Assert.Empty(await s.ListPoliciesAsync(default));
        Assert.Equal("PROPOSAL_NOT_PENDING", (await s.ReviewProposalAsync(admin, create.ProposalId, true, 2, null, default)).Code);

        var again = (await s.SubmitProposalAsync(staff, AdminModules.SystemPolicies,
            new SubmitProposalCommand("SYSTEM_POLICY", "report.retention_days", "CREATE", null, "{\"days\":30}"), default)).Value!;
        Assert.Equal("STALE_PROPOSAL_VERSION", (await s.ReviewProposalAsync(admin, again.ProposalId, true, 9, null, default)).Code);
        Assert.Equal(ProposalStatuses.Applied, (await s.ReviewProposalAsync(admin, again.ProposalId, true, 1, null, default)).Value!.Status);
        var policy = Assert.Single(await s.ListPoliciesAsync(default));
        Assert.Equal(1, policy.Version);

        // Two updates from the same base: the first applies, the second is STALE and changes nothing.
        var u1 = (await s.SubmitProposalAsync(staff, AdminModules.SystemPolicies,
            new SubmitProposalCommand("SYSTEM_POLICY", "report.retention_days", "UPDATE", 1, "{\"days\":60}"), default)).Value!;
        var u2 = (await s.SubmitProposalAsync(staff, AdminModules.SystemPolicies,
            new SubmitProposalCommand("SYSTEM_POLICY", "report.retention_days", "UPDATE", 1, "{\"days\":90}"), default)).Value!;
        Assert.Equal(ProposalStatuses.Applied, (await s.ReviewProposalAsync(admin, u1.ProposalId, true, 1, null, default)).Value!.Status);
        Assert.Equal(ProposalStatuses.Stale, (await s.ReviewProposalAsync(admin, u2.ProposalId, true, 1, null, default)).Value!.Status);
        Assert.Contains("60", Assert.Single(await s.ListPoliciesAsync(default)).ValueJson);

        // Staff cannot review (the store refuses self-review; the API refuses non-admins).
        var own = (await s.SubmitProposalAsync(admin, AdminModules.SystemPolicies,
            new SubmitProposalCommand("SYSTEM_POLICY", "x.own", "CREATE", null, "1"), default)).Value!;
        Assert.Equal("CANNOT_REVIEW_OWN_PROPOSAL", (await s.ReviewProposalAsync(admin, own.ProposalId, true, 1, null, default)).Code);
        Assert.Equal(ProposalStatuses.Withdrawn, (await s.WithdrawProposalAsync(admin, own.ProposalId, 1, default)).Value!.Status);
    }

    [Fact]
    public async Task A07_tasks_enforce_assignment_transitions_versions_and_purpose_bound_audited_review()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        var (_, project) = await SeedAsync(db);
        var staff = await StaffAsync(db, admin, AdminModules.Surveys);
        var other = await StaffAsync(db, admin, AdminModules.Surveys);
        await using var c = Context(db);
        var s = Store(c);
        Assert.Equal("ASSIGNEE_NOT_IN_MODULE", (await s.CreateTaskAsync(admin, staff, "Review", null, AdminModules.Reports, project, default)).Code);
        var task = (await s.CreateTaskAsync(admin, staff, "Review survey", "check OCR", AdminModules.Surveys, project, default)).Value!;

        Assert.Equal("NOT_FOUND", (await s.ReviewTaskProjectAsync(other, task.TaskId, "unrelated staff attempt", default)).Code);
        Assert.Equal("PURPOSE_REQUIRED", (await s.ReviewTaskProjectAsync(staff, task.TaskId, "short", default)).Code);
        var view = await s.ReviewTaskProjectAsync(staff, task.TaskId, "verify extraction quality for assigned task", default);
        Assert.Equal(project, view.Value!.ProjectId);

        Assert.Equal("TASK_TRANSITION_NOT_ALLOWED", (await s.MoveTaskAsync(staff, task.TaskId, TaskStatuses.Open, 1, null, default)).Code);
        Assert.Equal("ESCALATION_REASON_REQUIRED", (await s.MoveTaskAsync(staff, task.TaskId, TaskStatuses.Escalated, 1, null, default)).Code);
        var moved = await s.MoveTaskAsync(staff, task.TaskId, TaskStatuses.InProgress, 1, null, default);
        Assert.Equal(2, moved.Value!.Version);
        Assert.Equal("STALE_TASK_VERSION", (await s.MoveTaskAsync(staff, task.TaskId, TaskStatuses.Done, 1, null, default)).Code);
        Assert.Equal(TaskStatuses.Done, (await s.MoveTaskAsync(staff, task.TaskId, TaskStatuses.Done, 2, "finished", default)).Value!.Status);
        Assert.Equal("TASK_NOT_ACTIVE", (await s.ReviewTaskProjectAsync(staff, task.TaskId, "after completion access attempt", default)).Code);

        var audit = await s.ListAuditAsync(PageRequest.Normalize(1, 100, "PROJECT_REVIEW_ACCESS", null, null), default);
        var access = Assert.Single(audit.Items);
        Assert.Equal(staff, access.ActorId);
        Assert.Equal("verify extraction quality for assigned task", access.Purpose);
    }

    [Fact]
    public async Task A08_audit_chain_verifies_and_detects_tampering()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        await StaffAsync(db, admin, AdminModules.Reports);
        await using var c = Context(db);
        var ok = await Store(c).VerifyAuditChainAsync(default);
        Assert.True(ok.Intact);
        Assert.Equal(2, ok.EventsChecked);
        await c.Database.ExecuteSqlRawAsync("UPDATE admin_audit_events SET details_json = '{{\"modules\":[\"ALL\"]}}' WHERE sequence = 2");
        var broken = await Store(c).VerifyAuditChainAsync(default);
        Assert.False(broken.Intact);
        Assert.Equal(2, broken.FirstBrokenSequence);
    }

    [Fact]
    public async Task A09_customer_listing_excludes_privileged_accounts_and_supports_search_and_paging()
    {
        await using var db = await SchemaAsync();
        var admin = await AdminAsync(db);
        await StaffAsync(db, admin, AdminModules.Reports);
        var (customer, _) = await SeedAsync(db);
        await using var c = Context(db);
        var page = await Store(c).ListAccountsAsync(PageRequest.Normalize(1, 500, null, null, null), true, default);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(customer, Assert.Single(page.Items).UserId);
        var all = await Store(c).ListAccountsAsync(PageRequest.Normalize(1, 10, null, null, "email"), false, default);
        Assert.Equal(3, all.Total);
        var search = await Store(c).ListAccountsAsync(PageRequest.Normalize(1, 10, customer.ToString("N")[..12], null, null), false, default);
        Assert.Equal(customer, Assert.Single(search.Items).UserId);
    }
}
