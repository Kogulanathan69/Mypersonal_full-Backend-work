namespace BuildMind.Application.Features.Administration;

/// <summary>
/// Captain-local role model (proposed, not ratified), following V3 09-ADMIN: two primary categories, Customer (no role
/// row) and Admin, plus delegated Staff with module scopes, plus Architect for the portal. Privileges are never carried
/// in the JWT; they are read fresh from the database on every privileged request (deny by default).
/// </summary>
public static class PrincipalRoles
{
    public const string Admin = "ADMIN";
    public const string Staff = "STAFF";
    public const string Architect = "ARCHITECT";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Admin, Staff, Architect };
}

/// <summary>Staff module scopes. Staff may VIEW an assigned module; every staff write is a pending proposal.</summary>
public static class AdminModules
{
    public const string Customers = "CUSTOMERS";
    public const string Projects = "PROJECTS";
    public const string Surveys = "SURVEYS";
    public const string Reports = "REPORTS";
    public const string ArchitectRequests = "ARCHITECT_REQUESTS";
    public const string ArchitectDirectory = "ARCHITECT_DIRECTORY";
    public const string RequirementCatalogue = "REQUIREMENT_CATALOGUE";
    public const string RegulatoryConstraints = "REGULATORY_CONSTRAINTS";
    public const string SystemPolicies = "SYSTEM_POLICIES";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Customers, Projects, Surveys, Reports, ArchitectRequests, ArchitectDirectory, RequirementCatalogue,
        RegulatoryConstraints, SystemPolicies,
    };
}

/// <summary>Fresh, server-side privilege view of one authenticated user.</summary>
public sealed record PrincipalContext(Guid UserId, bool IsActive, IReadOnlySet<string> Roles, IReadOnlySet<string> StaffModules)
{
    public bool IsAdmin => IsActive && Roles.Contains(PrincipalRoles.Admin);
    public bool IsStaff => IsActive && Roles.Contains(PrincipalRoles.Staff);
    public bool IsArchitect => IsActive && Roles.Contains(PrincipalRoles.Architect);

    /// <summary>Admin sees every module; staff only their assigned modules; nobody else.</summary>
    public bool CanView(string module) => IsAdmin || (IsStaff && StaffModules.Contains(module));
}

public interface IPrincipalDirectory
{
    /// <summary>Reads roles, scopes and account state from the database (no caching). Null for unknown users.</summary>
    Task<PrincipalContext?> GetAsync(Guid userId, CancellationToken cancellationToken);
}

public enum AdminStatus { Success, NotFound, Forbidden, ValidationFailed, Conflict }

public sealed record AdminResult<T>(AdminStatus Status, T? Value = default, string? Code = null)
{
    public static AdminResult<T> Ok(T value) => new(AdminStatus.Success, value);
}

public sealed record PageRequest(int Page, int PageSize, string? Search, string? Status, string? Sort)
{
    public const int MaxPageSize = 100;

    public static PageRequest Normalize(int? page, int? pageSize, string? search, string? status, string? sort) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 25, 1, MaxPageSize),
            string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(search.Trim().Length, 100)],
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(), string.IsNullOrWhiteSpace(sort) ? null : sort.Trim());
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

public sealed record AccountSummary(Guid UserId, string Email, string? FullName, bool IsActive, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> Roles, int ProjectCount);

public sealed record AccountDetail(AccountSummary Account, IReadOnlyList<string> StaffModules, int SurveyCount, int ReportCount,
    int ArchitectRequestCount, long TokenVersion);

public sealed record StaffSummary(Guid UserId, string Email, string? FullName, bool IsActive, IReadOnlyList<string> Modules,
    long ScopeVersion, DateTimeOffset CreatedAtUtc);

public sealed record AuditEvent(long Sequence, Guid? ActorId, string Action, string TargetType, string? TargetId, string? Purpose,
    string DetailsJson, DateTimeOffset OccurredAtUtc, string Hash);

