using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Requirements;

internal sealed class RequirementRevisionRecordConfiguration : IEntityTypeConfiguration<RequirementRevisionRecord>
{
    public void Configure(EntityTypeBuilder<RequirementRevisionRecord> builder)
    {
        builder.ToTable("requirement_revisions");
        builder.HasKey(r => r.Id).HasName("pk_requirement_revisions");
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(r => r.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(r => r.SchemaVersion).HasColumnName("schema_version").HasMaxLength(RequirementRevisionRecord.VersionMaxLength).IsRequired();
        builder.Property(r => r.CatalogueVersion).HasColumnName("catalogue_version").HasMaxLength(RequirementRevisionRecord.VersionMaxLength).IsRequired();
        builder.Property(r => r.ContentJson).HasColumnName("content_json").HasColumnType("json").IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasOne<ProjectRecord>()
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .HasConstraintName("fk_requirement_revisions_projects_project_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Consecutive numbering per project; a concurrent duplicate save is rejected by the database.
        builder.HasIndex(r => new { r.ProjectId, r.RevisionNumber })
            .IsUnique()
            .HasDatabaseName("ux_requirement_revisions_project_id_revision_number");

        // Lets the head pointer reference a revision of the SAME project (composite FK below).
        builder.HasAlternateKey(r => new { r.ProjectId, r.Id }).HasName("ak_requirement_revisions_project_id_id");
    }
}

internal sealed class RequirementHeadRecordConfiguration : IEntityTypeConfiguration<RequirementHeadRecord>
{
    public void Configure(EntityTypeBuilder<RequirementHeadRecord> builder)
    {
        builder.ToTable("project_requirement_heads");
        builder.HasKey(h => h.ProjectId).HasName("pk_project_requirement_heads");
        builder.Property(h => h.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
        builder.Property(h => h.CurrentRevisionId).HasColumnName("current_revision_id").IsRequired();
        builder.Property(h => h.CurrentRevisionNumber).HasColumnName("current_revision_number").IsRequired();

        builder.HasOne<ProjectRecord>()
            .WithOne()
            .HasForeignKey<RequirementHeadRecord>(h => h.ProjectId)
            .HasConstraintName("fk_project_requirement_heads_projects_project_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Supporting index for the composite FK (named explicitly so EF does not generate a mixed-case "IX_" name).
        builder.HasIndex(h => new { h.ProjectId, h.CurrentRevisionId })
            .HasDatabaseName("ix_project_requirement_heads_project_id_current_revision_id");

        builder.HasOne<RequirementRevisionRecord>()
            .WithMany()
            .HasForeignKey(h => new { h.ProjectId, h.CurrentRevisionId })
            .HasPrincipalKey(r => new { r.ProjectId, r.Id })
            .HasConstraintName("fk_project_requirement_heads_requirement_revisions_current")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
