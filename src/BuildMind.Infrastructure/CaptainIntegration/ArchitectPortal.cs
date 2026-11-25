using BuildMind.Application.Features.Administration;
using BuildMind.Application.Features.Reports.Services;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// In-app notification + durable email outbox entry, written inside the caller's transaction (transactional outbox).
/// Bodies never contain secrets, tokens or document content. The dedupe key makes enqueueing idempotent.
/// </summary>
internal static class Notify
{
    public static async Task AddAsync(BuildMindDbContext context, Guid userId, string kind, string title, string body, string? link,
        string dedupeKey, DateTimeOffset now, CancellationToken token)
    {
        if (await context.Set<NotificationRecord>().AnyAsync(n => n.UserId == userId && n.Kind == kind && n.Link == link
                && n.Title == title && n.CreatedAtUtc == now, token).ConfigureAwait(false))
            return;
        context.Add(new NotificationRecord { Id = Guid.NewGuid(), UserId = userId, Kind = kind, Title = title, Body = body, Link = link, CreatedAtUtc = now });
        var emailEnabled = await context.Set<NotificationPreferenceRecord>().Where(p => p.UserId == userId).Select(p => (bool?)p.EmailEnabled)
            .SingleOrDefaultAsync(token).ConfigureAwait(false) ?? true;
        if (emailEnabled && !await context.Set<OutboxMessageRecord>().AnyAsync(o => o.DedupeKey == dedupeKey, token).ConfigureAwait(false))
            context.Add(new OutboxMessageRecord
            {
                Id = Guid.NewGuid(), Kind = "EMAIL", RecipientUserId = userId, Subject = "BuildMind: " + title,
                Body = body + "\n\nSign in to BuildMind to see the details.", Status = "PENDING", Attempts = 0, NextAttemptAtUtc = now,
                CreatedAtUtc = now, DedupeKey = dedupeKey,
            });
        await context.SaveChangesAsync(token).ConfigureAwait(false);
    }
}

/// <summary>At-least-once outbox delivery: PENDING → SENDING (CAS) → SENT; failures back off; 5 attempts → DEAD.</summary>
internal sealed class MySqlOutboxDispatcher(BuildMindDbContext context, ISystemEmailSender email, TimeProvider time) : IOutboxDispatcher
{
    public const int MaxAttempts = 5;

    public async Task<(int Sent, int Failed, int Dead)> DispatchDueAsync(int batchSize, CancellationToken token)
    {
        var now = time.GetUtcNow();
        // Crash recovery: a message stuck in SENDING for 5 minutes is retried (at-least-once; dedupe prevents re-enqueue).
        await context.Set<OutboxMessageRecord>().Where(o => o.Status == "SENDING" && o.NextAttemptAtUtc < now.AddMinutes(-5))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "PENDING"), token).ConfigureAwait(false);
        if (!email.IsConfigured) return (0, 0, 0);
        var due = await context.Set<OutboxMessageRecord>().AsNoTracking().Where(o => o.Status == "PENDING" && o.NextAttemptAtUtc <= now)
            .OrderBy(o => o.NextAttemptAtUtc).Take(batchSize).Select(o => o.Id).ToListAsync(token).ConfigureAwait(false);
        int sent = 0, failed = 0, dead = 0;
        foreach (var id in due)
        {
            var claimed = await context.Set<OutboxMessageRecord>().Where(o => o.Id == id && o.Status == "PENDING")
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "SENDING").SetProperty(o => o.NextAttemptAtUtc, now), token).ConfigureAwait(false);
            if (claimed != 1) continue;
            var message = await context.Set<OutboxMessageRecord>().AsNoTracking().SingleAsync(o => o.Id == id, token).ConfigureAwait(false);
            var recipient = await context.Set<UserRecord>().AsNoTracking().Where(u => u.Id == message.RecipientUserId && u.IsActive)
                .Select(u => u.Email).SingleOrDefaultAsync(token).ConfigureAwait(false);
            var ok = recipient is not null && await email.SendAsync(recipient, message.Subject, message.Body, token).ConfigureAwait(false);
            if (ok)
            {
                sent++;
                await context.Set<OutboxMessageRecord>().Where(o => o.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "SENT").SetProperty(o => o.SentAtUtc, time.GetUtcNow())
                        .SetProperty(o => o.Attempts, o => o.Attempts + 1), token).ConfigureAwait(false);
                continue;
            }
            var attempts = message.Attempts + 1;
            var isDead = attempts >= MaxAttempts || recipient is null;
            if (isDead) dead++; else failed++;
            await context.Set<OutboxMessageRecord>().Where(o => o.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, isDead ? "DEAD" : "PENDING").SetProperty(o => o.Attempts, attempts)
                    .SetProperty(o => o.LastErrorCode, recipient is null ? "RECIPIENT_INACTIVE" : "DELIVERY_FAILED")
                    .SetProperty(o => o.NextAttemptAtUtc, now.AddSeconds(30 * Math.Pow(2, attempts))), token).ConfigureAwait(false);
        }
        return (sent, failed, dead);
    }
}

