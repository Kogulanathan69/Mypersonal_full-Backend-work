using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Administration;

internal sealed class PrincipalRoleRecordConfiguration : IEntityTypeConfiguration<PrincipalRoleRecord>
{
    public void Configure(EntityTypeBuilder<PrincipalRoleRecord> b)
    {
        b.ToTable("principal_roles");
        b.HasKey(r => r.Id).HasName("pk_principal_roles");
        b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        b.Property(r => r.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
        b.Property(r => r.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(r => r.GrantedBy).HasColumnName("granted_by");
        b.Property(r => r.GrantedAtUtc).HasColumnName("granted_at_utc").IsRequired();
        b.Property(r => r.RevokedBy).HasColumnName("revoked_by");
        b.Property(r => r.RevokedAtUtc).HasColumnName("revoked_at_utc");
        b.HasIndex(r => new { r.UserId, r.Role }).IsUnique().HasDatabaseName("ux_principal_roles_user_id_role");
        b.HasIndex(r => new { r.Role, r.IsActive }).HasDatabaseName("ix_principal_roles_role_is_active");
        b.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.UserId)
            .HasConstraintName("fk_principal_roles_users_user_id").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.GrantedBy)
            .HasConstraintName("fk_principal_roles_users_granted_by").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.RevokedBy)
            .HasConstraintName("fk_principal_roles_users_revoked_by").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.GrantedBy).HasDatabaseName("ix_principal_roles_granted_by");
        b.HasIndex(r => r.RevokedBy).HasDatabaseName("ix_principal_roles_revoked_by");
    }
}