public sealed record AuditVerification(bool Intact, long EventsChecked, long? FirstBrokenSequence);

public static class ProposalStatuses
{
    public const string Pending = "PENDING";
    public const string Applied = "APPLIED";
    public const string Rejected = "REJECTED";
    public const string Stale = "STALE";
    public const string Withdrawn = "WITHDRAWN";
}

public sealed record ChangeProposal(Guid ProposalId, string Module, string TargetType, string TargetKey, string ChangeType,
    long? BaseVersion, string PayloadJson, string Status, Guid ProposedBy, DateTimeOffset ProposedAtUtc, Guid? ReviewedBy,
    DateTimeOffset? ReviewedAtUtc, string? ReviewNote, long RowVersion);

public sealed record SubmitProposalCommand(string? TargetType, string? TargetKey, string? ChangeType, long? BaseVersion, string? PayloadJson);

public static class TaskStatuses
{
    public const string Open = "OPEN";
    public const string InProgress = "IN_PROGRESS";
    public const string Escalated = "ESCALATED";
    public const string Done = "DONE";
    public const string Cancelled = "CANCELLED";

    /// <summary>Allowed transitions (Captain-local policy). Terminal: DONE, CANCELLED.</summary>
    public static bool CanMove(string from, string to) => (from, to) switch
    {
        (Open, InProgress or Escalated or Done or Cancelled) => true,
        (InProgress, Escalated or Done or Cancelled) => true,
        (Escalated, InProgress or Done or Cancelled) => true,
        _ => false,
    };
}