internal sealed class MySqlNotificationStore(BuildMindDbContext context, TimeProvider time) : INotificationStore
{
    public async Task<PagedResult<NotificationView>> ListAsync(Guid userId, PageRequest page, bool unreadOnly, CancellationToken token)
    {
        var query = context.Set<NotificationRecord>().AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => n.ReadAtUtc == null);
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.OrderByDescending(n => n.CreatedAtUtc).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(token).ConfigureAwait(false);
        return new PagedResult<NotificationView>(rows.Select(n => new NotificationView(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAtUtc, n.ReadAtUtc))
            .ToArray(), page.Page, page.PageSize, total);
    }

    public Task<long> UnreadCountAsync(Guid userId, CancellationToken token) =>
        context.Set<NotificationRecord>().LongCountAsync(n => n.UserId == userId && n.ReadAtUtc == null, token);

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken token) =>
        await context.Set<NotificationRecord>().Where(n => n.Id == notificationId && n.UserId == userId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, time.GetUtcNow()), token).ConfigureAwait(false) == 1;

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken token) =>
        context.Set<NotificationRecord>().Where(n => n.UserId == userId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, time.GetUtcNow()), token);

    public async Task<bool> GetEmailEnabledAsync(Guid userId, CancellationToken token) =>
        await context.Set<NotificationPreferenceRecord>().AsNoTracking().Where(p => p.UserId == userId).Select(p => (bool?)p.EmailEnabled)
            .SingleOrDefaultAsync(token).ConfigureAwait(false) ?? true;

    public async Task SetEmailEnabledAsync(Guid userId, bool enabled, CancellationToken token)
    {
        var row = await context.Set<NotificationPreferenceRecord>().SingleOrDefaultAsync(p => p.UserId == userId, token).ConfigureAwait(false);
        if (row is null) context.Add(new NotificationPreferenceRecord { UserId = userId, EmailEnabled = enabled, UpdatedAtUtc = time.GetUtcNow() });
        else
        {
            row.EmailEnabled = enabled;
            row.UpdatedAtUtc = time.GetUtcNow();
        }
        try { await context.SaveChangesAsync(token).ConfigureAwait(false); }
        finally { context.ChangeTracker.Clear(); }
    }
}

