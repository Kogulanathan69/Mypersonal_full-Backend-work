using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Deliverables;

internal sealed class ReportRecordConfiguration : IEntityTypeConfiguration<ReportRecord>
{
    public void Configure(EntityTypeBuilder<ReportRecord> builder)
    {
        builder.ToTable("reports");
        builder.HasKey(r => r.Id).HasName("pk_reports");
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(r => r.RequestedBy).HasColumnName("requested_by").IsRequired();
        builder.Property(r => r.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        builder.Property(r => r.SnapshotSchemaVersion).HasColumnName("snapshot_schema_version").HasMaxLength(64).IsRequired();
        builder.Property(r => r.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired();
        builder.Property(r => r.RequirementsRevisionId).HasColumnName("requirements_revision_id").IsRequired();
        builder.Property(r => r.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(r => r.PlanRevisionId).HasColumnName("plan_revision_id").IsRequired();
        builder.Property(r => r.ConfirmationId).HasColumnName("confirmation_id").IsRequired();
        builder.Property(r => r.DisclaimerVersion).HasColumnName("disclaimer_version").HasMaxLength(64).IsRequired();
        builder.Property(r => r.DisclaimerText).HasColumnName("disclaimer_text").HasMaxLength(2000).IsRequired();
        builder.Property(r => r.StateVersion).HasColumnName("state_version").IsRequired().IsConcurrencyToken();
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(r => r.ArtifactId).HasColumnName("artifact_id");
        builder.Property(r => r.ArtifactMediaType).HasColumnName("artifact_media_type").HasMaxLength(64);
        builder.Property(r => r.ArtifactByteLength).HasColumnName("artifact_byte_length");
        builder.Property(r => r.ArtifactContentHash).HasColumnName("artifact_content_hash").HasMaxLength(71);
        builder.Property(r => r.ArtifactCreatedAtUtc).HasColumnName("artifact_created_at_utc");
        builder.Property(r => r.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(r => r.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(r => r.FailedAtUtc).HasColumnName("failed_at_utc");
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(r => r.ProjectId)
            .HasConstraintName("fk_reports_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.RequestedBy)
            .HasConstraintName("fk_reports_users_requested_by").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanConfirmationRecord>().WithMany().HasForeignKey(r => r.ConfirmationId)
            .HasConstraintName("fk_reports_plan_confirmations").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.ArtifactId).IsUnique().HasDatabaseName("ux_reports_artifact_id");
        builder.HasIndex(r => new { r.ProjectId, r.RequestedAtUtc }).HasDatabaseName("ix_reports_project_requested");
        builder.HasIndex(r => r.RequestedBy).HasDatabaseName("ix_reports_requested_by");
        builder.HasIndex(r => r.ConfirmationId).HasDatabaseName("ix_reports_confirmation_id");
    }
}

internal sealed class ArchitectRequestRecordConfiguration : IEntityTypeConfiguration<ArchitectRequestRecord>
{
    public void Configure(EntityTypeBuilder<ArchitectRequestRecord> builder)
    {
        builder.ToTable("architect_requests");
        builder.HasKey(r => r.RequestId).HasName("pk_architect_requests");
        builder.Property(r => r.RequestId).HasColumnName("request_id").ValueGeneratedNever();
        builder.Property(r => r.ConsentId).HasColumnName("consent_id").IsRequired();
        builder.Property(r => r.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(r => r.GrantedBy).HasColumnName("granted_by").IsRequired();
        builder.Property(r => r.RecipientId).HasColumnName("recipient_id").IsRequired();
        builder.Property(r => r.ReportId).HasColumnName("report_id");
        builder.Property(r => r.DocumentIdsJson).HasColumnName("document_ids_json").HasColumnType("text").IsRequired();
        builder.Property(r => r.PolicyVersion).HasColumnName("policy_version").HasMaxLength(128).IsRequired();
        builder.Property(r => r.GrantedAtUtc).HasColumnName("granted_at_utc").IsRequired();
        builder.Property(r => r.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        builder.Property(r => r.StateVersion).HasColumnName("state_version").IsRequired().IsConcurrencyToken();
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(r => r.WithdrawnAtUtc).HasColumnName("withdrawn_at_utc");
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(r => r.ProjectId)
            .HasConstraintName("fk_architect_requests_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.GrantedBy)
            .HasConstraintName("fk_architect_requests_users_granted_by").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReportRecord>().WithMany().HasForeignKey(r => r.ReportId)
            .HasConstraintName("fk_architect_requests_reports").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.ConsentId).IsUnique().HasDatabaseName("ux_architect_requests_consent_id");
        builder.HasIndex(r => new { r.ProjectId, r.RequestedAtUtc }).HasDatabaseName("ix_architect_requests_project_requested");
        builder.HasIndex(r => r.GrantedBy).HasDatabaseName("ix_architect_requests_granted_by");
        builder.HasIndex(r => r.ReportId).HasDatabaseName("ix_architect_requests_report_id");
    }
}
