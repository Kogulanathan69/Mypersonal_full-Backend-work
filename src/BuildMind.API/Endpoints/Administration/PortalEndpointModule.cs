using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Administration;
using BuildMind.Application.Features.Planning;
using A = BuildMind.API.Endpoints.Administration.AdministrationEndpointModule;
using P = BuildMind.API.Endpoints.Planning.PlanningEndpointModule;

namespace BuildMind.API.Endpoints.Administration;

/// <summary>
/// Architect directory (admin-managed, persistent), the architect portal (scoped to requests addressed to the caller's
/// own directory entry, consent-bound report access), the customer view of responses/feedback, and in-app notifications.
/// Captain profile, Development only; portal scope is unratified (G09) and deny-by-default.
/// </summary>
public sealed class PortalEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);

        var admin = apiV1.MapGroup("/admin/architects").WithTags("Admin").RequireAuthorization();
        admin.MapGet("", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s, CancellationToken t) =>
            A.AsAdmin(c, u, d, t, async _ => Results.Ok(new { architects = await s.ListProfilesAsync(t) })));
        admin.MapPost("", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s, CancellationToken t) =>
            A.AsAdmin(c, u, d, t, async p =>
            {
                var body = await P.ReadAsync<CreateArchitectRequest>(c, t);
                if (body.Failure is not null) return body.Failure;
                if (body.Value!.UserId is not { } userId) return P.Invalid("userId");
                return A.Map(await s.CreateProfileAsync(p.UserId, userId, body.Value.DisplayName ?? "", body.Value.RegistrationNumber, t), x => x, created: true);
            }));
        admin.MapPost("/{recipientId}/verification", (string recipientId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => A.AsAdmin(c, u, d, t, async p =>
            {
                if (!P.TryId(recipientId, out var id)) return P.Invalid("recipientId");
                var body = await P.ReadAsync<VerificationRequest>(c, t);
                if (body.Failure is not null) return body.Failure;
                if (body.Value!.ExpectedRowVersion is not { } v) return P.Invalid("expectedRowVersion");
                return A.Map(await s.SetVerificationAsync(p.UserId, id, body.Value.Status ?? "", body.Value.Reference, v, t), x => x);
            }));
        admin.MapPut("/{recipientId}/listing", (string recipientId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => A.AsAdmin(c, u, d, t, async p =>
            {
                if (!P.TryId(recipientId, out var id)) return P.Invalid("recipientId");
                var body = await P.ReadAsync<ListingRequest>(c, t);
                if (body.Failure is not null) return body.Failure;
                var r = body.Value!;
                if (r.IsListed is not { } listed || r.AcceptingRequests is not { } accepting || r.ExpectedRowVersion is not { } v) return P.Invalid("request");
                return A.Map(await s.SetListingAsync(p.UserId, id, listed, accepting, v, t), x => x);
            }));

        apiV1.MapGet("/architects", async (HttpContext c, IArchitectPortalStore s, CancellationToken t) =>
        {
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { architects = await s.DirectoryAsync(t),
                notice = "Verification status reflects an administrator check of the cited evidence; BuildMind does not certify professional registration." });
        }).WithTags("Architect directory").RequireAuthorization();

        var portal = apiV1.MapGroup("/architect").WithTags("Architect portal").RequireAuthorization();
        portal.MapGet("/dashboard", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s, CancellationToken t) =>
            AsArchitect(c, u, d, s, t, async (_, profile) =>
            {
                var requests = await s.RequestsForAsync(profile.RecipientId, t);
                return Results.Ok(new { profile, pending = requests.Count(r => r.Decision is null), accepted = requests.Count(r => r.Decision == "ACCEPTED") });
            }));
        portal.MapGet("/requests", (HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s, CancellationToken t) =>
            AsArchitect(c, u, d, s, t, async (_, profile) => Results.Ok(new { requests = await s.RequestsForAsync(profile.RecipientId, t) })));
        portal.MapGet("/requests/{requestId}", (string requestId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => AsArchitect(c, u, d, s, t, async (_, profile) =>
                !P.TryId(requestId, out var id) ? P.Invalid("requestId")
                : await s.RequestForAsync(profile.RecipientId, id, t) is { } request ? Results.Ok(request) : A.Problem(404, "NOT_FOUND")));
        portal.MapGet("/requests/{requestId}/report", (string requestId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => AsArchitect(c, u, d, s, t, async (p, profile) =>
            {
                if (!P.TryId(requestId, out var id)) return P.Invalid("requestId");
                var bytes = await s.ReportForAsync(p.UserId, profile.RecipientId, id, t);
                return bytes is null ? A.Problem(404, "NOT_FOUND")
                    : Results.File(bytes, "application/pdf", "buildmind-preliminary-report.pdf");
            }));
        portal.MapPost("/requests/{requestId}/respond", (string requestId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => AsArchitect(c, u, d, s, t, async (p, profile) =>
            {
                if (!P.TryId(requestId, out var id)) return P.Invalid("requestId");
                var body = await P.ReadAsync<RespondRequest>(c, t);
                if (body.Failure is not null) return body.Failure;
                return A.Map(await s.RespondAsync(p.UserId, profile.RecipientId, id, body.Value!.Decision ?? "", body.Value.Note, t), x => x);
            }));
        portal.MapPost("/requests/{requestId}/complete", (string requestId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => AsArchitect(c, u, d, s, t, async (p, profile) =>
                !P.TryId(requestId, out var id) ? P.Invalid("requestId")
                : A.Map(await s.CompleteAsync(p.UserId, profile.RecipientId, id, t), x => x)))
            .WithName("CompleteArchitectRequest");
        portal.MapPost("/requests/{requestId}/feedback", (string requestId, HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s,
            CancellationToken t) => AsArchitect(c, u, d, s, t, async (p, profile) =>
            {
                if (!P.TryId(requestId, out var id)) return P.Invalid("requestId");
                var body = await P.ReadAsync<FeedbackRequest>(c, t);
                if (body.Failure is not null) return body.Failure;
                return A.Map(await s.AddFeedbackAsync(p.UserId, profile.RecipientId, id, body.Value!.Kind ?? "", body.Value.Body ?? "", t), x => x,
                    created: true);
            }));

        apiV1.MapGet("/projects/{projectId}/architect-requests/{requestId}/conversation", async (string projectId, string requestId, HttpContext c,
            PlanningAccess access, IArchitectPortalStore s, CancellationToken t) =>
        {
            c.Response.Headers.CacheControl = "no-store";
            if (!P.TryId(projectId, out var pid) || !P.TryId(requestId, out var rid)) return P.Invalid("id");
            var (status, _, _) = await access.AuthorizeAsync(pid, t);
            if (status != PlanningStatus.Success) return P.Map(new PlanningResult<object>(status), o => o);
            var (request, feedback) = await s.ConversationAsync(pid, rid, t);
            return request is null ? A.Problem(404, "NOT_FOUND") : Results.Ok(new { request, feedback });
        }).WithTags("Planning").RequireAuthorization();

        var notifications = apiV1.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();
        notifications.MapGet("", async (HttpContext c, ICurrentUser u, INotificationStore s, int? page, int? pageSize, bool? unreadOnly, CancellationToken t) =>
        {
            c.Response.Headers.CacheControl = "no-store";
            if (u.UserId is not { } id) return A.Problem(401, "UNAUTHENTICATED");
            var result = await s.ListAsync(id, PageRequest.Normalize(page, pageSize, null, null, null), unreadOnly ?? false, t);
            return Results.Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
        });
        notifications.MapGet("/unread-count", async (HttpContext c, ICurrentUser u, INotificationStore s, CancellationToken t) =>
            u.UserId is { } id ? Results.Ok(new { unread = await s.UnreadCountAsync(id, t) }) : A.Problem(401, "UNAUTHENTICATED"));
        notifications.MapPost("/{notificationId}/read", async (string notificationId, ICurrentUser u, INotificationStore s, CancellationToken t) =>
            u.UserId is not { } id ? A.Problem(401, "UNAUTHENTICATED")
            : !P.TryId(notificationId, out var nid) ? P.Invalid("notificationId")
            : await s.MarkReadAsync(id, nid, t) ? Results.NoContent() : A.Problem(404, "NOT_FOUND"));
        notifications.MapPost("/read-all", async (ICurrentUser u, INotificationStore s, CancellationToken t) =>
            u.UserId is { } id ? Results.Ok(new { marked = await s.MarkAllReadAsync(id, t) }) : A.Problem(401, "UNAUTHENTICATED"));

        var preferences = apiV1.MapGroup("/users/me/notification-preferences").WithTags("Notifications").RequireAuthorization();
        preferences.MapGet("", async (ICurrentUser u, INotificationStore s, CancellationToken t) =>
            u.UserId is { } id ? Results.Ok(new { emailEnabled = await s.GetEmailEnabledAsync(id, t) }) : A.Problem(401, "UNAUTHENTICATED"));
        preferences.MapPut("", async (HttpContext c, ICurrentUser u, INotificationStore s, CancellationToken t) =>
        {
            if (u.UserId is not { } id) return A.Problem(401, "UNAUTHENTICATED");
            var body = await P.ReadAsync<PreferencesRequest>(c, t);
            if (body.Failure is not null) return body.Failure;
            if (body.Value!.EmailEnabled is not { } enabled) return P.Invalid("emailEnabled");
            await s.SetEmailEnabledAsync(id, enabled, t);
            return Results.Ok(new { emailEnabled = enabled });
        });
    }

    private static async Task<IResult> AsArchitect(HttpContext c, ICurrentUser u, IPrincipalDirectory d, IArchitectPortalStore s, CancellationToken t,
        Func<PrincipalContext, ArchitectProfileView, Task<IResult>> action)
    {
        c.Response.Headers.CacheControl = "no-store";
        if (u.UserId is not { } id) return A.Problem(401, "UNAUTHENTICATED");
        var principal = await d.GetAsync(id, t);
        if (principal is not { IsArchitect: true }) return A.Denied();
        var profile = await s.ProfileForUserAsync(id, t);
        return profile is null ? A.Problem(403, "ARCHITECT_PROFILE_MISSING") : await action(principal, profile);
    }
}

public sealed record CreateArchitectRequest(Guid? UserId, string? DisplayName, string? RegistrationNumber);
public sealed record VerificationRequest(string? Status, string? Reference, long? ExpectedRowVersion);
public sealed record ListingRequest(bool? IsListed, bool? AcceptingRequests, long? ExpectedRowVersion);
public sealed record RespondRequest(string? Decision, string? Note);
public sealed record FeedbackRequest(string? Kind, string? Body);
public sealed record PreferencesRequest(bool? EmailEnabled);
