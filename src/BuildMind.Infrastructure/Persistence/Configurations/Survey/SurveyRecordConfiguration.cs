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

        builder.HasIndex(s => s.StorageId).IsUnique().HasDatabaseName("ux_surveys_storage_id");
        builder.HasIndex(s => s.UploadedBy).HasDatabaseName("ix_surveys_uploaded_by");
        builder.HasIndex(s => new { s.ProjectId, s.UploadedAtUtc }).HasDatabaseName("ix_surveys_project_id_uploaded_at_utc");
    }
}
