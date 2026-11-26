using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserRecordConfiguration : IEntityTypeConfiguration<UserRecord>
{
    public void Configure(EntityTypeBuilder<UserRecord> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id).HasName("pk_users");
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(UserRecord.EmailMaxLength).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(UserRecord.EmailMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(UserRecord.PasswordHashMaxLength).IsRequired();
        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(UserRecord.FullNameMaxLength);
        builder.Property(u => u.PhoneNumber).HasColumnName("phone_number").HasMaxLength(UserRecord.PhoneNumberMaxLength);
        builder.Property(u => u.IsActive).HasColumnName("is_active").IsRequired();
        // Changed only by atomic "token_version = token_version + 1" updates; existing rows start at 1.
        builder.Property(u => u.TokenVersion).HasColumnName("token_version").HasColumnType("bigint").HasDefaultValue(1L).IsRequired();
        builder.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        // IUserRepository.GetByEmailAsync / ExistsByEmailAsync look users up by normalized email.
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
    }
}

internal sealed class RefreshTokenRecordConfiguration : IEntityTypeConfiguration<RefreshTokenRecord>
{
    public void Configure(EntityTypeBuilder<RefreshTokenRecord> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id).HasName("pk_refresh_tokens");
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasColumnType("char(64)")
            .HasMaxLength(RefreshTokenRecord.TokenHashLength).IsFixedLength().IsRequired();
        builder.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(t => t.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(t => t.AbsoluteExpiresAtUtc).HasColumnName("absolute_expires_at_utc").IsRequired();
        builder.Property(t => t.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        builder.Property(t => t.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        builder.Property(t => t.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(t => t.RevocationReason).HasColumnName("revocation_reason")
            .HasMaxLength(RefreshTokenRecord.RevocationReasonMaxLength);

        // Lookup by presented token (hash), revocation by family and by user.
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_refresh_tokens_user_id");

        builder.HasOne<UserRecord>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_refresh_tokens_users_user_id");
    }
}

internal sealed class PasswordResetRecordConfiguration : IEntityTypeConfiguration<PasswordResetRecord>
{
    public void Configure(EntityTypeBuilder<PasswordResetRecord> builder)
    {
        builder.ToTable("password_reset_tokens");
        // One outstanding token per user: issuing a new one replaces the previous one.
        builder.HasKey(r => r.UserId).HasName("pk_password_reset_tokens");
        builder.Property(r => r.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(r => r.TokenHash).HasColumnName("token_hash").HasColumnType("char(64)")
            .HasMaxLength(PasswordResetRecord.TokenHashLength).IsFixedLength().IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(r => r.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(r => r.ConsumedAtUtc).HasColumnName("consumed_at_utc");

        builder.HasOne<UserRecord>()
            .WithOne()
            .HasForeignKey<PasswordResetRecord>(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_password_reset_tokens_users_user_id");
    }
}
