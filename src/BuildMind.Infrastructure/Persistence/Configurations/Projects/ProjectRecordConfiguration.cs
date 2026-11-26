using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Projects;

internal sealed class ProjectRecordConfiguration : IEntityTypeConfiguration<ProjectRecord>
{
    public void Configure(EntityTypeBuilder<ProjectRecord> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id).HasName("pk_projects");
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(p => p.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(ProjectRecord.NameMaxLength).IsRequired();
        builder.Property(p => p.ProjectType).HasColumnName("project_type").HasMaxLength(ProjectRecord.ProjectTypeMaxLength);
        builder.Property(p => p.LocationReference).HasColumnName("location_reference").HasMaxLength(ProjectRecord.LocationReferenceMaxLength);
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(ProjectRecord.StatusMaxLength).IsRequired();
        // IProjectRepository.UpdateAsync(project, ownerId, expectedRevision) is a compare-and-set on this column.
        builder.Property(p => p.Revision).HasColumnName("revision").IsRequired().IsConcurrencyToken();
        builder.Property(p => p.AuthorizationVersion)
       .HasColumnName("authorization_version")
       .HasColumnType("bigint")
       .HasDefaultValue(1L)
       .IsRequired()
       .IsConcurrencyToken(); builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasOne<UserRecord>()
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .HasConstraintName("fk_projects_users_owner_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Owner-scoped listing (GetByOwnerIdAsync) with deterministic creation ordering.
        builder.HasIndex(p => new { p.OwnerId, p.CreatedAtUtc }).HasDatabaseName("ix_projects_owner_id_created_at_utc");
    }
}
