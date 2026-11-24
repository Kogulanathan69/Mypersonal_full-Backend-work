using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Survey;

internal sealed class SurveyRecordConfiguration : IEntityTypeConfiguration<SurveyRecord>
{
    public void Configure(EntityTypeBuilder<SurveyRecord> builder)
    {
        builder.ToTable("surveys");
        builder.HasKey(s => s.Id).HasName("pk_surveys");
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(s => s.UploadedBy).HasColumnName("uploaded_by").IsRequired();
        builder.Property(s => s.StorageId).HasColumnName("storage_id").IsRequired();
        builder.Property(s => s.OriginalFileName).HasColumnName("original_file_name")
            .HasMaxLength(SurveyRecord.FileNameMaxLength).IsRequired();
        builder.Property(s => s.MediaType).HasColumnName("media_type")
            .HasMaxLength(SurveyRecord.MediaTypeMaxLength).IsRequired();
        builder.Property(s => s.ByteLength).HasColumnName("byte_length").IsRequired();
        builder.Property(s => s.Sha256Hash).HasColumnName("sha256_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(s => s.UploadedAtUtc).HasColumnName("uploaded_at_utc").IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(SurveyRecord.StatusMaxLength).IsRequired();
        // UpdateAsync is a compare-and-set on this column.
        builder.Property(s => s.StateVersion).HasColumnName("state_version").IsRequired().IsConcurrencyToken();
        builder.Property(s => s.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(s => s.FailureCode).HasColumnName("failure_code").HasMaxLength(SurveyRecord.FailureCodeMaxLength);
        builder.Property(s => s.FailedAtUtc).HasColumnName("failed_at_utc");

        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(s => s.ProjectId)
            .HasConstraintName("fk_surveys_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRecord>().WithMany().HasForeignKey(s => s.UploadedBy)
            .HasConstraintName("fk_surveys_users_uploaded_by").OnDelete(DeleteBehavior.Restrict);

        builder.Property(s => s.ProcessingLeaseUntilUtc).HasColumnName("processing_lease_until_utc");
        builder.Property(s => s.ProcessingAttempts).HasColumnName("processing_attempts").HasDefaultValue(0).IsRequired();
        builder.HasIndex(s => new { s.Status, s.ProcessingLeaseUntilUtc }).HasDatabaseName("ix_surveys_status_lease");

        builder.HasIndex(s => s.StorageId).IsUnique().HasDatabaseName("ux_surveys_storage_id");
        builder.HasIndex(s => s.UploadedBy).HasDatabaseName("ix_surveys_uploaded_by");
        builder.HasIndex(s => new { s.ProjectId, s.UploadedAtUtc }).HasDatabaseName("ix_surveys_project_id_uploaded_at_utc");
    }
}

internal sealed class SurveyExtractionRecordConfiguration : IEntityTypeConfiguration<SurveyExtractionRecord>
{
    public void Configure(EntityTypeBuilder<SurveyExtractionRecord> builder)
    {
        builder.ToTable("survey_extractions");
        builder.HasKey(e => e.ExtractionId).HasName("pk_survey_extractions");
        builder.Property(e => e.ExtractionId).HasColumnName("extraction_id").ValueGeneratedNever();
        builder.Property(e => e.SurveyId).HasColumnName("survey_id").IsRequired();
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(16).IsRequired();
        builder.Property(e => e.Method).HasColumnName("method").HasMaxLength(16).IsRequired();
        builder.Property(e => e.ResultJson).HasColumnName("result_json").HasColumnType("mediumtext").IsRequired();
        builder.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasOne<SurveyRecord>().WithMany().HasForeignKey(e => e.SurveyId)
            .HasConstraintName("fk_survey_extractions_surveys").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(e => e.ProjectId)
            .HasConstraintName("fk_survey_extractions_projects_project_id").OnDelete(DeleteBehavior.Restrict);
        // One published extraction per survey: publication is idempotent and a stale claimer cannot add a second.
        builder.HasIndex(e => e.SurveyId).IsUnique().HasDatabaseName("ux_survey_extractions_survey_id");
        builder.HasIndex(e => e.ProjectId).HasDatabaseName("ix_survey_extractions_project_id");
    }
}