internal sealed class StaffProfileRecordConfiguration : IEntityTypeConfiguration<StaffProfileRecord>
{
    public void Configure(EntityTypeBuilder<StaffProfileRecord> b)
    {
        b.ToTable("staff_profiles");
        b.HasKey(s => s.UserId).HasName("pk_staff_profiles");
        b.Property(s => s.UserId).HasColumnName("user_id").ValueGeneratedNever();
        b.Property(s => s.ModulesCsv).HasColumnName("modules_csv").HasMaxLength(StaffProfileRecord.ModulesMaxLength).IsRequired();
        b.Property(s => s.ScopeVersion).HasColumnName("scope_version").IsRequired().IsConcurrencyToken();
        b.Property(s => s.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(s => s.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.HasOne<UserRecord>().WithOne().HasForeignKey<StaffProfileRecord>(s => s.UserId)
            .HasConstraintName("fk_staff_profiles_users_user_id").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(s => s.CreatedBy)
            .HasConstraintName("fk_staff_profiles_users_created_by").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => s.CreatedBy).HasDatabaseName("ix_staff_profiles_created_by");
    }
}

internal sealed class AdminAuditEventRecordConfiguration : IEntityTypeConfiguration<AdminAuditEventRecord>
{
    public void Configure(EntityTypeBuilder<AdminAuditEventRecord> b)
    {
        b.ToTable("admin_audit_events");
        b.HasKey(e => e.Sequence).HasName("pk_admin_audit_events");
        b.Property(e => e.Sequence).HasColumnName("sequence").ValueGeneratedNever();
        b.Property(e => e.ActorId).HasColumnName("actor_id");
        b.Property(e => e.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        b.Property(e => e.TargetType).HasColumnName("target_type").HasMaxLength(64).IsRequired();
        b.Property(e => e.TargetId).HasColumnName("target_id").HasMaxLength(128);
        b.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(500);
        b.Property(e => e.DetailsJson).HasColumnName("details_json").HasColumnType("text").IsRequired();
        b.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        b.Property(e => e.PreviousHash).HasColumnName("previous_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(e => e.Hash).HasColumnName("hash").HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasOne<UserRecord>().WithMany().HasForeignKey(e => e.ActorId)
            .HasConstraintName("fk_admin_audit_events_users_actor_id").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ActorId).HasDatabaseName("ix_admin_audit_events_actor_id");
        b.HasIndex(e => new { e.TargetType, e.TargetId }).HasDatabaseName("ix_admin_audit_events_target");
    }
}

internal sealed class AdminAuditChainHeadRecordConfiguration : IEntityTypeConfiguration<AdminAuditChainHeadRecord>
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public void Configure(EntityTypeBuilder<AdminAuditChainHeadRecord> b)
    {
        b.ToTable("admin_audit_chain_head");
        b.HasKey(h => h.Id).HasName("pk_admin_audit_chain_head");
        b.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(h => h.LastSequence).HasColumnName("last_sequence").IsRequired();
        b.Property(h => h.LastHash).HasColumnName("last_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasData(new AdminAuditChainHeadRecord { Id = 1, LastSequence = 0, LastHash = GenesisHash });
    }
}

internal sealed class StaffChangeProposalRecordConfiguration : IEntityTypeConfiguration<StaffChangeProposalRecord>
{
    public void Configure(EntityTypeBuilder<StaffChangeProposalRecord> b)
    {
        b.ToTable("staff_change_proposals");
        b.HasKey(p => p.Id).HasName("pk_staff_change_proposals");
        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(p => p.Module).HasColumnName("module").HasMaxLength(32).IsRequired();
        b.Property(p => p.TargetType).HasColumnName("target_type").HasMaxLength(64).IsRequired();
        b.Property(p => p.TargetKey).HasColumnName("target_key").HasMaxLength(128).IsRequired();
        b.Property(p => p.ChangeType).HasColumnName("change_type").HasMaxLength(16).IsRequired();
        b.Property(p => p.BaseVersion).HasColumnName("base_version");
        b.Property(p => p.PayloadJson).HasColumnName("payload_json").HasColumnType("mediumtext").IsRequired();
        b.Property(p => p.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(p => p.ProposedBy).HasColumnName("proposed_by").IsRequired();
        b.Property(p => p.ProposedAtUtc).HasColumnName("proposed_at_utc").IsRequired();
        b.Property(p => p.ReviewedBy).HasColumnName("reviewed_by");
        b.Property(p => p.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        b.Property(p => p.ReviewNote).HasColumnName("review_note").HasMaxLength(1000);
        b.Property(p => p.RowVersion).HasColumnName("row_version").IsRequired().IsConcurrencyToken();
        b.HasOne<UserRecord>().WithMany().HasForeignKey(p => p.ProposedBy)
            .HasConstraintName("fk_staff_change_proposals_users_proposed_by").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(p => p.ReviewedBy)
            .HasConstraintName("fk_staff_change_proposals_users_reviewed_by").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.Status, p.ProposedAtUtc }).HasDatabaseName("ix_staff_change_proposals_status");
        b.HasIndex(p => p.ProposedBy).HasDatabaseName("ix_staff_change_proposals_proposed_by");
        b.HasIndex(p => p.ReviewedBy).HasDatabaseName("ix_staff_change_proposals_reviewed_by");
    }
}

internal sealed class StaffTaskRecordConfiguration : IEntityTypeConfiguration<StaffTaskRecord>
{
    public void Configure(EntityTypeBuilder<StaffTaskRecord> b)
    {
        b.ToTable("staff_tasks");
        b.HasKey(t => t.Id).HasName("pk_staff_tasks");
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(t => t.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(t => t.Module).HasColumnName("module").HasMaxLength(32).IsRequired();
        b.Property(t => t.ProjectId).HasColumnName("project_id");
        b.Property(t => t.AssignedTo).HasColumnName("assigned_to").IsRequired();
        b.Property(t => t.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(t => t.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(t => t.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();
        b.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        b.HasOne<ProjectRecord>().WithMany().HasForeignKey(t => t.ProjectId)
            .HasConstraintName("fk_staff_tasks_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(t => t.AssignedTo)
            .HasConstraintName("fk_staff_tasks_users_assigned_to").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(t => t.CreatedBy)
            .HasConstraintName("fk_staff_tasks_users_created_by").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => t.ProjectId).HasDatabaseName("ix_staff_tasks_project_id");
        b.HasIndex(t => new { t.AssignedTo, t.Status }).HasDatabaseName("ix_staff_tasks_assigned_to_status");
        b.HasIndex(t => t.CreatedBy).HasDatabaseName("ix_staff_tasks_created_by");
    }
}

internal sealed class StaffTaskNoteRecordConfiguration : IEntityTypeConfiguration<StaffTaskNoteRecord>
{
    public void Configure(EntityTypeBuilder<StaffTaskNoteRecord> b)
    {
        b.ToTable("staff_task_notes");
        b.HasKey(n => n.Id).HasName("pk_staff_task_notes");
        b.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(n => n.TaskId).HasColumnName("task_id").IsRequired();
        b.Property(n => n.AuthorId).HasColumnName("author_id").IsRequired();
        b.Property(n => n.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
        b.Property(n => n.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        b.Property(n => n.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.HasOne<StaffTaskRecord>().WithMany().HasForeignKey(n => n.TaskId)
            .HasConstraintName("fk_staff_task_notes_staff_tasks_task_id").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(n => n.AuthorId)
            .HasConstraintName("fk_staff_task_notes_users_author_id").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(n => new { n.TaskId, n.CreatedAtUtc }).HasDatabaseName("ix_staff_task_notes_task_id_created");
        b.HasIndex(n => n.AuthorId).HasDatabaseName("ix_staff_task_notes_author_id");
    }
}

internal sealed class AccountSecurityRecordConfiguration : IEntityTypeConfiguration<AccountSecurityRecord>
{
    public void Configure(EntityTypeBuilder<AccountSecurityRecord> b)
    {
        b.ToTable("account_security");
        b.HasKey(a => a.UserId).HasName("pk_account_security");
        b.Property(a => a.UserId).HasColumnName("user_id").ValueGeneratedNever();
        b.Property(a => a.EmailVerifiedAtUtc).HasColumnName("email_verified_at_utc");
        b.Property(a => a.VerificationCodeHash).HasColumnName("verification_code_hash").HasMaxLength(64).IsFixedLength();
        b.Property(a => a.VerificationExpiresAtUtc).HasColumnName("verification_expires_at_utc");
        b.HasOne<UserRecord>().WithOne().HasForeignKey<AccountSecurityRecord>(a => a.UserId)
            .HasConstraintName("fk_account_security_users_user_id").OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SecurityEventRecordConfiguration : IEntityTypeConfiguration<SecurityEventRecord>
{
    public void Configure(EntityTypeBuilder<SecurityEventRecord> b)
    {
        b.ToTable("security_events");
        b.HasKey(e => e.Id).HasName("pk_security_events");
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        b.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        b.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        b.Property(e => e.DetailsJson).HasColumnName("details_json").HasColumnType("text").IsRequired();
        b.HasOne<UserRecord>().WithMany().HasForeignKey(e => e.UserId)
            .HasConstraintName("fk_security_events_users_user_id").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.UserId, e.OccurredAtUtc }).HasDatabaseName("ix_security_events_user_id_occurred");
    }
}

internal sealed class RequirementCatalogueVersionRecordConfiguration : IEntityTypeConfiguration<RequirementCatalogueVersionRecord>
{
    public void Configure(EntityTypeBuilder<RequirementCatalogueVersionRecord> b)
    {
        b.ToTable("requirement_catalogue_versions");
        b.HasKey(r => r.Version).HasName("pk_requirement_catalogue_versions");
        b.Property(r => r.Version).HasColumnName("version").HasMaxLength(64).ValueGeneratedNever();
        b.Property(r => r.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(r => r.Provenance).HasColumnName("provenance").HasMaxLength(64).IsRequired();
        b.Property(r => r.DocumentJson).HasColumnName("document_json").HasColumnType("mediumtext").IsRequired();
        b.Property(r => r.EffectiveFromUtc).HasColumnName("effective_from_utc");
        b.Property(r => r.RowVersion).HasColumnName("row_version").IsRequired().IsConcurrencyToken();
        b.Property(r => r.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.Property(r => r.PublishedBy).HasColumnName("published_by");
        b.Property(r => r.PublishedAtUtc).HasColumnName("published_at_utc");
        b.Property(r => r.RetiredAtUtc).HasColumnName("retired_at_utc");
        b.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.CreatedBy)
            .HasConstraintName("fk_requirement_catalogue_versions_created_by").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.PublishedBy)
            .HasConstraintName("fk_requirement_catalogue_versions_published_by").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => new { r.Status, r.EffectiveFromUtc }).HasDatabaseName("ix_requirement_catalogue_versions_served");
        b.HasIndex(r => r.CreatedBy).HasDatabaseName("ix_requirement_catalogue_versions_created_by");
        b.HasIndex(r => r.PublishedBy).HasDatabaseName("ix_requirement_catalogue_versions_published_by");
    }
}

internal sealed class SystemPolicyRecordConfiguration : IEntityTypeConfiguration<SystemPolicyRecord>
{
    public void Configure(EntityTypeBuilder<SystemPolicyRecord> b)
    {
        b.ToTable("system_policies");
        b.HasKey(p => p.Key).HasName("pk_system_policies");
        b.Property(p => p.Key).HasColumnName("policy_key").HasMaxLength(64).ValueGeneratedNever();
        b.Property(p => p.ValueJson).HasColumnName("value_json").HasColumnType("text").IsRequired();
        b.Property(p => p.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();
        b.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        b.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        b.HasOne<UserRecord>().WithMany().HasForeignKey(p => p.UpdatedBy)
            .HasConstraintName("fk_system_policies_users_updated_by").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.UpdatedBy).HasDatabaseName("ix_system_policies_updated_by");
    }
}
