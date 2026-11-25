namespace BuildMind.Application.Features.Administration;

public static class ArchitectVerification
{
    public const string Unverified = "UNVERIFIED";
    public const string Verified = "VERIFIED";
    public const string Rejected = "REJECTED";
}

public sealed record ArchitectProfileView(Guid RecipientId, Guid UserId, string DisplayName, string? RegistrationNumber, string VerificationStatus,
    string? VerificationReference, bool IsListed, bool AcceptingRequests, long RowVersion);

/// <summary>Customer-visible directory entry (no contact details are disclosed).</summary>
public sealed record DirectoryEntry(Guid RecipientId, string DisplayName, string VerificationStatus);

public sealed record ArchitectRequestView(Guid RequestId, Guid ProjectId, string RequestStatus, Guid? ReportId, DateTimeOffset RequestedAtUtc,
    string? Decision, string? DecisionNote, DateTimeOffset? RespondedAtUtc);

public sealed record FeedbackView(Guid FeedbackId, Guid RequestId, Guid AuthorId, string Kind, string Body, DateTimeOffset CreatedAtUtc);

public sealed record NotificationView(Guid Id, string Kind, string Title, string Body, string? Link, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc);

/// <summary>
/// Persistent architect directory and portal (Captain-local; G09 portal scope unresolved, so deny-by-default):
/// an architect sees only requests addressed to their own directory entry, only while the request is not withdrawn,
/// and only the exact report the customer consented to share. Verification is an admin assertion with a reference;
/// nothing here certifies professional registration.
/// </summary>
public interface IArchitectPortalStore
{
    Task<IReadOnlyList<ArchitectProfileView>> ListProfilesAsync(CancellationToken token);
    Task<AdminResult<ArchitectProfileView>> CreateProfileAsync(Guid admin, Guid userId, string displayName, string? registrationNumber, CancellationToken token);
    Task<AdminResult<ArchitectProfileView>> SetVerificationAsync(Guid admin, Guid recipientId, string status, string? reference, long expectedRowVersion,
        CancellationToken token);
    Task<AdminResult<ArchitectProfileView>> SetListingAsync(Guid admin, Guid recipientId, bool listed, bool accepting, long expectedRowVersion,
        CancellationToken token);

    Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(CancellationToken token);

    Task<ArchitectProfileView?> ProfileForUserAsync(Guid userId, CancellationToken token);
    Task<IReadOnlyList<ArchitectRequestView>> RequestsForAsync(Guid recipientId, CancellationToken token);
    Task<ArchitectRequestView?> RequestForAsync(Guid recipientId, Guid requestId, CancellationToken token);
    Task<byte[]?> ReportForAsync(Guid architectUserId, Guid recipientId, Guid requestId, CancellationToken token);
    Task<AdminResult<ArchitectRequestView>> RespondAsync(Guid architectUserId, Guid recipientId, Guid requestId, string decision, string? note,
        CancellationToken token);
    Task<AdminResult<FeedbackView>> AddFeedbackAsync(Guid architectUserId, Guid recipientId, Guid requestId, string kind, string body,
        CancellationToken token);

    /// <summary>Customer view (the caller must have established project ownership).</summary>
    Task<(ArchitectRequestView? Request, IReadOnlyList<FeedbackView> Feedback)> ConversationAsync(Guid projectId, Guid requestId, CancellationToken token);
}

public interface INotificationStore
{
    Task<PagedResult<NotificationView>> ListAsync(Guid userId, PageRequest page, bool unreadOnly, CancellationToken token);
    Task<long> UnreadCountAsync(Guid userId, CancellationToken token);
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken token);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken token);
    Task<bool> GetEmailEnabledAsync(Guid userId, CancellationToken token);
    Task SetEmailEnabledAsync(Guid userId, bool enabled, CancellationToken token);
}

/// <summary>Durable outbox dispatch (bounded retries, dead-lettering). Returns counts only.</summary>
public interface IOutboxDispatcher
{
    Task<(int Sent, int Failed, int Dead)> DispatchDueAsync(int batchSize, CancellationToken token);
}
