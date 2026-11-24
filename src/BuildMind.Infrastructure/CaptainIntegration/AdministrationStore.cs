using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildMind.Application.Features.Administration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Configurations.Administration;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>Fresh privilege read per request (no caching, no JWT claims).</summary>
internal sealed class MySqlPrincipalDirectory(BuildMindDbContext context) : IPrincipalDirectory
{
    public async Task<PrincipalContext?> GetAsync(Guid userId, CancellationToken token)
    {
        var user = await context.Set<UserRecord>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.IsActive }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (user is null) return null;
        var roles = await context.Set<PrincipalRoleRecord>().AsNoTracking().Where(r => r.UserId == userId && r.IsActive)
            .Select(r => r.Role).ToListAsync(token).ConfigureAwait(false);
        var modules = roles.Contains(PrincipalRoles.Staff)
            ? (await context.Set<StaffProfileRecord>().AsNoTracking().Where(s => s.UserId == userId).Select(s => s.ModulesCsv)
                .SingleOrDefaultAsync(token).ConfigureAwait(false))?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []
            : [];
        return new PrincipalContext(userId, user.IsActive, roles.ToHashSet(StringComparer.Ordinal),
            modules.Where(AdminModules.All.Contains).ToHashSet(StringComparer.Ordinal));
    }
}

