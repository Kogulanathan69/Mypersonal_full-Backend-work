using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildMind.Infrastructure.Persistence.Configurations.Administration;

internal sealed class ArchitectProfileRecordConfiguration : IEntityTypeConfiguration<ArchitectProfileRecord>
{
    public void Configure(EntityTypeBuilder<ArchitectProfileRecord> b)
    {
        b.ToTable("architect_profiles");
        b.HasKey(a => a.RecipientId).HasName("pk_architect_profiles");
        b.Property(a => a.RecipientId).HasColumnName("recipient_id").ValueGeneratedNever();
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        b.Property(a => a.RegistrationNumber).HasColumnName("registration_number").HasMaxLength(64);
        b.Property(a => a.VerificationStatus).HasColumnName("verification_status").HasMaxLength(16).IsRequired();
        b.Property(a => a.VerificationReference).HasColumnName("verification_reference").HasMaxLength(200);
        b.Property(a => a.VerifiedBy).HasColumnName("verified_by");
        b.Property(a => a.VerifiedAtUtc).HasColumnName("verified_at_utc");
        b.Property(a => a.IsListed).HasColumnName("is_listed").IsRequired();
        b.Property(a => a.AcceptingRequests).HasColumnName("accepting_requests").IsRequired();
        b.Property(a => a.RowVersion).HasColumnName("row_version").IsRequired().IsConcurrencyToken();
        b.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.HasIndex(a => a.UserId).IsUnique().HasDatabaseName("ux_architect_profiles_user_id");
        b.HasIndex(a => a.VerifiedBy).HasDatabaseName("ix_architect_profiles_verified_by");
        b.HasOne<UserRecord>().WithMany().HasForeignKey(a => a.UserId)
            .HasConstraintName("fk_architect_profiles_users_user_id").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(a => a.VerifiedBy)
            .HasConstraintName("fk_architect_profiles_users_verified_by").OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ArchitectResponseRecordConfiguration : IEntityTypeConfiguration<ArchitectResponseRecord>
{
    public void Configure(EntityTypeBuilder<ArchitectResponseRecord> b)
    {
        b.ToTable("architect_request_responses");
        b.HasKey(r => r.RequestId).HasName("pk_architect_request_responses");
        b.Property(r => r.RequestId).HasColumnName("request_id").ValueGeneratedNever();
        b.Property(r => r.RecipientId).HasColumnName("recipient_id").IsRequired();
        b.Property(r => r.Decision).HasColumnName("decision").HasMaxLength(16).IsRequired();
        b.Property(r => r.Note).HasColumnName("note").HasMaxLength(2000);
        b.Property(r => r.RespondedBy).HasColumnName("responded_by").IsRequired();
        b.Property(r => r.RespondedAtUtc).HasColumnName("responded_at_utc").IsRequired();
        b.HasOne<ArchitectRequestRecord>().WithOne().HasForeignKey<ArchitectResponseRecord>(r => r.RequestId)
            .HasConstraintName("fk_architect_request_responses_requests").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(r => r.RespondedBy)
            .HasConstraintName("fk_architect_request_responses_users").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.RespondedBy).HasDatabaseName("ix_architect_request_responses_responded_by");
    }
}

internal sealed class ArchitectFeedbackRecordConfiguration : IEntityTypeConfiguration<ArchitectFeedbackRecord>
{
    public void Configure(EntityTypeBuilder<ArchitectFeedbackRecord> b)
    {
        b.ToTable("architect_feedback");
        b.HasKey(f => f.Id).HasName("pk_architect_feedback");
        b.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(f => f.RequestId).HasColumnName("request_id").IsRequired();
        b.Property(f => f.AuthorId).HasColumnName("author_id").IsRequired();
        b.Property(f => f.Kind).HasColumnName("kind").HasMaxLength(24).IsRequired();
        b.Property(f => f.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        b.Property(f => f.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.HasOne<ArchitectRequestRecord>().WithMany().HasForeignKey(f => f.RequestId)
            .HasConstraintName("fk_architect_feedback_requests").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(f => f.AuthorId)
            .HasConstraintName("fk_architect_feedback_users").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(f => new { f.RequestId, f.CreatedAtUtc }).HasDatabaseName("ix_architect_feedback_request_id");
        b.HasIndex(f => f.AuthorId).HasDatabaseName("ix_architect_feedback_author_id");
    }
}

internal sealed class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> b)
    {
        b.ToTable("notifications");
        b.HasKey(n => n.Id).HasName("pk_notifications");
        b.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        b.Property(n => n.Kind).HasColumnName("kind").HasMaxLength(48).IsRequired();
        b.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(n => n.Body).HasColumnName("body").HasMaxLength(1000).IsRequired();
        b.Property(n => n.Link).HasColumnName("link").HasMaxLength(300);
        b.Property(n => n.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.Property(n => n.ReadAtUtc).HasColumnName("read_at_utc");
        b.HasOne<UserRecord>().WithMany().HasForeignKey(n => n.UserId)
            .HasConstraintName("fk_notifications_users_user_id").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(n => new { n.UserId, n.ReadAtUtc, n.CreatedAtUtc }).HasDatabaseName("ix_notifications_user_unread");
    }
}

internal sealed class OutboxMessageRecordConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(o => o.Id).HasName("pk_outbox_messages");
        b.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(o => o.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
        b.Property(o => o.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        b.Property(o => o.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        b.Property(o => o.Body).HasColumnName("body").HasColumnType("text").IsRequired();
        b.Property(o => o.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(o => o.Attempts).HasColumnName("attempts").IsRequired();
        b.Property(o => o.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc").IsRequired();
        b.Property(o => o.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.Property(o => o.SentAtUtc).HasColumnName("sent_at_utc");
        b.Property(o => o.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(64);
        b.Property(o => o.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(128).IsRequired();
        b.HasIndex(o => o.DedupeKey).IsUnique().HasDatabaseName("ux_outbox_messages_dedupe_key");
        b.HasIndex(o => new { o.Status, o.NextAttemptAtUtc }).HasDatabaseName("ix_outbox_messages_due");
        b.HasIndex(o => o.RecipientUserId).HasDatabaseName("ix_outbox_messages_recipient");
        b.HasOne<UserRecord>().WithMany().HasForeignKey(o => o.RecipientUserId)
            .HasConstraintName("fk_outbox_messages_users_recipient").OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationPreferenceRecordConfiguration : IEntityTypeConfiguration<NotificationPreferenceRecord>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceRecord> b)
    {
        b.ToTable("notification_preferences");
        b.HasKey(p => p.UserId).HasName("pk_notification_preferences");
        b.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
        b.Property(p => p.EmailEnabled).HasColumnName("email_enabled").IsRequired();
        b.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        b.HasOne<UserRecord>().WithOne().HasForeignKey<NotificationPreferenceRecord>(p => p.UserId)
            .HasConstraintName("fk_notification_preferences_users").OnDelete(DeleteBehavior.Restrict);
    }
}
