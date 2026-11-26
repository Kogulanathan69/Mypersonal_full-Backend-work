using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Requirements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Requirements;

internal sealed class RequirementMutationReceiptConfiguration : IEntityTypeConfiguration<RequirementMutationReceiptRecord>
{
    public void Configure(EntityTypeBuilder<RequirementMutationReceiptRecord> builder)
    {
        builder.ToTable("requirement_mutation_receipts");
        builder.HasKey(r => new { r.ProjectId, r.ActorId, r.OperationId }).HasName("pk_requirement_mutation_receipts");
        builder.Property(r => r.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
        builder.Property(r => r.ActorId).HasColumnName("actor_id").ValueGeneratedNever();
        builder.Property(r => r.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(r => r.AuthorizationVersion).HasColumnName("authorization_version");
        builder.Property(r => r.Category).HasColumnName("category");
        builder.Property(r => r.FormatVersion).HasColumnName("format_version");
        builder.Property(r => r.Digest).HasColumnName("digest").HasColumnType("binary(32)");
        builder.Property(r => r.SavedRevisionId).HasColumnName("saved_revision_id");
        builder.HasIndex(r => new { r.ProjectId, r.SavedRevisionId }).HasDatabaseName("ix_requirement_mutation_receipts_revision");
        builder.HasOne<RequirementRevisionRecord>().WithMany()
            .HasForeignKey(r => new { r.ProjectId, r.SavedRevisionId })
            .HasPrincipalKey(r => new { r.ProjectId, r.Id })
            .HasConstraintName("fk_requirement_mutation_receipts_revision").OnDelete(DeleteBehavior.Restrict);
    }
}