/// <summary>Append-only SHA-256 hash chain. The head row lock is always the LAST lock taken in a transaction.</summary>
internal static class AdminAudit
{
    public static async Task AppendAsync(BuildMindDbContext context, Guid? actor, string action, string targetType, string? targetId,
        string? purpose, object? details, DateTimeOffset now, CancellationToken token)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT last_sequence FROM admin_audit_chain_head WHERE id = 1 FOR UPDATE", token).ConfigureAwait(false);
        var head = await context.Set<AdminAuditChainHeadRecord>().SingleAsync(h => h.Id == 1, token).ConfigureAwait(false);
        var record = new AdminAuditEventRecord
        {
            Sequence = head.LastSequence + 1, ActorId = actor, Action = action, TargetType = targetType, TargetId = targetId,
            Purpose = purpose, DetailsJson = JsonSerializer.Serialize(details ?? new { }), OccurredAtUtc = now,
            PreviousHash = head.LastHash, Hash = string.Empty,
        };
        var sealedRecord = new AdminAuditEventRecord
        {
            Sequence = record.Sequence, ActorId = actor, Action = action, TargetType = targetType, TargetId = targetId, Purpose = purpose,
            DetailsJson = record.DetailsJson, OccurredAtUtc = now, PreviousHash = head.LastHash, Hash = Compute(record),
        };
        context.Add(sealedRecord);
        head.LastSequence = sealedRecord.Sequence;
        head.LastHash = sealedRecord.Hash;
        await context.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public static string Compute(AdminAuditEventRecord e)
    {
        var canonical = string.Join('\n', e.Sequence, e.PreviousHash, e.ActorId?.ToString("D") ?? "", e.Action, e.TargetType,
            e.TargetId ?? "", e.Purpose ?? "", e.DetailsJson, e.OccurredAtUtc.UtcTicks / 10);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Applies an approved staff proposal inside the approval transaction.</summary>
internal interface IProposalApplier
{
    string TargetType { get; }
    string Module { get; }

    /// <summary>Early validation at submission; null when valid, otherwise a stable code.</summary>
    string? Validate(string changeType, string targetKey, string payloadJson);

    Task<ProposalApplyOutcome> ApplyAsync(BuildMindDbContext context, StaffChangeProposalRecord proposal, Guid admin, DateTimeOffset now,
        CancellationToken token);
}

internal enum ProposalApplyOutcome { Applied, Stale, Invalid }

internal sealed partial class SystemPolicyProposalApplier : IProposalApplier
{
    public string TargetType => "SYSTEM_POLICY";
    public string Module => AdminModules.SystemPolicies;

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{2,63}$")]
    internal static partial Regex KeyPattern();

    public string? Validate(string changeType, string targetKey, string payloadJson)
    {
        if (!KeyPattern().IsMatch(targetKey)) return "POLICY_KEY_INVALID";
        if (changeType != "DELETE" && Encoding.UTF8.GetByteCount(payloadJson) > SystemPolicyRecord.ValueMaxBytes) return "POLICY_VALUE_TOO_LARGE";
        return null;
    }

    public async Task<ProposalApplyOutcome> ApplyAsync(BuildMindDbContext context, StaffChangeProposalRecord p, Guid admin, DateTimeOffset now,
        CancellationToken token)
    {
        var current = await context.Set<SystemPolicyRecord>().SingleOrDefaultAsync(x => x.Key == p.TargetKey, token).ConfigureAwait(false);
        switch (p.ChangeType)
        {
            case "CREATE":
                if (current is not null) return ProposalApplyOutcome.Stale;
                context.Add(new SystemPolicyRecord { Key = p.TargetKey, ValueJson = p.PayloadJson, Version = 1, UpdatedBy = admin, UpdatedAtUtc = now });
                break;
            case "UPDATE":
                if (current is null || current.Version != p.BaseVersion) return ProposalApplyOutcome.Stale;
                current.ValueJson = p.PayloadJson;
                current.Version++;
                current.UpdatedBy = admin;
                current.UpdatedAtUtc = now;
                break;
            case "DELETE":
                if (current is null || current.Version != p.BaseVersion) return ProposalApplyOutcome.Stale;
                context.Remove(current);
                break;
            default:
                return ProposalApplyOutcome.Invalid;
        }
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return ProposalApplyOutcome.Applied;
    }
}

internal sealed partial class MySqlAdministrationStore(
    BuildMindDbContext context, TransactionRunner transactions, IEnumerable<IProposalApplier> appliers, TimeProvider time)
    : IAdministrationStore
{
    private static readonly string[] ActiveTaskStatuses = [TaskStatuses.Open, TaskStatuses.InProgress, TaskStatuses.Escalated];

    [GeneratedRegex(@"^[^@\s]{1,64}@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private sealed class Abort(object result) : Exception
    {
        public object Result { get; } = result;
    }

    private async Task<AdminResult<T>> WriteAsync<T>(Func<CancellationToken, Task<AdminResult<T>>> work, CancellationToken token)
    {
        try
        {
            return await transactions.ExecuteAsync(async inner =>
            {
                var result = await work(inner).ConfigureAwait(false);
                if (result.Status != AdminStatus.Success) throw new Abort(result); // roll back anything staged
                return result;
            }, token).ConfigureAwait(false);
        }
        catch (Abort abort)
        {
            return (AdminResult<T>)abort.Result;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    private static AdminResult<T> Fail<T>(AdminStatus status, string code) => new(status, default, code);

    // ---------------------------------------------------------------- accounts

    public async Task<PagedResult<AccountSummary>> ListAccountsAsync(PageRequest page, bool customersOnly, CancellationToken token)
    {
        var users = context.Set<UserRecord>().AsNoTracking();
        if (customersOnly)
            users = users.Where(u => !context.Set<PrincipalRoleRecord>().Any(r => r.UserId == u.Id && r.IsActive));
        if (page.Search is { } search)
        {
            var upper = search.ToUpperInvariant();
            users = users.Where(u => u.NormalizedEmail.Contains(upper) || (u.FullName != null && u.FullName.Contains(search)));
        }
        users = page.Status switch
        {
            "ACTIVE" => users.Where(u => u.IsActive),
            "SUSPENDED" => users.Where(u => !u.IsActive),
            _ => users,
        };
        users = page.Sort switch
        {
            "email" => users.OrderBy(u => u.NormalizedEmail),
            "-email" => users.OrderByDescending(u => u.NormalizedEmail),
            "created" => users.OrderBy(u => u.CreatedAtUtc),
            _ => users.OrderByDescending(u => u.CreatedAtUtc),
        };
        var total = await users.LongCountAsync(token).ConfigureAwait(false);
        var rows = await users.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive, u.CreatedAtUtc,
                Projects = context.Set<ProjectRecord>().Count(p => p.OwnerId == u.Id) })
            .ToListAsync(token).ConfigureAwait(false);
        var ids = rows.Select(r => r.Id).ToArray();
        var roles = (await context.Set<PrincipalRoleRecord>().AsNoTracking().Where(r => ids.Contains(r.UserId) && r.IsActive)
            .Select(r => new { r.UserId, r.Role }).ToListAsync(token).ConfigureAwait(false)).ToLookup(r => r.UserId, r => r.Role);
        return new PagedResult<AccountSummary>(rows.Select(r => new AccountSummary(r.Id, r.Email, r.FullName, r.IsActive, r.CreatedAtUtc,
            roles[r.Id].Order().ToArray(), r.Projects)).ToArray(), page.Page, page.PageSize, total);
    }

    public async Task<AccountDetail?> GetAccountAsync(Guid userId, CancellationToken token)
    {
        var u = await context.Set<UserRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, token).ConfigureAwait(false);
        if (u is null) return null;
        var roles = await context.Set<PrincipalRoleRecord>().AsNoTracking().Where(r => r.UserId == userId && r.IsActive)
            .Select(r => r.Role).ToListAsync(token).ConfigureAwait(false);
        var modules = (await context.Set<StaffProfileRecord>().AsNoTracking().Where(s => s.UserId == userId).Select(s => s.ModulesCsv)
            .SingleOrDefaultAsync(token).ConfigureAwait(false))?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var projects = context.Set<ProjectRecord>().AsNoTracking().Where(p => p.OwnerId == userId).Select(p => p.Id);
        var projectCount = await projects.CountAsync(token).ConfigureAwait(false);
        var surveys = await context.Set<SurveyRecord>().CountAsync(s => projects.Contains(s.ProjectId), token).ConfigureAwait(false);
        var reports = await context.Set<ReportRecord>().CountAsync(r => projects.Contains(r.ProjectId), token).ConfigureAwait(false);
        var requests = await context.Set<ArchitectRequestRecord>().CountAsync(r => projects.Contains(r.ProjectId), token).ConfigureAwait(false);
        return new AccountDetail(new AccountSummary(u.Id, u.Email, u.FullName, u.IsActive, u.CreatedAtUtc, roles.Order().ToArray(), projectCount),
            modules, surveys, reports, requests, u.TokenVersion);
    }

    public Task<AdminResult<AccountSummary>> SetAccountActiveAsync(Guid actor, Guid target, bool active, string reason, CancellationToken token)
    {
        if (actor == target) return Task.FromResult(Fail<AccountSummary>(AdminStatus.Conflict, "CANNOT_CHANGE_OWN_ACCOUNT"));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            return Task.FromResult(Fail<AccountSummary>(AdminStatus.ValidationFailed, "REASON_REQUIRED"));
        return WriteAsync(async inner =>
        {
            if ((await SessionSql.LockUserAsync(context, target, inner).ConfigureAwait(false)).Count == 0)
                return Fail<AccountSummary>(AdminStatus.NotFound, "NOT_FOUND");
            if (!active && !await OtherActiveAdminExistsAsync(target, inner).ConfigureAwait(false)
                && await IsActiveAdminAsync(target, inner).ConfigureAwait(false))
                return Fail<AccountSummary>(AdminStatus.Conflict, "LAST_ADMIN");
            var now = Now();
            // Any status change ends every session (token version), and suspension also revokes refresh tokens.
            await context.Set<UserRecord>().Where(u => u.Id == target).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, active).SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), inner).ConfigureAwait(false);
            await SessionSql.RevokeUserRefreshTokensAsync(context, target, now, active ? "account_reactivated" : "account_suspended", inner)
                .ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, actor, active ? "ACCOUNT_ACTIVATED" : "ACCOUNT_SUSPENDED", "USER", target.ToString("D"),
                reason.Trim(), null, now, inner).ConfigureAwait(false);
            var detail = await GetAccountAsync(target, inner).ConfigureAwait(false);
            return AdminResult<AccountSummary>.Ok(detail!.Account);
        }, token);
    }

    public Task<AdminResult<bool>> SetRoleAsync(Guid actor, Guid target, string role, bool granted, CancellationToken token)
    {
        if (role is not (PrincipalRoles.Admin or PrincipalRoles.Architect))
            return Task.FromResult(Fail<bool>(AdminStatus.ValidationFailed, "ROLE_NOT_ASSIGNABLE"));
        if (actor == target) return Task.FromResult(Fail<bool>(AdminStatus.Conflict, "CANNOT_CHANGE_OWN_ROLES"));
        return WriteAsync(async inner =>
        {
            if ((await SessionSql.LockUserAsync(context, target, inner).ConfigureAwait(false)).Count == 0)
                return Fail<bool>(AdminStatus.NotFound, "NOT_FOUND");
            var user = await context.Set<UserRecord>().AsNoTracking().SingleAsync(u => u.Id == target, inner).ConfigureAwait(false);
            if (granted && !user.IsActive) return Fail<bool>(AdminStatus.Conflict, "ACCOUNT_SUSPENDED");
            if (!granted && role == PrincipalRoles.Admin && !await OtherActiveAdminExistsAsync(target, inner).ConfigureAwait(false))
                return Fail<bool>(AdminStatus.Conflict, "LAST_ADMIN");
            var now = Now();
            var row = await context.Set<PrincipalRoleRecord>().SingleOrDefaultAsync(r => r.UserId == target && r.Role == role, inner)
                .ConfigureAwait(false);
            if ((row?.IsActive ?? false) == granted) return AdminResult<bool>.Ok(false); // idempotent: no change
            if (row is null)
                context.Add(new PrincipalRoleRecord { Id = Guid.NewGuid(), UserId = target, Role = role, IsActive = true, GrantedBy = actor, GrantedAtUtc = now });
            else if (granted)
            {
                row.IsActive = true; row.GrantedBy = actor; row.GrantedAtUtc = now; row.RevokedBy = null; row.RevokedAtUtc = null;
            }
            else
            {
                row.IsActive = false; row.RevokedBy = actor; row.RevokedAtUtc = now;
            }
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await RevokeSessionsAsync(target, now, "privileges_changed", inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, actor, granted ? "ROLE_GRANTED" : "ROLE_REVOKED", "USER", target.ToString("D"), null,
                new { role }, now, inner).ConfigureAwait(false);
            return AdminResult<bool>.Ok(true);
        }, token);
    }

    private async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, string reason, CancellationToken token)
    {
        await context.Set<UserRecord>().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), token).ConfigureAwait(false);
        await SessionSql.RevokeUserRefreshTokensAsync(context, userId, now, reason, token).ConfigureAwait(false);
    }

    private Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken token) =>
        context.Set<PrincipalRoleRecord>().AnyAsync(r => r.UserId == userId && r.Role == PrincipalRoles.Admin && r.IsActive, token);

    /// <summary>Locks the active ADMIN role rows (after the target users row) so two admins cannot remove each other.</summary>
    private async Task<bool> OtherActiveAdminExistsAsync(Guid excluded, CancellationToken token)
    {
        var admins = await context.Database.SqlQuery<Guid>(
                $"SELECT user_id AS Value FROM principal_roles WHERE role = {PrincipalRoles.Admin} AND is_active = TRUE FOR UPDATE")
            .ToListAsync(token).ConfigureAwait(false);
        var others = admins.Where(a => a != excluded).ToArray();
        return others.Length > 0 && await context.Set<UserRecord>().AnyAsync(u => others.Contains(u.Id) && u.IsActive, token).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- staff

    public Task<AdminResult<Guid>> CreateStaffAsync(Guid actor, string email, string? fullName, IReadOnlyCollection<string> modules,
        string unusablePasswordHash, CancellationToken token)
    {
        email = (email ?? "").Trim();
        if (email.Length > UserRecord.EmailMaxLength || !EmailPattern().IsMatch(email))
            return Task.FromResult(Fail<Guid>(AdminStatus.ValidationFailed, "EMAIL_INVALID"));
        if (fullName is { Length: > UserRecord.FullNameMaxLength })
            return Task.FromResult(Fail<Guid>(AdminStatus.ValidationFailed, "FULL_NAME_TOO_LONG"));
        if (ModulesInvalid(modules)) return Task.FromResult(Fail<Guid>(AdminStatus.ValidationFailed, "MODULES_INVALID"));
        return WriteAsync(async inner =>
        {
            var normalized = email.ToUpperInvariant();
            if (await context.Set<UserRecord>().AnyAsync(u => u.NormalizedEmail == normalized, inner).ConfigureAwait(false))
                return Fail<Guid>(AdminStatus.Conflict, "EMAIL_ALREADY_REGISTERED");
            var now = Now();
            var id = Guid.NewGuid();
            context.Add(new UserRecord
            {
                Id = id, Email = email, NormalizedEmail = normalized, PasswordHash = unusablePasswordHash, FullName = fullName?.Trim(),
                IsActive = true, TokenVersion = 1, CreatedAtUtc = now,
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            context.Add(new PrincipalRoleRecord { Id = Guid.NewGuid(), UserId = id, Role = PrincipalRoles.Staff, IsActive = true, GrantedBy = actor, GrantedAtUtc = now });
            context.Add(new StaffProfileRecord { UserId = id, ModulesCsv = Csv(modules), ScopeVersion = 1, CreatedBy = actor, CreatedAtUtc = now });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, actor, "STAFF_CREATED", "USER", id.ToString("D"), null,
                new { modules = modules.Order().ToArray() }, now, inner).ConfigureAwait(false);
            return AdminResult<Guid>.Ok(id);
        }, token);
    }

    private static bool ModulesInvalid(IReadOnlyCollection<string>? modules) =>
        modules is null || modules.Count == 0 || modules.Any(m => !AdminModules.All.Contains(m)) || modules.Distinct().Count() != modules.Count;

    private static string Csv(IEnumerable<string> modules) => string.Join(',', modules.Order(StringComparer.Ordinal));

    public async Task<PagedResult<StaffSummary>> ListStaffAsync(PageRequest page, CancellationToken token)
    {
        var query = from s in context.Set<StaffProfileRecord>().AsNoTracking()
                    join u in context.Set<UserRecord>().AsNoTracking() on s.UserId equals u.Id
                    where context.Set<PrincipalRoleRecord>().Any(r => r.UserId == u.Id && r.Role == PrincipalRoles.Staff && r.IsActive)
                    select new { u.Id, u.Email, u.NormalizedEmail, u.FullName, u.IsActive, s.ModulesCsv, s.ScopeVersion, s.CreatedAtUtc };
        if (page.Search is { } search) query = query.Where(x => x.NormalizedEmail.Contains(search.ToUpperInvariant()));
        if (page.Status == "ACTIVE") query = query.Where(x => x.IsActive);
        if (page.Status == "SUSPENDED") query = query.Where(x => !x.IsActive);
        query = page.Sort == "email" ? query.OrderBy(x => x.NormalizedEmail) : query.OrderByDescending(x => x.CreatedAtUtc);
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(token).ConfigureAwait(false);
        return new PagedResult<StaffSummary>(rows.Select(r => new StaffSummary(r.Id, r.Email, r.FullName, r.IsActive,
            r.ModulesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries), r.ScopeVersion, r.CreatedAtUtc)).ToArray(), page.Page, page.PageSize, total);
    }

    public Task<AdminResult<StaffSummary>> SetStaffModulesAsync(Guid actor, Guid staff, IReadOnlyCollection<string> modules, long expectedScopeVersion,
        CancellationToken token)
    {
        if (ModulesInvalid(modules)) return Task.FromResult(Fail<StaffSummary>(AdminStatus.ValidationFailed, "MODULES_INVALID"));
        return WriteAsync(async inner =>
        {
            if ((await SessionSql.LockUserAsync(context, staff, inner).ConfigureAwait(false)).Count == 0)
                return Fail<StaffSummary>(AdminStatus.NotFound, "NOT_FOUND");
            var profile = await context.Set<StaffProfileRecord>().SingleOrDefaultAsync(s => s.UserId == staff, inner).ConfigureAwait(false);
            if (profile is null) return Fail<StaffSummary>(AdminStatus.NotFound, "NOT_FOUND");
            if (profile.ScopeVersion != expectedScopeVersion) return Fail<StaffSummary>(AdminStatus.Conflict, "STALE_SCOPE_VERSION");
            var now = Now();
            var before = profile.ModulesCsv;
            profile.ModulesCsv = Csv(modules);
            profile.ScopeVersion++;
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await RevokeSessionsAsync(staff, now, "privileges_changed", inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, actor, "STAFF_SCOPE_CHANGED", "USER", staff.ToString("D"), null,
                new { before = before.Split(','), after = modules.Order().ToArray() }, now, inner).ConfigureAwait(false);
            var user = await context.Set<UserRecord>().AsNoTracking().SingleAsync(u => u.Id == staff, inner).ConfigureAwait(false);
            return AdminResult<StaffSummary>.Ok(new StaffSummary(staff, user.Email, user.FullName, user.IsActive,
                modules.Order().ToArray(), profile.ScopeVersion, profile.CreatedAtUtc));
        }, token);
    }

    // ---------------------------------------------------------------- proposals

    private IProposalApplier? Applier(string? targetType) => appliers.FirstOrDefault(a => a.TargetType == targetType);

    private static ChangeProposal View(StaffChangeProposalRecord p) => new(p.Id, p.Module, p.TargetType, p.TargetKey, p.ChangeType,
        p.BaseVersion, p.PayloadJson, p.Status, p.ProposedBy, p.ProposedAtUtc, p.ReviewedBy, p.ReviewedAtUtc, p.ReviewNote, p.RowVersion);

    public Task<AdminResult<ChangeProposal>> SubmitProposalAsync(Guid staff, string module, SubmitProposalCommand command, CancellationToken token)
    {
        var applier = Applier(command.TargetType);
        if (applier is null || applier.Module != module) return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, "TARGET_TYPE_UNSUPPORTED"));
        if (command.ChangeType is not ("CREATE" or "UPDATE" or "DELETE"))
            return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, "CHANGE_TYPE_INVALID"));
        if (string.IsNullOrWhiteSpace(command.TargetKey) || command.TargetKey.Length > 128)
            return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, "TARGET_KEY_INVALID"));
        if (command.ChangeType != "CREATE" && command.BaseVersion is null)
            return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, "BASE_VERSION_REQUIRED"));
        var payload = command.ChangeType == "DELETE" ? "null" : command.PayloadJson ?? "";
        if (Encoding.UTF8.GetByteCount(payload) > StaffChangeProposalRecord.PayloadMaxBytes || !IsJson(payload))
            return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, "PAYLOAD_INVALID"));
        if (applier.Validate(command.ChangeType, command.TargetKey, payload) is { } code)
            return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, code));
        return WriteAsync(async inner =>
        {
            var now = Now();
            var record = new StaffChangeProposalRecord
            {
                Id = Guid.NewGuid(), Module = module, TargetType = applier.TargetType, TargetKey = command.TargetKey, ChangeType = command.ChangeType,
                BaseVersion = command.BaseVersion, PayloadJson = payload, Status = ProposalStatuses.Pending, ProposedBy = staff,
                ProposedAtUtc = now, RowVersion = 1,
            };
            context.Add(record);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, staff, "PROPOSAL_SUBMITTED", "PROPOSAL", record.Id.ToString("D"), null,
                new { module, record.TargetType, record.TargetKey, record.ChangeType }, now, inner).ConfigureAwait(false);
            return AdminResult<ChangeProposal>.Ok(View(record));
        }, token);
    }

    internal static bool IsJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 32 });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<PagedResult<ChangeProposal>> ListProposalsAsync(PageRequest page, Guid? proposedBy, IReadOnlySet<string>? modules, CancellationToken token)
    {
        var query = context.Set<StaffChangeProposalRecord>().AsNoTracking();
        if (proposedBy is { } by) query = query.Where(p => p.ProposedBy == by);
        if (modules is not null)
        {
            var allowed = modules.ToArray();
            query = query.Where(p => allowed.Contains(p.Module));
        }
        if (page.Status is { } status) query = query.Where(p => p.Status == status);
        query = page.Sort == "proposed" ? query.OrderBy(p => p.ProposedAtUtc) : query.OrderByDescending(p => p.ProposedAtUtc);
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(token).ConfigureAwait(false);
        return new PagedResult<ChangeProposal>(rows.Select(View).ToArray(), page.Page, page.PageSize, total);
    }

    public Task<AdminResult<ChangeProposal>> ReviewProposalAsync(Guid admin, Guid proposalId, bool approve, long expectedRowVersion, string? note,
        CancellationToken token)
    {
        if (note is { Length: > 1000 }) return Task.FromResult(Fail<ChangeProposal>(AdminStatus.ValidationFailed, "NOTE_TOO_LONG"));
        return WriteAsync(async inner =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM staff_change_proposals WHERE id = {proposalId} FOR UPDATE", inner).ConfigureAwait(false);
            var p = await context.Set<StaffChangeProposalRecord>().SingleOrDefaultAsync(x => x.Id == proposalId, inner).ConfigureAwait(false);
            if (p is null) return Fail<ChangeProposal>(AdminStatus.NotFound, "NOT_FOUND");
            if (p.Status != ProposalStatuses.Pending) return Fail<ChangeProposal>(AdminStatus.Conflict, "PROPOSAL_NOT_PENDING");
            if (p.RowVersion != expectedRowVersion) return Fail<ChangeProposal>(AdminStatus.Conflict, "STALE_PROPOSAL_VERSION");
            if (p.ProposedBy == admin) return Fail<ChangeProposal>(AdminStatus.Forbidden, "CANNOT_REVIEW_OWN_PROPOSAL");
            var now = Now();
            string outcome;
            if (!approve)
                outcome = ProposalStatuses.Rejected; // live state untouched
            else
            {
                var applier = Applier(p.TargetType);
                if (applier is null) return Fail<ChangeProposal>(AdminStatus.Conflict, "TARGET_TYPE_UNSUPPORTED");
                var applied = await applier.ApplyAsync(context, p, admin, now, inner).ConfigureAwait(false);
                if (applied == ProposalApplyOutcome.Invalid) return Fail<ChangeProposal>(AdminStatus.ValidationFailed, "PROPOSAL_INVALID");
                outcome = applied == ProposalApplyOutcome.Applied ? ProposalStatuses.Applied : ProposalStatuses.Stale;
            }
            p.Status = outcome;
            p.ReviewedBy = admin;
            p.ReviewedAtUtc = now;
            p.ReviewNote = note?.Trim();
            p.RowVersion++;
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, admin, "PROPOSAL_" + outcome, "PROPOSAL", p.Id.ToString("D"), null,
                new { p.Module, p.TargetType, p.TargetKey, p.ChangeType }, now, inner).ConfigureAwait(false);
            return AdminResult<ChangeProposal>.Ok(View(p));
        }, token);
    }

    public Task<AdminResult<ChangeProposal>> WithdrawProposalAsync(Guid staff, Guid proposalId, long expectedRowVersion, CancellationToken token) =>
        WriteAsync(async inner =>
        {
            var p = await context.Set<StaffChangeProposalRecord>().SingleOrDefaultAsync(x => x.Id == proposalId && x.ProposedBy == staff, inner)
                .ConfigureAwait(false);
            if (p is null) return Fail<ChangeProposal>(AdminStatus.NotFound, "NOT_FOUND");
            if (p.Status != ProposalStatuses.Pending) return Fail<ChangeProposal>(AdminStatus.Conflict, "PROPOSAL_NOT_PENDING");
            if (p.RowVersion != expectedRowVersion) return Fail<ChangeProposal>(AdminStatus.Conflict, "STALE_PROPOSAL_VERSION");
            var now = Now();
            p.Status = ProposalStatuses.Withdrawn;
            p.RowVersion++;
            try { await context.SaveChangesAsync(inner).ConfigureAwait(false); }
            catch (DbUpdateConcurrencyException) { return Fail<ChangeProposal>(AdminStatus.Conflict, "STALE_PROPOSAL_VERSION"); }
            await AdminAudit.AppendAsync(context, staff, "PROPOSAL_WITHDRAWN", "PROPOSAL", p.Id.ToString("D"), null, null, now, inner)
                .ConfigureAwait(false);
            return AdminResult<ChangeProposal>.Ok(View(p));
        }, token);

    // ---------------------------------------------------------------- tasks

    private static StaffTask View(StaffTaskRecord t) => new(t.Id, t.Title, t.Description, t.Module, t.ProjectId, t.AssignedTo, t.CreatedBy,
        t.Status, t.Version, t.CreatedAtUtc, t.UpdatedAtUtc);

    public Task<AdminResult<StaffTask>> CreateTaskAsync(Guid admin, Guid assignee, string title, string? description, string module, Guid? projectId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || description is { Length: > 2000 } || !AdminModules.All.Contains(module))
            return Task.FromResult(Fail<StaffTask>(AdminStatus.ValidationFailed, "TASK_INVALID"));
        return WriteAsync(async inner =>
        {
            var staff = await new MySqlPrincipalDirectory(context).GetAsync(assignee, inner).ConfigureAwait(false);
            if (staff is null || !staff.IsStaff || !staff.StaffModules.Contains(module))
                return Fail<StaffTask>(AdminStatus.ValidationFailed, "ASSIGNEE_NOT_IN_MODULE");
            if (projectId is { } project && !await context.Set<ProjectRecord>().AnyAsync(p => p.Id == project, inner).ConfigureAwait(false))
                return Fail<StaffTask>(AdminStatus.ValidationFailed, "PROJECT_NOT_FOUND");
            var now = Now();
            var task = new StaffTaskRecord
            {
                Id = Guid.NewGuid(), Title = title.Trim(), Description = description?.Trim(), Module = module, ProjectId = projectId,
                AssignedTo = assignee, CreatedBy = admin, Status = TaskStatuses.Open, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            context.Add(task);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, admin, "TASK_CREATED", "TASK", task.Id.ToString("D"), null,
                new { assignee, module, projectId }, now, inner).ConfigureAwait(false);
            return AdminResult<StaffTask>.Ok(View(task));
        }, token);
    }

    public async Task<PagedResult<StaffTask>> ListTasksAsync(PageRequest page, Guid? assignedTo, CancellationToken token)
    {
        var query = context.Set<StaffTaskRecord>().AsNoTracking();
        if (assignedTo is { } a) query = query.Where(t => t.AssignedTo == a);
        if (page.Status is { } status) query = query.Where(t => t.Status == status);
        query = page.Sort == "created" ? query.OrderBy(t => t.CreatedAtUtc) : query.OrderByDescending(t => t.UpdatedAtUtc);
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(token).ConfigureAwait(false);
        return new PagedResult<StaffTask>(rows.Select(View).ToArray(), page.Page, page.PageSize, total);
    }

    public async Task<StaffTask?> GetTaskAsync(Guid taskId, CancellationToken token) =>
        await context.Set<StaffTaskRecord>().AsNoTracking().Where(t => t.Id == taskId).Select(t => View(t)).SingleOrDefaultAsync(token)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<TaskNote>> ListTaskNotesAsync(Guid taskId, CancellationToken token) =>
        await context.Set<StaffTaskNoteRecord>().AsNoTracking().Where(n => n.TaskId == taskId).OrderBy(n => n.CreatedAtUtc).Take(500)
            .Select(n => new TaskNote(n.Id, n.TaskId, n.AuthorId, n.Kind, n.Body, n.CreatedAtUtc)).ToListAsync(token).ConfigureAwait(false);

    public Task<AdminResult<StaffTask>> MoveTaskAsync(Guid actor, Guid taskId, string status, long expectedVersion, string? note, CancellationToken token)
    {
        if (note is { Length: > 2000 }) return Task.FromResult(Fail<StaffTask>(AdminStatus.ValidationFailed, "NOTE_TOO_LONG"));
        if (status == TaskStatuses.Escalated && string.IsNullOrWhiteSpace(note))
            return Task.FromResult(Fail<StaffTask>(AdminStatus.ValidationFailed, "ESCALATION_REASON_REQUIRED"));
        return WriteAsync(async inner =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM staff_tasks WHERE id = {taskId} FOR UPDATE", inner).ConfigureAwait(false);
            var task = await context.Set<StaffTaskRecord>().SingleOrDefaultAsync(t => t.Id == taskId, inner).ConfigureAwait(false);
            if (task is null) return Fail<StaffTask>(AdminStatus.NotFound, "NOT_FOUND");
            if (task.Version != expectedVersion) return Fail<StaffTask>(AdminStatus.Conflict, "STALE_TASK_VERSION");
            if (!TaskStatuses.CanMove(task.Status, status)) return Fail<StaffTask>(AdminStatus.Conflict, "TASK_TRANSITION_NOT_ALLOWED");
            var now = Now();
            var from = task.Status;
            task.Status = status;
            task.Version++;
            task.UpdatedAtUtc = now;
            context.Add(new StaffTaskNoteRecord
            {
                Id = Guid.NewGuid(), TaskId = taskId, AuthorId = actor, Kind = status == TaskStatuses.Escalated ? "ESCALATION" : "STATUS",
                Body = string.IsNullOrWhiteSpace(note) ? $"{from} -> {status}" : note.Trim(), CreatedAtUtc = now,
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, actor, "TASK_STATUS_CHANGED", "TASK", taskId.ToString("D"), null, new { from, to = status }, now, inner)
                .ConfigureAwait(false);
            return AdminResult<StaffTask>.Ok(View(task));
        }, token);
    }

    public Task<AdminResult<TaskNote>> AddTaskNoteAsync(Guid actor, Guid taskId, string body, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) return Task.FromResult(Fail<TaskNote>(AdminStatus.ValidationFailed, "NOTE_INVALID"));
        return WriteAsync(async inner =>
        {
            if (!await context.Set<StaffTaskRecord>().AnyAsync(t => t.Id == taskId, inner).ConfigureAwait(false))
                return Fail<TaskNote>(AdminStatus.NotFound, "NOT_FOUND");
            var now = Now();
            var note = new StaffTaskNoteRecord { Id = Guid.NewGuid(), TaskId = taskId, AuthorId = actor, Kind = "NOTE", Body = body.Trim(), CreatedAtUtc = now };
            context.Add(note);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, actor, "TASK_NOTE_ADDED", "TASK", taskId.ToString("D"), null, new { length = note.Body.Length }, now, inner)
                .ConfigureAwait(false);
            return AdminResult<TaskNote>.Ok(new TaskNote(note.Id, taskId, actor, note.Kind, note.Body, now));
        }, token);
    }

    public Task<AdminResult<ProjectReviewView>> ReviewTaskProjectAsync(Guid staff, Guid taskId, string purpose, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Trim().Length is < 10 or > 500)
            return Task.FromResult(Fail<ProjectReviewView>(AdminStatus.ValidationFailed, "PURPOSE_REQUIRED"));
        return WriteAsync(async inner =>
        {
            var task = await context.Set<StaffTaskRecord>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.AssignedTo == staff, inner)
                .ConfigureAwait(false);
            if (task is null) return Fail<ProjectReviewView>(AdminStatus.NotFound, "NOT_FOUND");
            if (task.ProjectId is not { } projectId) return Fail<ProjectReviewView>(AdminStatus.Conflict, "TASK_HAS_NO_PROJECT");
            if (!ActiveTaskStatuses.Contains(task.Status)) return Fail<ProjectReviewView>(AdminStatus.Forbidden, "TASK_NOT_ACTIVE");
            var project = await context.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId, inner).ConfigureAwait(false);
            var surveys = await context.Set<SurveyRecord>().AsNoTracking().Where(s => s.ProjectId == projectId).OrderByDescending(s => s.UploadedAtUtc)
                .Take(50).Select(s => new { s.Id, s.Status, s.MediaType, s.ByteLength, s.UploadedAtUtc, s.FailureCode }).ToListAsync(inner).ConfigureAwait(false);
            var view = new ProjectReviewView(project.Id, project.Name, project.Status, project.CreatedAtUtc, surveys.Cast<object>().ToArray(),
                await context.Set<LandRevisionRecord>().AnyAsync(l => l.ProjectId == projectId, inner).ConfigureAwait(false),
                await context.Set<RequirementHeadRecord>().AnyAsync(r => r.ProjectId == projectId, inner).ConfigureAwait(false),
                await context.Set<PlanRecord>().CountAsync(p => p.ProjectId == projectId, inner).ConfigureAwait(false),
                await context.Set<ReportRecord>().CountAsync(r => r.ProjectId == projectId, inner).ConfigureAwait(false));
            // Access to customer data is purpose-bound and audited before anything is returned.
            await AdminAudit.AppendAsync(context, staff, "PROJECT_REVIEW_ACCESS", "PROJECT", projectId.ToString("D"), purpose.Trim(),
                new { taskId }, Now(), inner).ConfigureAwait(false);
            return AdminResult<ProjectReviewView>.Ok(view);
        }, token);
    }

    // ---------------------------------------------------------------- policies

    public async Task<IReadOnlyList<SystemPolicy>> ListPoliciesAsync(CancellationToken token) =>
        await context.Set<SystemPolicyRecord>().AsNoTracking().OrderBy(p => p.Key)
            .Select(p => new SystemPolicy(p.Key, p.ValueJson, p.Version, p.UpdatedBy, p.UpdatedAtUtc)).ToListAsync(token).ConfigureAwait(false);

    public Task<AdminResult<SystemPolicy>> SetPolicyAsync(Guid admin, string key, string valueJson, long? expectedVersion, CancellationToken token)
    {
        if (key is null || !SystemPolicyProposalApplier.KeyPattern().IsMatch(key))
            return Task.FromResult(Fail<SystemPolicy>(AdminStatus.ValidationFailed, "POLICY_KEY_INVALID"));
        if (valueJson is null || Encoding.UTF8.GetByteCount(valueJson) > SystemPolicyRecord.ValueMaxBytes || !IsJson(valueJson))
            return Task.FromResult(Fail<SystemPolicy>(AdminStatus.ValidationFailed, "POLICY_VALUE_INVALID"));
        return WriteAsync(async inner =>
        {
            var now = Now();
            var current = await context.Set<SystemPolicyRecord>().SingleOrDefaultAsync(p => p.Key == key, inner).ConfigureAwait(false);
            if (current is null)
            {
                if (expectedVersion is not null) return Fail<SystemPolicy>(AdminStatus.Conflict, "STALE_POLICY_VERSION");
                current = new SystemPolicyRecord { Key = key, ValueJson = valueJson, Version = 1, UpdatedBy = admin, UpdatedAtUtc = now };
                context.Add(current);
            }
            else
            {
                if (current.Version != expectedVersion) return Fail<SystemPolicy>(AdminStatus.Conflict, "STALE_POLICY_VERSION");
                current.ValueJson = valueJson;
                current.Version++;
                current.UpdatedBy = admin;
                current.UpdatedAtUtc = now;
            }
            try { await context.SaveChangesAsync(inner).ConfigureAwait(false); }
            catch (DbUpdateException) { return Fail<SystemPolicy>(AdminStatus.Conflict, "STALE_POLICY_VERSION"); }
            await AdminAudit.AppendAsync(context, admin, "POLICY_SET", "SYSTEM_POLICY", key, null, new { current.Version }, now, inner).ConfigureAwait(false);
            return AdminResult<SystemPolicy>.Ok(new SystemPolicy(key, valueJson, current.Version, admin, now));
        }, token);
    }

    // ---------------------------------------------------------------- monitoring and audit

    public async Task<OperationalStatistics> GetStatisticsAsync(CancellationToken token)
    {
        async Task<IReadOnlyDictionary<string, long>> ByStatus(IQueryable<string> statuses) =>
            (await statuses.GroupBy(s => s).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync(token).ConfigureAwait(false))
            .ToDictionary(x => x.Key, x => x.Count);
        var roleCounts = (await context.Set<PrincipalRoleRecord>().Where(r => r.IsActive).GroupBy(r => r.Role)
            .Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync(token).ConfigureAwait(false)).ToDictionary(x => x.Key, x => x.Count);
        var customers = await context.Set<UserRecord>().LongCountAsync(u => !context.Set<PrincipalRoleRecord>().Any(r => r.UserId == u.Id && r.IsActive), token)
            .ConfigureAwait(false);
        return new OperationalStatistics(customers, roleCounts.GetValueOrDefault(PrincipalRoles.Staff), roleCounts.GetValueOrDefault(PrincipalRoles.Admin),
            await context.Set<ProjectRecord>().LongCountAsync(token).ConfigureAwait(false),
            await ByStatus(context.Set<SurveyRecord>().Select(s => s.Status)).ConfigureAwait(false),
            await ByStatus(context.Set<GenerationJobRecord>().Select(j => j.Status)).ConfigureAwait(false),
            await ByStatus(context.Set<ReportRecord>().Select(r => r.Status)).ConfigureAwait(false),
            await ByStatus(context.Set<ArchitectRequestRecord>().Select(r => r.Status)).ConfigureAwait(false),
            await context.Set<StaffChangeProposalRecord>().LongCountAsync(p => p.Status == ProposalStatuses.Pending, token).ConfigureAwait(false),
            await context.Set<StaffTaskRecord>().LongCountAsync(t => ActiveTaskStatuses.Contains(t.Status), token).ConfigureAwait(false));
    }

    /// <summary>Metadata-only monitoring rows (no names, files, geometry or document content).</summary>
    public async Task<PagedResult<MonitoringRow>> ListMonitoringAsync(string module, PageRequest page, CancellationToken token)
    {
        IQueryable<MonitoringProjection> query = module switch
        {
            "projects" => context.Set<ProjectRecord>().AsNoTracking()
                .Select(p => new MonitoringProjection { Id = p.Id, ProjectId = p.Id, Status = p.Status, Detail = null, AtUtc = p.CreatedAtUtc }),
            "surveys" => context.Set<SurveyRecord>().AsNoTracking()
                .Select(s => new MonitoringProjection { Id = s.Id, ProjectId = s.ProjectId, Status = s.Status, Detail = s.FailureCode, AtUtc = s.UploadedAtUtc }),
            "generation-jobs" => context.Set<GenerationJobRecord>().AsNoTracking()
                .Select(j => new MonitoringProjection { Id = j.Id, ProjectId = j.ProjectId, Status = j.Status, Detail = j.Outcome, AtUtc = j.CreatedAtUtc }),
            "reports" => context.Set<ReportRecord>().AsNoTracking()
                .Select(r => new MonitoringProjection { Id = r.Id, ProjectId = r.ProjectId, Status = r.Status, Detail = r.FailureCode, AtUtc = r.RequestedAtUtc }),
            "architect-requests" => context.Set<ArchitectRequestRecord>().AsNoTracking()
                .Select(r => new MonitoringProjection { Id = r.RequestId, ProjectId = r.ProjectId, Status = r.Status, Detail = null, AtUtc = r.RequestedAtUtc }),
            _ => throw new ArgumentOutOfRangeException(nameof(module)),
        };
        if (page.Status is { } status) query = query.Where(r => r.Status == status);
        query = page.Sort == "at" ? query.OrderBy(r => r.AtUtc) : query.OrderByDescending(r => r.AtUtc);
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(token).ConfigureAwait(false);
        return new PagedResult<MonitoringRow>(rows.Select(r => new MonitoringRow(r.Id, r.ProjectId, r.Status, r.Detail, r.AtUtc)).ToArray(),
            page.Page, page.PageSize, total);
    }

    private sealed class MonitoringProjection
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public string Status { get; init; } = "";
        public string? Detail { get; init; }
        public DateTimeOffset AtUtc { get; init; }
    }

    public async Task<PagedResult<AuditEvent>> ListAuditAsync(PageRequest page, CancellationToken token)
    {
        var query = context.Set<AdminAuditEventRecord>().AsNoTracking();
        if (page.Search is { } action) query = query.Where(e => e.Action == action.ToUpperInvariant());
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.OrderByDescending(e => e.Sequence).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(token)
            .ConfigureAwait(false);
        return new PagedResult<AuditEvent>(rows.Select(e => new AuditEvent(e.Sequence, e.ActorId, e.Action, e.TargetType, e.TargetId, e.Purpose,
            e.DetailsJson, e.OccurredAtUtc, e.Hash)).ToArray(), page.Page, page.PageSize, total);
    }

    public async Task<AuditVerification> VerifyAuditChainAsync(CancellationToken token)
    {
        var previous = AdminAuditChainHeadRecordConfiguration.GenesisHash;
        long expected = 1, checkedCount = 0, last = 0;
        await foreach (var e in context.Set<AdminAuditEventRecord>().AsNoTracking().OrderBy(e => e.Sequence).AsAsyncEnumerable()
                           .WithCancellation(token).ConfigureAwait(false))
        {
            if (e.Sequence != expected || e.PreviousHash != previous || AdminAudit.Compute(e) != e.Hash)
                return new AuditVerification(false, checkedCount, e.Sequence);
            previous = e.Hash;
            last = e.Sequence;
            expected++;
            checkedCount++;
        }
        var head = await context.Set<AdminAuditChainHeadRecord>().AsNoTracking().SingleAsync(h => h.Id == 1, token).ConfigureAwait(false);
        var headMatches = head.LastSequence == last && head.LastHash == previous;
        return new AuditVerification(headMatches, checkedCount, headMatches ? null : last + 1);
    }

    public Task<AdminResult<Guid>> BootstrapAdminAsync(string normalizedEmail, CancellationToken token) =>
        WriteAsync(async inner =>
        {
            var user = await context.Set<UserRecord>().AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, inner)
                .ConfigureAwait(false);
            if (user is null || !user.IsActive) return Fail<Guid>(AdminStatus.NotFound, "BOOTSTRAP_ACCOUNT_NOT_FOUND");
            await SessionSql.LockUserAsync(context, user.Id, inner).ConfigureAwait(false);
            if (await OtherActiveAdminExistsAsync(Guid.Empty, inner).ConfigureAwait(false))
                return Fail<Guid>(AdminStatus.Conflict, "ADMIN_ALREADY_EXISTS");
            var now = Now();
            var row = await context.Set<PrincipalRoleRecord>().SingleOrDefaultAsync(r => r.UserId == user.Id && r.Role == PrincipalRoles.Admin, inner)
                .ConfigureAwait(false);
            if (row is null)
                context.Add(new PrincipalRoleRecord { Id = Guid.NewGuid(), UserId = user.Id, Role = PrincipalRoles.Admin, IsActive = true, GrantedAtUtc = now });
            else
            {
                row.IsActive = true; row.GrantedBy = null; row.GrantedAtUtc = now; row.RevokedBy = null; row.RevokedAtUtc = null;
            }
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await RevokeSessionsAsync(user.Id, now, "privileges_changed", inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, null, "BOOTSTRAP_ADMIN", "USER", user.Id.ToString("D"), "operator bootstrap", null, now, inner)
                .ConfigureAwait(false);
            return AdminResult<Guid>.Ok(user.Id);
        }, token);
}
