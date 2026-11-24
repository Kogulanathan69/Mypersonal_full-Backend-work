namespace BuildMind.Infrastructure.Persistence.Records.Administration;

/// <summary>Row of <c>principal_roles</c>: one row per (user, role); revocation keeps the row (history).</summary>
internal sealed class PrincipalRoleRecord
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; init; }
    public bool IsActive { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

/// <summary>Row of <c>staff_profiles</c>: delegated staff module scope with a version for optimistic concurrency.</summary>
internal sealed class StaffProfileRecord
{
    public const int ModulesMaxLength = 512;
    public Guid UserId { get; init; }
    public required string ModulesCsv { get; set; }
    public long ScopeVersion { get; set; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>Row of <c>admin_audit_events</c>: append-only, SHA-256 hash chained (tamper evident).</summary>
internal sealed class AdminAuditEventRecord
{
    public long Sequence { get; init; }
    public Guid? ActorId { get; init; }
    public required string Action { get; init; }
    public required string TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? Purpose { get; init; }
    public required string DetailsJson { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string PreviousHash { get; init; }
    public required string Hash { get; init; }
}

/// <summary>Single-row <c>admin_audit_chain_head</c>; locked FOR UPDATE to serialise appends (always the LAST lock taken).</summary>
internal sealed class AdminAuditChainHeadRecord
{
    public int Id { get; init; }
    public long LastSequence { get; set; }
    public required string LastHash { get; set; }
}

/// <summary>Row of <c>staff_change_proposals</c> (09-ADMIN pending change queue).</summary>
internal sealed class StaffChangeProposalRecord
{
    public const int PayloadMaxBytes = 64 * 1024;
    public Guid Id { get; init; }
    public required string Module { get; init; }
    public required string TargetType { get; init; }
    public required string TargetKey { get; init; }
    public required string ChangeType { get; init; }
    public long? BaseVersion { get; init; }
    public required string PayloadJson { get; init; }
    public required string Status { get; set; }
    public Guid ProposedBy { get; init; }
    public DateTimeOffset ProposedAtUtc { get; init; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public long RowVersion { get; set; }
}

internal sealed class StaffTaskRecord
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Module { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid AssignedTo { get; init; }
    public Guid CreatedBy { get; init; }
    public required string Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class StaffTaskNoteRecord
{
    public Guid Id { get; init; }
    public Guid TaskId { get; init; }
    public Guid AuthorId { get; init; }
    public required string Kind { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>Row of <c>account_security</c>: email verification state and a single outstanding hashed code.</summary>
internal sealed class AccountSecurityRecord
{
    public Guid UserId { get; init; }
    public DateTimeOffset? EmailVerifiedAtUtc { get; set; }
    public string? VerificationCodeHash { get; set; }
    public DateTimeOffset? VerificationExpiresAtUtc { get; set; }
}

/// <summary>Row of <c>security_events</c>: per-user security history (no secrets, no tokens).</summary>
internal sealed class SecurityEventRecord
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string Kind { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string DetailsJson { get; init; }
}

internal sealed class SystemPolicyRecord
{
    public const int ValueMaxBytes = 16 * 1024;
    public required string Key { get; init; }
    public required string ValueJson { get; set; }
    public long Version { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
