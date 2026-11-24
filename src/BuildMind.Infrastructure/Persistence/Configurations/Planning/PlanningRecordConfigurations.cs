using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySql.EntityFrameworkCore.Extensions;

namespace BuildMind.Infrastructure.Persistence.Configurations.Planning;

internal sealed class LandRevisionRecordConfiguration : IEntityTypeConfiguration<LandRevisionRecord>
{
    public void Configure(EntityTypeBuilder<LandRevisionRecord> builder)
    {
        builder.ToTable("land_revisions");
        builder.HasKey(r => r.Id).HasName("pk_land_revisions");
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(r => r.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(r => r.LandJson).HasColumnName("land_json").HasColumnType("mediumtext").ForMySQLHasCharset("utf8mb4").IsRequired();
        builder.Property(r => r.AreaM2).HasColumnName("area_m2").IsRequired();
        builder.Property(r => r.EdgeCount).HasColumnName("edge_count").IsRequired();
        builder.Property(r => r.SurveyId).HasColumnName("survey_id");
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(r => r.ProjectId)
            .HasConstraintName("fk_land_revisions_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.CreatedBy)
            .HasConstraintName("fk_land_revisions_users_created_by").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.ProjectId, r.RevisionNumber }).IsUnique().HasDatabaseName("ux_land_revisions_project_id_revision_number");
        builder.HasIndex(r => r.CreatedBy).HasDatabaseName("ix_land_revisions_created_by");
    }
}

