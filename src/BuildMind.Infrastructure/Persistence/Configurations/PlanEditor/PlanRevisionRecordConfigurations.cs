using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySql.EntityFrameworkCore.Extensions;

namespace BuildMind.Infrastructure.Persistence.Configurations.PlanEditor;

internal sealed class PlanRecordConfiguration : IEntityTypeConfiguration<PlanRecord>
{
    public void Configure(EntityTypeBuilder<PlanRecord> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(p => new { p.ProjectId, p.PlanId }).HasName("pk_plans");
        builder.Property(p => p.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
        builder.Property(p => p.PlanId).HasColumnName("plan_id").ValueGeneratedNever();
        builder.Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");

        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(p => p.ProjectId)
            .HasConstraintName("fk_plans_projects").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanRevisionRecord>().WithMany()
            .HasForeignKey(p => new { p.ProjectId, p.PlanId, p.HeadRevisionId })
            .HasConstraintName("fk_plans_plan_revisions_head").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.ProjectId, p.PlanId, p.HeadRevisionId })
            .HasDatabaseName("ix_plans_head_revision");
    }
}

internal sealed class PlanRevisionRecordConfiguration : IEntityTypeConfiguration<PlanRevisionRecord>
{
    public void Configure(EntityTypeBuilder<PlanRevisionRecord> builder)
    {
        builder.ToTable("plan_revisions", table =>
        {
            table.HasCheckConstraint("ck_plan_revisions_version", "version_number BETWEEN 1 AND 9223372036854775806");
            table.HasCheckConstraint("ck_plan_revisions_parent",
                "(version_number = 1 AND parent_revision_id IS NULL) OR " +
                "(version_number > 1 AND parent_revision_id IS NOT NULL AND parent_revision_id <> revision_id)");
            table.HasCheckConstraint("ck_plan_revisions_geometry_size",
                FormattableString.Invariant($"OCTET_LENGTH(geometry_json) BETWEEN 1 AND {PlanRevisionRecord.ProvisionalGeometryMaxBytes}"));
            table.HasCheckConstraint("ck_plan_revisions_digest_sizes",
                "OCTET_LENGTH(geometry_hash) = 32 AND OCTET_LENGTH(validation_geometry_hash) = 32");
        });
        builder.HasKey(r => new { r.ProjectId, r.PlanId, r.RevisionId }).HasName("pk_plan_revisions");
        builder.Property(r => r.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
        builder.Property(r => r.PlanId).HasColumnName("plan_id").ValueGeneratedNever();
        builder.Property(r => r.RevisionId).HasColumnName("revision_id").ValueGeneratedNever();
        builder.Property(r => r.ParentRevisionId).HasColumnName("parent_revision_id");
        builder.Property(r => r.VersionNumber).HasColumnName("version_number");
        builder.Property(r => r.GeometrySchemaVersion).HasColumnName("geometry_schema_version")
            .HasMaxLength(PlanRevisionRecord.VersionLabelMaxLength);
        builder.Property(r => r.GeometryJson).HasColumnName("geometry_json").HasColumnType("mediumtext")
            .ForMySQLHasCharset("utf8mb4");
        ConfigureHash(builder.Property(r => r.GeometryHash), "geometry_hash");
        builder.Property(r => r.LandRevisionId).HasColumnName("land_revision_id");
        builder.Property(r => r.RequirementsRevisionId).HasColumnName("requirements_revision_id");
        builder.Property(r => r.EditCommandId).HasColumnName("edit_command_id");
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(r => r.ChangeSummary).HasColumnName("change_summary")
            .HasMaxLength(PlanRevisionRecord.ChangeSummaryMaxLength);

        builder.Property(r => r.ValidationId).HasColumnName("validation_id");
        builder.Property(r => r.ValidatorVersion).HasColumnName("validator_version")
            .HasMaxLength(PlanRevisionRecord.VersionLabelMaxLength);
        ConfigureHash(builder.Property(r => r.ValidationGeometryHash), "validation_geometry_hash");
        builder.Property(r => r.GeometryValid).HasColumnName("geometry_valid");
        builder.Property(r => r.RequirementsMatch).HasColumnName("requirements_match");
        builder.Property(r => r.RegulatoryEvidenceStatus).HasColumnName("regulatory_evidence_status")
            .HasMaxLength(PlanRevisionRecord.EvidenceStatusMaxLength);
        builder.Property(r => r.HasBlockingIssues).HasColumnName("has_blocking_issues");
        builder.Property(r => r.ValidatedAtUtc).HasColumnName("validated_at_utc");

        builder.HasOne<PlanRecord>().WithMany().HasForeignKey(r => new { r.ProjectId, r.PlanId })
            .HasConstraintName("fk_plan_revisions_plans").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanRevisionRecord>().WithMany()
            .HasForeignKey(r => new { r.ProjectId, r.PlanId, r.ParentRevisionId })
            // InnoDB's default rejects deletes. Leave the action clause implicit for the parent CHECK.
            .HasConstraintName("fk_plan_revisions_parent").OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasIndex(r => new { r.ProjectId, r.PlanId, r.ParentRevisionId })
            .HasDatabaseName("ix_plan_revisions_parent");
        builder.HasIndex(r => new { r.ProjectId, r.PlanId, r.VersionNumber }).IsUnique()
            .HasDatabaseName("ux_plan_revisions_version");
    }

    private static void ConfigureHash(PropertyBuilder<string> property, string columnName) =>
        property.HasColumnName(columnName).HasColumnType("varbinary(32)").HasConversion<PlanGeometryHashConverter>();
}

internal sealed class PlanGeometryHashConverter() : ValueConverter<string, byte[]>(
    value => ToDigest(value), digest => ToCanonicalHash(digest))
{
    private const string Prefix = "sha256:";

    private static byte[] ToDigest(string value)
    {
        if (value.Length != Prefix.Length + 64 || !value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException("Expected a canonical SHA-256 geometry hash.");

        var digest = Convert.FromHexString(value.AsSpan(Prefix.Length));
        if (!string.Equals(value, ToCanonicalHash(digest), StringComparison.Ordinal))
            throw new FormatException("Expected a canonical SHA-256 geometry hash.");

        return digest;
    }

    private static string ToCanonicalHash(byte[] digest)
    {
        if (digest.Length != 32)
            throw new FormatException("Expected a 32-byte SHA-256 geometry digest.");

        return Prefix + Convert.ToHexStringLower(digest);
    }
}