public sealed record StaffTask(Guid TaskId, string Title, string? Description, string Module, Guid? ProjectId, Guid AssignedTo,
    Guid CreatedBy, string Status, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record TaskNote(Guid NoteId, Guid TaskId, Guid AuthorId, string Kind, string Body, DateTimeOffset CreatedAtUtc);

public sealed record ProjectReviewView(Guid ProjectId, string Name, string Status, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<object> Surveys, bool HasLand, bool HasRequirements, int PlanCount, int ReportCount);

public sealed record SystemPolicy(string Key, string ValueJson, long Version, Guid? UpdatedBy, DateTimeOffset UpdatedAtUtc);

public sealed record OperationalStatistics(long Customers, long Staff, long Admins, long Projects,
    IReadOnlyDictionary<string, long> SurveysByStatus, IReadOnlyDictionary<string, long> GenerationJobsByStatus,
    IReadOnlyDictionary<string, long> ReportsByStatus, IReadOnlyDictionary<string, long> ArchitectRequestsByStatus,
    long PendingProposals, long OpenTasks);

public sealed record MonitoringRow(Guid Id, Guid ProjectId, string Status, string? Detail, DateTimeOffset AtUtc);

/// <summary>Admin/staff persistence and workflows. Every write is transactional and appends to the audit chain.</summary>
public interface IAdministrationStore
{
    // Accounts
    Task<PagedResult<AccountSummary>> ListAccountsAsync(PageRequest page, bool customersOnly, CancellationToken token);
    Task<AccountDetail?> GetAccountAsync(Guid userId, CancellationToken token);
    Task<AdminResult<AccountSummary>> SetAccountActiveAsync(Guid actor, Guid target, bool active, string reason, CancellationToken token);
    Task<AdminResult<bool>> SetRoleAsync(Guid actor, Guid target, string role, bool granted, CancellationToken token);

    // Staff
    Task<AdminResult<Guid>> CreateStaffAsync(Guid actor, string email, string? fullName, IReadOnlyCollection<string> modules,
        string unusablePasswordHash, CancellationToken token);
    Task<PagedResult<StaffSummary>> ListStaffAsync(PageRequest page, CancellationToken token);
    Task<AdminResult<StaffSummary>> SetStaffModulesAsync(Guid actor, Guid staff, IReadOnlyCollection<string> modules, long expectedScopeVersion,
        CancellationToken token);

    // Proposals (09-ADMIN pending change queue)
    Task<AdminResult<ChangeProposal>> SubmitProposalAsync(Guid staff, string module, SubmitProposalCommand command, CancellationToken token);
    Task<PagedResult<ChangeProposal>> ListProposalsAsync(PageRequest page, Guid? proposedBy, IReadOnlySet<string>? modules, CancellationToken token);
    Task<AdminResult<ChangeProposal>> ReviewProposalAsync(Guid admin, Guid proposalId, bool approve, long expectedRowVersion, string? note,
        CancellationToken token);
    Task<AdminResult<ChangeProposal>> WithdrawProposalAsync(Guid staff, Guid proposalId, long expectedRowVersion, CancellationToken token);

    // Tasks
    Task<AdminResult<StaffTask>> CreateTaskAsync(Guid admin, Guid assignee, string title, string? description, string module, Guid? projectId,
        CancellationToken token);
    Task<PagedResult<StaffTask>> ListTasksAsync(PageRequest page, Guid? assignedTo, CancellationToken token);
    Task<StaffTask?> GetTaskAsync(Guid taskId, CancellationToken token);
    Task<IReadOnlyList<TaskNote>> ListTaskNotesAsync(Guid taskId, CancellationToken token);
    Task<AdminResult<StaffTask>> MoveTaskAsync(Guid actor, Guid taskId, string status, long expectedVersion, string? note, CancellationToken token);
    Task<AdminResult<TaskNote>> AddTaskNoteAsync(Guid actor, Guid taskId, string body, CancellationToken token);
    Task<AdminResult<ProjectReviewView>> ReviewTaskProjectAsync(Guid staff, Guid taskId, string purpose, CancellationToken token);

    // Policies
    Task<IReadOnlyList<SystemPolicy>> ListPoliciesAsync(CancellationToken token);
    Task<AdminResult<SystemPolicy>> SetPolicyAsync(Guid admin, string key, string valueJson, long? expectedVersion, CancellationToken token);

    // Monitoring and audit
    Task<OperationalStatistics> GetStatisticsAsync(CancellationToken token);
    Task<PagedResult<MonitoringRow>> ListMonitoringAsync(string module, PageRequest page, CancellationToken token);
    Task<PagedResult<AuditEvent>> ListAuditAsync(PageRequest page, CancellationToken token);
    Task<AuditVerification> VerifyAuditChainAsync(CancellationToken token);

    /// <summary>Grants ADMIN to an existing active account only when no active admin exists. Audited as BOOTSTRAP.</summary>
    Task<AdminResult<Guid>> BootstrapAdminAsync(string normalizedEmail, CancellationToken token);
}

/// <summary>Outbound transactional email (invitations, recovery, verification). Implementations never log bodies.</summary>
public interface ISystemEmailSender
{
    bool IsConfigured { get; }
    Task<bool> SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken);
}

public sealed record CatalogueVersionView(string Version, string Status, string Provenance, DateTimeOffset? EffectiveFromUtc, long RowVersion,
    Guid CreatedBy, DateTimeOffset CreatedAtUtc, Guid? PublishedBy, DateTimeOffset? PublishedAtUtc, DateTimeOffset? RetiredAtUtc);

/// <summary>Admin management of the persistent requirement catalogue (product preferences, not regulations).</summary>
public interface IRequirementCatalogueAdministration
{
    Task<IReadOnlyList<CatalogueVersionView>> ListAsync(CancellationToken token);
    Task<(CatalogueVersionView View, string Document)?> GetAsync(string version, CancellationToken token);
    Task<AdminResult<CatalogueVersionView>> CreateDraftAsync(Guid admin, string version, string provenance, string documentJson, CancellationToken token);
    Task<AdminResult<CatalogueVersionView>> TransitionAsync(Guid admin, string version, string target, long expectedRowVersion,
        DateTimeOffset? effectiveFromUtc, CancellationToken token);
}