internal sealed class GenerationJobRecordConfiguration : IEntityTypeConfiguration<GenerationJobRecord>
{
    public void Configure(EntityTypeBuilder<GenerationJobRecord> builder)
    {
        builder.ToTable("generation_jobs");
        builder.HasKey(j => j.Id).HasName("pk_generation_jobs");
        builder.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(j => j.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(j => j.RequestedBy).HasColumnName("requested_by").IsRequired();
        builder.Property(j => j.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(GenerationJobRecord.KeyMaxLength).IsRequired();
        builder.Property(j => j.LandRevisionId).HasColumnName("land_revision_id").IsRequired();
        builder.Property(j => j.RequirementsRevisionId).HasColumnName("requirements_revision_id").IsRequired();
        builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(j => j.Outcome).HasColumnName("outcome").HasMaxLength(16);
        builder.Property(j => j.ReasonCode).HasColumnName("reason_code").HasMaxLength(GenerationJobRecord.CodeMaxLength);
        builder.Property(j => j.AlgorithmVersion).HasColumnName("algorithm_version").HasMaxLength(GenerationJobRecord.LabelMaxLength);
        builder.Property(j => j.EvidenceMode).HasColumnName("evidence_mode").HasMaxLength(32).IsRequired();
        builder.Property(j => j.Seed).HasColumnName("seed").IsRequired();
        builder.Property(j => j.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(j => j.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(j => j.EvidencePackId).HasColumnName("evidence_pack_id").HasMaxLength(128);
        builder.Property(j => j.DiagnosticsJson).HasColumnName("diagnostics_json").HasColumnType("mediumtext").ForMySQLHasCharset("utf8mb4").IsRequired();
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(j => j.ProjectId)
            .HasConstraintName("fk_generation_jobs_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(j => j.RequestedBy)
            .HasConstraintName("fk_generation_jobs_users_requested_by").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LandRevisionRecord>().WithMany().HasForeignKey(j => j.LandRevisionId)
            .HasConstraintName("fk_generation_jobs_land_revisions").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(j => new { j.ProjectId, j.RequestedBy, j.IdempotencyKey }).IsUnique()
            .HasDatabaseName("ux_generation_jobs_project_actor_key");
        builder.HasIndex(j => j.RequestedBy).HasDatabaseName("ix_generation_jobs_requested_by");
        builder.HasIndex(j => j.LandRevisionId).HasDatabaseName("ix_generation_jobs_land_revision_id");
    }
}

internal sealed class GenerationCandidateRecordConfiguration : IEntityTypeConfiguration<GenerationCandidateRecord>
{
    public void Configure(EntityTypeBuilder<GenerationCandidateRecord> builder)
    {
        builder.ToTable("generation_candidates");
        builder.HasKey(c => c.Id).HasName("pk_generation_candidates");
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(c => c.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(c => c.Ordinal).HasColumnName("ordinal").IsRequired();
        builder.Property(c => c.GeometryJson).HasColumnName("geometry_json").HasColumnType("mediumtext").ForMySQLHasCharset("utf8mb4").IsRequired();
        builder.Property(c => c.GeometryHash).HasColumnName("geometry_hash").HasMaxLength(71).IsRequired();
        builder.Property(c => c.MetricsJson).HasColumnName("metrics_json").HasColumnType("text").IsRequired();
        builder.Property(c => c.ValidationJson).HasColumnName("validation_json").HasColumnType("mediumtext").ForMySQLHasCharset("utf8mb4").IsRequired();
        builder.Property(c => c.DiversitySignature).HasColumnName("diversity_signature").HasMaxLength(128).IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasOne<GenerationJobRecord>().WithMany().HasForeignKey(c => c.JobId)
            .HasConstraintName("fk_generation_candidates_generation_jobs").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(c => c.ProjectId)
            .HasConstraintName("fk_generation_candidates_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.JobId, c.Ordinal }).IsUnique().HasDatabaseName("ux_generation_candidates_job_ordinal");
        builder.HasIndex(c => c.ProjectId).HasDatabaseName("ix_generation_candidates_project_id");
    }
}

internal sealed class PlanEditCandidateRecordConfiguration : IEntityTypeConfiguration<PlanEditCandidateRecord>
{
    public void Configure(EntityTypeBuilder<PlanEditCandidateRecord> builder)
    {
        builder.ToTable("plan_edit_candidates");
        builder.HasKey(c => c.CandidateId).HasName("pk_plan_edit_candidates");
        builder.Property(c => c.CandidateId).HasColumnName("candidate_id").ValueGeneratedNever();
        builder.Property(c => c.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(c => c.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(c => c.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(c => c.BaseRevisionId).HasColumnName("base_revision_id").IsRequired();
        builder.Property(c => c.LandRevisionId).HasColumnName("land_revision_id").IsRequired();
        builder.Property(c => c.RequirementsRevisionId).HasColumnName("requirements_revision_id").IsRequired();
        builder.Property(c => c.CommandId).HasColumnName("command_id").IsRequired();
        builder.Property(c => c.GeometryJson).HasColumnName("geometry_json").HasColumnType("mediumtext").ForMySQLHasCharset("utf8mb4").IsRequired();
        builder.Property(c => c.GeometryHash).HasColumnName("geometry_hash").HasMaxLength(71).IsRequired();
        builder.Property(c => c.GeometrySchemaVersion).HasColumnName("geometry_schema_version").HasMaxLength(64).IsRequired();
        builder.Property(c => c.ValidationId).HasColumnName("validation_id").IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(c => c.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(c => new { c.ProjectId, c.PlanId })
            .HasConstraintName("fk_plan_edit_candidates_plans").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(c => c.OwnerId)
            .HasConstraintName("fk_plan_edit_candidates_users_owner_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanValidationEvidenceRecord>().WithMany().HasForeignKey(c => c.ValidationId)
            .HasConstraintName("fk_plan_edit_candidates_plan_validation_evidence").OnDelete(DeleteBehavior.Restrict);
        // One candidate per edit command on a plan: a replayed command cannot create a second pending candidate.
        builder.HasIndex(c => new { c.ProjectId, c.PlanId, c.CommandId }).IsUnique().HasDatabaseName("ux_plan_edit_candidates_plan_command");
        builder.HasIndex(c => c.OwnerId).HasDatabaseName("ix_plan_edit_candidates_owner_id");
        builder.HasIndex(c => c.ValidationId).HasDatabaseName("ix_plan_edit_candidates_validation_id");
    }
}

internal sealed class PlanConfirmationRecordConfiguration : IEntityTypeConfiguration<PlanConfirmationRecord>
{
    public void Configure(EntityTypeBuilder<PlanConfirmationRecord> builder)
    {
        builder.ToTable("plan_confirmations");
        builder.HasKey(c => c.ConfirmationId).HasName("pk_plan_confirmations");
        builder.Property(c => c.ConfirmationId).HasColumnName("confirmation_id").ValueGeneratedNever();
        builder.Property(c => c.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(c => c.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(c => c.ConfirmedRevisionId).HasColumnName("confirmed_revision_id").IsRequired();
        builder.Property(c => c.GeometryHash).HasColumnName("geometry_hash").HasMaxLength(71).IsRequired();
        builder.Property(c => c.ValidationId).HasColumnName("validation_id").IsRequired();
        builder.Property(c => c.ConfirmedBy).HasColumnName("confirmed_by").IsRequired();
        builder.Property(c => c.ConfirmedAtUtc).HasColumnName("confirmed_at_utc").IsRequired();
        builder.Property(c => c.WarningAcknowledgement).HasColumnName("warning_acknowledgement").IsRequired();
        builder.HasOne<PlanRevisionRecord>().WithMany().HasForeignKey(c => new { c.ProjectId, c.PlanId, c.ConfirmedRevisionId })
            .HasConstraintName("fk_plan_confirmations_plan_revisions").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(c => c.ConfirmedBy)
            .HasConstraintName("fk_plan_confirmations_users_confirmed_by").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.ProjectId, c.PlanId }).IsUnique().HasDatabaseName("ux_plan_confirmations_plan");
        builder.HasIndex(c => c.ConfirmedBy).HasDatabaseName("ix_plan_confirmations_confirmed_by");
        builder.HasIndex(c => new { c.ProjectId, c.PlanId, c.ConfirmedRevisionId }).HasDatabaseName("ix_plan_confirmations_revision");
    }
}

internal sealed class PlanValidationEvidenceRecordConfiguration : IEntityTypeConfiguration<PlanValidationEvidenceRecord>
{
    public void Configure(EntityTypeBuilder<PlanValidationEvidenceRecord> builder)
    {
        builder.ToTable("plan_validation_evidence");
        builder.HasKey(e => e.ValidationId).HasName("pk_plan_validation_evidence");
        builder.Property(e => e.ValidationId).HasColumnName("validation_id").ValueGeneratedNever();
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(e => e.ValidatorVersion).HasColumnName("validator_version").HasMaxLength(64).IsRequired();
        builder.Property(e => e.GeometryHash).HasColumnName("geometry_hash").HasMaxLength(71).IsRequired();
        builder.Property(e => e.GeometryValid).HasColumnName("geometry_valid").IsRequired();
        builder.Property(e => e.RequirementsMatch).HasColumnName("requirements_match");
        builder.Property(e => e.RegulatoryEvidenceStatus).HasColumnName("regulatory_evidence_status").HasMaxLength(64).IsRequired();
        builder.Property(e => e.HasBlockingIssues).HasColumnName("has_blocking_issues").IsRequired();
        builder.Property(e => e.ValidatedAtUtc).HasColumnName("validated_at_utc").IsRequired();
        builder.Property(e => e.DiagnosticsJson).HasColumnName("diagnostics_json").HasColumnType("mediumtext").ForMySQLHasCharset("utf8mb4").IsRequired();
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(e => e.ProjectId)
            .HasConstraintName("fk_plan_validation_evidence_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ProjectId, e.PlanId }).HasDatabaseName("ix_plan_validation_evidence_project_plan");
    }
}