internal sealed class MySqlArchitectPortalStore(
    BuildMindDbContext context, TransactionRunner transactions, CaptainReportBoundary reports, PrivateReportArtifactStore artifacts,
    TimeProvider time) : IArchitectPortalStore
{
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static ArchitectProfileView View(ArchitectProfileRecord a) => new(a.RecipientId, a.UserId, a.DisplayName, a.RegistrationNumber,
        a.VerificationStatus, a.VerificationReference, a.IsListed, a.AcceptingRequests, a.RowVersion);

    private sealed class Abort(object result) : Exception
    {
        public object Result { get; } = result;
    }

    private async Task<AdminResult<T>> WriteAsync<T>(Func<CancellationToken, Task<AdminResult<T>>> work, CancellationToken token)
    {
        try
        {
            return await transactions.ExecuteAsync(async inner =>
            {
                var result = await work(inner).ConfigureAwait(false);
                if (result.Status != AdminStatus.Success) throw new Abort(result);
                return result;
            }, token).ConfigureAwait(false);
        }
        catch (Abort abort)
        {
            return (AdminResult<T>)abort.Result;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<ArchitectProfileView>> ListProfilesAsync(CancellationToken token) =>
        (await context.Set<ArchitectProfileRecord>().AsNoTracking().OrderBy(a => a.DisplayName).Take(500).ToListAsync(token).ConfigureAwait(false))
        .Select(View).ToArray();

    public Task<AdminResult<ArchitectProfileView>> CreateProfileAsync(Guid admin, Guid userId, string displayName, string? registrationNumber,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100 || registrationNumber is { Length: > 64 })
            return Task.FromResult(new AdminResult<ArchitectProfileView>(AdminStatus.ValidationFailed, null, "PROFILE_INVALID"));
        return WriteAsync(async inner =>
        {
            var principal = await new MySqlPrincipalDirectory(context).GetAsync(userId, inner).ConfigureAwait(false);
            if (principal is not { IsArchitect: true }) return new AdminResult<ArchitectProfileView>(AdminStatus.ValidationFailed, null, "USER_NOT_ARCHITECT");
            if (await context.Set<ArchitectProfileRecord>().AnyAsync(a => a.UserId == userId, inner).ConfigureAwait(false))
                return new(AdminStatus.Conflict, null, "PROFILE_EXISTS");
            var now = Now();
            var record = new ArchitectProfileRecord
            {
                RecipientId = Guid.NewGuid(), UserId = userId, DisplayName = displayName.Trim(), RegistrationNumber = registrationNumber?.Trim(),
                VerificationStatus = ArchitectVerification.Unverified, IsListed = false, AcceptingRequests = false, RowVersion = 1, CreatedAtUtc = now,
            };
            context.Add(record);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            await AdminAudit.AppendAsync(context, admin, "ARCHITECT_PROFILE_CREATED", "ARCHITECT", record.RecipientId.ToString("D"), null,
                new { userId }, now, inner).ConfigureAwait(false);
            return AdminResult<ArchitectProfileView>.Ok(View(record));
        }, token);
    }

    private Task<AdminResult<ArchitectProfileView>> UpdateProfileAsync(Guid admin, Guid recipientId, long expectedRowVersion, string action,
        Func<ArchitectProfileRecord, string?> apply, object details, CancellationToken token) =>
        WriteAsync(async inner =>
        {
            var profile = await context.Set<ArchitectProfileRecord>().SingleOrDefaultAsync(a => a.RecipientId == recipientId, inner).ConfigureAwait(false);
            if (profile is null) return new AdminResult<ArchitectProfileView>(AdminStatus.NotFound, null, "NOT_FOUND");
            if (profile.RowVersion != expectedRowVersion) return new(AdminStatus.Conflict, null, "STALE_PROFILE_VERSION");
            if (apply(profile) is { } code) return new(AdminStatus.ValidationFailed, null, code);
            profile.RowVersion++;
            try { await context.SaveChangesAsync(inner).ConfigureAwait(false); }
            catch (DbUpdateConcurrencyException) { return new(AdminStatus.Conflict, null, "STALE_PROFILE_VERSION"); }
            await AdminAudit.AppendAsync(context, admin, action, "ARCHITECT", recipientId.ToString("D"), null, details, Now(), inner).ConfigureAwait(false);
            return AdminResult<ArchitectProfileView>.Ok(View(profile));
        }, token);

    public Task<AdminResult<ArchitectProfileView>> SetVerificationAsync(Guid admin, Guid recipientId, string status, string? reference,
        long expectedRowVersion, CancellationToken token) =>
        UpdateProfileAsync(admin, recipientId, expectedRowVersion, "ARCHITECT_VERIFICATION_SET", profile =>
        {
            if (status is not (ArchitectVerification.Verified or ArchitectVerification.Rejected or ArchitectVerification.Unverified))
                return "VERIFICATION_STATUS_INVALID";
            // Verification is an admin assertion that MUST cite the authoritative evidence it relied on.
            if (status == ArchitectVerification.Verified && (string.IsNullOrWhiteSpace(reference) || reference.Length > 200))
                return "VERIFICATION_REFERENCE_REQUIRED";
            profile.VerificationStatus = status;
            profile.VerificationReference = reference?.Trim();
            profile.VerifiedBy = status == ArchitectVerification.Verified ? admin : null;
            profile.VerifiedAtUtc = status == ArchitectVerification.Verified ? Now() : null;
            if (status != ArchitectVerification.Verified) profile.IsListed = false;
            return null;
        }, new { status }, token);

    public Task<AdminResult<ArchitectProfileView>> SetListingAsync(Guid admin, Guid recipientId, bool listed, bool accepting, long expectedRowVersion,
        CancellationToken token) =>
        UpdateProfileAsync(admin, recipientId, expectedRowVersion, "ARCHITECT_LISTING_SET", profile =>
        {
            if (listed && profile.VerificationStatus != ArchitectVerification.Verified) return "ARCHITECT_NOT_VERIFIED";
            profile.IsListed = listed;
            profile.AcceptingRequests = accepting;
            return null;
        }, new { listed, accepting }, token);

    public async Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(CancellationToken token) =>
        await (from a in context.Set<ArchitectProfileRecord>().AsNoTracking()
               join u in context.Set<UserRecord>().AsNoTracking() on a.UserId equals u.Id
               where a.IsListed && a.AcceptingRequests && a.VerificationStatus == ArchitectVerification.Verified && u.IsActive
               orderby a.DisplayName
               select new DirectoryEntry(a.RecipientId, a.DisplayName, a.VerificationStatus)).Take(500).ToListAsync(token).ConfigureAwait(false);

    /// <summary>Eligible recipient: listed, accepting, verified, active account (deny by default).</summary>
    internal static Task<bool> IsEligibleAsync(BuildMindDbContext context, Guid recipientId, CancellationToken token) =>
        (from a in context.Set<ArchitectProfileRecord>()
         join u in context.Set<UserRecord>() on a.UserId equals u.Id
         where a.RecipientId == recipientId && a.IsListed && a.AcceptingRequests && a.VerificationStatus == ArchitectVerification.Verified && u.IsActive
         select a).AnyAsync(token);

    public async Task<ArchitectProfileView?> ProfileForUserAsync(Guid userId, CancellationToken token) =>
        await context.Set<ArchitectProfileRecord>().AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, token).ConfigureAwait(false)
            is { } a ? View(a) : null;

    /// <summary>Requests addressed to this recipient that are not withdrawn. Filters are applied before projection (EF).</summary>
    private async Task<List<ArchitectRequestView>> RequestsAsync(Guid recipientId, Guid? requestId, CancellationToken token)
    {
        var requests = context.Set<ArchitectRequestRecord>().AsNoTracking()
            .Where(r => r.RecipientId == recipientId && r.Status == nameof(ArchitectRequestStatus.Pending));
        if (requestId is { } id) requests = requests.Where(r => r.RequestId == id);
        var rows = await requests.OrderByDescending(r => r.RequestedAtUtc).Take(200)
            .Select(r => new { r.RequestId, r.ProjectId, r.Status, r.ReportId, r.RequestedAtUtc }).ToListAsync(token).ConfigureAwait(false);
        var ids = rows.Select(r => r.RequestId).ToArray();
        var responses = await context.Set<ArchitectResponseRecord>().AsNoTracking().Where(x => ids.Contains(x.RequestId))
            .ToDictionaryAsync(x => x.RequestId, token).ConfigureAwait(false);
        return rows.Select(r => new ArchitectRequestView(r.RequestId, r.ProjectId, r.Status, r.ReportId, r.RequestedAtUtc,
            responses.GetValueOrDefault(r.RequestId)?.Decision, responses.GetValueOrDefault(r.RequestId)?.Note,
            responses.GetValueOrDefault(r.RequestId)?.RespondedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<ArchitectRequestView>> RequestsForAsync(Guid recipientId, CancellationToken token) =>
        await RequestsAsync(recipientId, null, token).ConfigureAwait(false);

    public async Task<ArchitectRequestView?> RequestForAsync(Guid recipientId, Guid requestId, CancellationToken token) =>
        (await RequestsAsync(recipientId, requestId, token).ConfigureAwait(false)).SingleOrDefault();

    public async Task<byte[]?> ReportForAsync(Guid architectUserId, Guid recipientId, Guid requestId, CancellationToken token)
    {
        var request = await RequestForAsync(recipientId, requestId, token).ConfigureAwait(false);
        if (request?.ReportId is not { } reportId) return null; // withdrawn, foreign or nothing shared
        var report = await reports.LoadAsync(request.ProjectId, reportId, token).ConfigureAwait(false);
        if (report is not { Status: ReportStatus.Ready, Artifact: { } artifact }) return null;
        var bytes = await artifacts.ReadVerifiedAsync(artifact, ReportPrivatePdfReadVerifier.AbsoluteMaximumBytes, token).ConfigureAwait(false);
        if (bytes is null) return null;
        // Consent-bound access is audited before the document is released.
        await transactions.ExecuteAsync(async inner =>
        {
            await AdminAudit.AppendAsync(context, architectUserId, "ARCHITECT_REPORT_ACCESS", "ARCHITECT_REQUEST", requestId.ToString("D"),
                "consented report review", new { reportId }, Now(), inner).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        return bytes;
    }

    public Task<AdminResult<ArchitectRequestView>> RespondAsync(Guid architectUserId, Guid recipientId, Guid requestId, string decision, string? note,
        CancellationToken token)
    {
        if (decision is not ("ACCEPTED" or "DECLINED") || note is { Length: > 2000 })
            return Task.FromResult(new AdminResult<ArchitectRequestView>(AdminStatus.ValidationFailed, null, "RESPONSE_INVALID"));
        return WriteAsync(async inner =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT request_id FROM architect_requests WHERE request_id = {requestId} FOR UPDATE", inner).ConfigureAwait(false);
            var request = await RequestForAsync(recipientId, requestId, inner).ConfigureAwait(false);
            if (request is null) return new AdminResult<ArchitectRequestView>(AdminStatus.NotFound, null, "NOT_FOUND");
            if (request.Decision is not null) return new(AdminStatus.Conflict, null, "ALREADY_RESPONDED");
            var now = Now();
            context.Add(new ArchitectResponseRecord
            {
                RequestId = requestId, RecipientId = recipientId, Decision = decision, Note = note?.Trim(), RespondedBy = architectUserId, RespondedAtUtc = now,
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            var owner = await context.Set<ArchitectRequestRecord>().AsNoTracking().Where(r => r.RequestId == requestId).Select(r => r.GrantedBy)
                .SingleAsync(inner).ConfigureAwait(false);
            await Notify.AddAsync(context, owner, "ARCHITECT_RESPONSE", decision == "ACCEPTED" ? "An architect accepted your review request"
                    : "An architect declined your review request", "Your architect review request has a response.",
                $"/projects/{request.ProjectId}/architect-requests/{requestId}", $"architect-response:{requestId}", now, inner).ConfigureAwait(false);
            return AdminResult<ArchitectRequestView>.Ok(request with { Decision = decision, DecisionNote = note?.Trim(), RespondedAtUtc = now });
        }, token);
    }

    public Task<AdminResult<FeedbackView>> AddFeedbackAsync(Guid architectUserId, Guid recipientId, Guid requestId, string kind, string body,
        CancellationToken token)
    {
        if (kind is not ("FEEDBACK" or "REVISION_REQUEST") || string.IsNullOrWhiteSpace(body) || body.Length > 4000)
            return Task.FromResult(new AdminResult<FeedbackView>(AdminStatus.ValidationFailed, null, "FEEDBACK_INVALID"));
        return WriteAsync(async inner =>
        {
            var request = await RequestForAsync(recipientId, requestId, inner).ConfigureAwait(false);
            if (request is null) return new AdminResult<FeedbackView>(AdminStatus.NotFound, null, "NOT_FOUND");
            if (request.Decision != "ACCEPTED") return new(AdminStatus.Conflict, null, "REQUEST_NOT_ACCEPTED");
            var now = Now();
            var record = new ArchitectFeedbackRecord { Id = Guid.NewGuid(), RequestId = requestId, AuthorId = architectUserId, Kind = kind, Body = body.Trim(), CreatedAtUtc = now };
            context.Add(record);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            var owner = await context.Set<ArchitectRequestRecord>().AsNoTracking().Where(r => r.RequestId == requestId).Select(r => r.GrantedBy)
                .SingleAsync(inner).ConfigureAwait(false);
            await Notify.AddAsync(context, owner, "ARCHITECT_FEEDBACK", kind == "REVISION_REQUEST" ? "An architect requested revisions" : "New architect feedback",
                "There is new architect feedback on your review request.", $"/projects/{request.ProjectId}/architect-requests/{requestId}",
                $"architect-feedback:{record.Id}", now, inner).ConfigureAwait(false);
            return AdminResult<FeedbackView>.Ok(new FeedbackView(record.Id, requestId, architectUserId, kind, record.Body, now));
        }, token);
    }

    public async Task<(ArchitectRequestView? Request, IReadOnlyList<FeedbackView> Feedback)> ConversationAsync(Guid projectId, Guid requestId,
        CancellationToken token)
    {
        var request = await (from r in context.Set<ArchitectRequestRecord>().AsNoTracking()
                             join resp in context.Set<ArchitectResponseRecord>().AsNoTracking() on r.RequestId equals resp.RequestId into responses
                             from resp in responses.DefaultIfEmpty()
                             where r.ProjectId == projectId && r.RequestId == requestId
                             select new ArchitectRequestView(r.RequestId, r.ProjectId, r.Status, r.ReportId, r.RequestedAtUtc, resp.Decision, resp.Note,
                                 (DateTimeOffset?)resp.RespondedAtUtc)).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (request is null) return (null, []);
        var feedback = await context.Set<ArchitectFeedbackRecord>().AsNoTracking().Where(f => f.RequestId == requestId).OrderBy(f => f.CreatedAtUtc)
            .Select(f => new FeedbackView(f.Id, f.RequestId, f.AuthorId, f.Kind, f.Body, f.CreatedAtUtc)).ToListAsync(token).ConfigureAwait(false);
        return (request, feedback);
    }
}
