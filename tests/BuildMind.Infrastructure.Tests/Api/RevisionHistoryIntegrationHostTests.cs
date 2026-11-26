using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.API.Endpoints.PlanEditor;
using BuildMind.API.Errors;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// Database-free vertical integration of the M3 HTTP boundary and real application service.
/// Controlled ports prove composition and contract propagation only; they do not represent MySQL or prove
/// SQL predicates, locking, transaction isolation, index use, durability, or rollback behavior.
/// </summary>
public sealed class RevisionHistoryIntegrationHostTests
{
    private static readonly Guid ActorId = Guid.Parse("00000000-0000-4000-8000-000000000020");
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid PlanId = Guid.Parse("00000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task Authorized_owner_reads_list_and_exact_revision_through_real_service()
    {
        var revisions = History(2);
        var authorization = new ControlledAuthorizationPort(Authorized(7));
        var history = new ControlledRevisionHistoryPort
        {
            PageHandler = (_, _, _, pageSize, _) =>
            {
                Assert.Equal(2, pageSize);
                return Task.FromResult(Page(revisions, hasMore: false));
            },
            RevisionHandler = (_, _, revisionId, _) => Task.FromResult(
                new PlanRevisionHistorySourceRevision(
                    PlanRevisionHistorySourceStatus.Succeeded,
                    revisions.Single(revision => revision.RevisionId == revisionId))),
        };
        await using var app = await Host(authorization, history);
        using var client = AuthenticatedClient(app);

        using var list = await client.GetAsync(HistoryRoute + "?pageSize=2");
        var listBody = await list.Content.ReadAsStringAsync();
        using var exact = await client.GetAsync(RevisionRoute(revisions[0].RevisionId));
        var exactBody = await exact.Content.ReadAsStringAsync();
        using var candidate = await client.PostAsync(HistoryRoute[..^"versions".Length] + "candidates", null);
        Assert.Equal(HttpStatusCode.NotFound, candidate.StatusCode);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.DoesNotContain("\"geometry\":", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"geometry\":", exactBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, authorization.Calls);
        Assert.All(authorization.Actors, actor => Assert.Equal(ActorId, actor));
        Assert.All(authorization.Projects, project => Assert.Equal(ProjectId, project));
        Assert.Equal(1, history.PageCalls);
        Assert.Equal(1, history.RevisionCalls);
        Assert.Equal((ProjectId, PlanId, revisions[0].RevisionId), history.LastRevisionScope);
    }

    [Theory]
    [InlineData("cross-owner")]
    [InlineData("revoked")]
    [InlineData("version-changed")]
    public async Task Authorization_changes_fail_closed_without_history_payload(string mode)
    {
        var authorization = new ControlledAuthorizationPort(Authorized(7));
        switch (mode)
        {
            case "cross-owner":
                authorization.Results.Enqueue(new(ProjectAuthorizationStatus.Denied));
                break;
            case "revoked":
                authorization.Results.Enqueue(Authorized(7));
                authorization.Results.Enqueue(new(ProjectAuthorizationStatus.Denied));
                break;
            case "version-changed":
                authorization.Results.Enqueue(Authorized(7));
                authorization.Results.Enqueue(Authorized(8));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var history = new ControlledRevisionHistoryPort();
        await using var app = await Host(authorization, history);
        using var client = AuthenticatedClient(app);

        using var response = await client.GetAsync(HistoryRoute);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoProtectedPayload(body);
        Assert.Equal(mode == "cross-owner" ? 0 : 1, history.PageCalls);
    }

    [Fact]
    public async Task Continuation_stays_on_original_head_while_fresh_read_observes_new_head()
    {
        var revisions = History(4);
        var version4 = revisions[0];
        var version3 = revisions[1];
        var version2 = revisions[2];
        var authorization = new ControlledAuthorizationPort(Authorized(7));
        var freshReads = 0;
        var history = new ControlledRevisionHistoryPort
        {
            PageHandler = (_, _, cursor, pageSize, _) =>
            {
                Assert.Equal(1, pageSize);
                return Task.FromResult(historyPage(cursor));
            },
        };
        PlanRevisionHistorySourcePage historyPage(PlanRevisionHistoryCursor? cursor)
        {
            if (cursor is not null)
                return Page([version2], hasMore: true,
                    new(version3.VersionNumber, version3.RevisionId), cursorAnchorVerified: true);
            freshReads++;
            return freshReads == 1
                ? Page([version3], hasMore: true)
                : Page([version4], hasMore: true);
        }

        await using var app = await Host(authorization, history);
        using var client = AuthenticatedClient(app);

        using var first = await client.GetAsync(HistoryRoute + "?pageSize=1");
        var firstJson = JsonNode.Parse(await first.Content.ReadAsStringAsync())!;
        var cursor = firstJson["nextCursor"]!.GetValue<string>();
        using var continuation = await client.GetAsync(
            HistoryRoute + "?pageSize=1&cursor=" + Uri.EscapeDataString(cursor));
        var continuationJson = JsonNode.Parse(await continuation.Content.ReadAsStringAsync())!;
        using var fresh = await client.GetAsync(HistoryRoute + "?pageSize=1");
        var freshJson = JsonNode.Parse(await fresh.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, continuation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal(version3.RevisionId,
            firstJson["revisions"]![0]!["revisionId"]!.GetValue<Guid>());
        Assert.Equal(version2.RevisionId,
            continuationJson["revisions"]![0]!["revisionId"]!.GetValue<Guid>());
        Assert.Equal(version4.RevisionId,
            freshJson["revisions"]![0]!["revisionId"]!.GetValue<Guid>());
        Assert.Equal(version3.RevisionId, history.Cursors[1]!.Value.HeadRevisionId);
        Assert.Equal(version3.VersionNumber, history.Cursors[1]!.Value.HeadVersionNumber);
    }

    [Theory]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData("timeout", HttpStatusCode.GatewayTimeout)]
    public async Task Dependency_failures_map_safely_through_real_service(
        string failure,
        HttpStatusCode expected)
    {
        var authorization = new ControlledAuthorizationPort(Authorized(7));
        var history = new ControlledRevisionHistoryPort
        {
            PageHandler = (_, _, _, _, _) => Task.FromException<PlanRevisionHistorySourcePage>(
                failure == "timeout" ? new TimeoutException() : new HttpRequestException()),
        };
        await using var app = await Host(authorization, history);
        using var client = AuthenticatedClient(app);

        using var response = await client.GetAsync(HistoryRoute);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);
        AssertNoProtectedPayload(body);
        Assert.Equal(1, history.PageCalls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_through_HTTP_service_and_port()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorization = new ControlledAuthorizationPort(Authorized(7));
        var history = new ControlledRevisionHistoryPort
        {
            PageHandler = async (_, _, _, _, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
            },
        };
        await using var app = await Host(authorization, history);
        using var client = AuthenticatedClient(app);
        using var cancellation = new CancellationTokenSource();

        var request = client.GetAsync(HistoryRoute, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(1, history.PageCalls);
        Assert.Equal(1, authorization.Calls);
    }

    private static string HistoryRoute => $"/api/v1/projects/{ProjectId}/plans/{PlanId}/versions";

    private static string RevisionRoute(Guid revisionId) => HistoryRoute + $"/{revisionId}";

    private static ProjectAuthorizationResult Authorized(long version) =>
        new(ProjectAuthorizationStatus.Authorized,
            new ProjectAuthorizationSnapshot(ProjectId, ActorId, new AuthorizationVersion(version)));

    private static PlanRevisionHistorySourcePage Page(
        IReadOnlyList<PlanRevisionSnapshot> revisions,
        bool hasMore,
        PlanRevisionHistoryHead? head = null,
        bool cursorAnchorVerified = false) =>
        new(PlanRevisionHistorySourceStatus.Succeeded, revisions, hasMore,
            head ?? new PlanRevisionHistoryHead(revisions[0].VersionNumber, revisions[0].RevisionId),
            cursorAnchorVerified);

    private static IReadOnlyList<PlanRevisionSnapshot> History(int count)
    {
        var geometryJson = LoadGeometryFixture();
        var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(geometryJson, PlanEditorJson.StrictOptions)!;
        var geometryHash = PlanRevisionService.ComputeGeometryHash(geometryJson);
        var ascending = new List<PlanRevisionSnapshot>();
        Guid? parent = null;
        for (var version = 1; version <= count; version++)
        {
            var revisionId = Guid.Parse($"00000000-0000-4000-8000-{version + 100:D12}");
            ascending.Add(new(
                revisionId,
                PlanId,
                ProjectId,
                parent,
                version,
                GeometrySchemaInfo.DraftVersion,
                geometryJson,
                geometryHash,
                geometry.LandRevisionId,
                geometry.RequirementsRevisionId,
                Validation(geometryHash, version),
                Guid.Parse($"00000000-0000-4000-8001-{version:D12}"),
                ActorId,
                DateTimeOffset.Parse("2026-11-22T00:00:00Z").AddMinutes(version),
                $"Synthetic revision {version}"));
            parent = revisionId;
        }

        ascending.Reverse();
        return ascending;
    }

    private static PlanValidationReferenceDto Validation(string geometryHash, int version) => new()
    {
        ValidationId = Guid.Parse($"00000000-0000-4000-8002-{version:D12}"),
        ValidatorVersion = "synthetic-integration-host/1",
        GeometryHash = geometryHash,
        GeometryValid = true,
        RequirementsMatch = true,
        RegulatoryEvidenceStatus = "VERIFIED",
        HasBlockingIssues = false,
        ValidatedAtUtc = DateTimeOffset.Parse("2026-11-22T00:00:00Z").AddMinutes(version),
    };

    private static string LoadGeometryFixture()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BuildMind.sln")))
            directory = directory.Parent;
        if (directory is null)
            throw new InvalidOperationException("Could not locate the repository root for the shared geometry fixture.");

        return File.ReadAllText(Path.Combine(
            directory.FullName,
            "docs",
            "proposals",
            "m3-plan-editor",
            "PLAN-GEOMETRY-SAMPLE.json"));
    }

    private static void AssertNoProtectedPayload(string body)
    {
        Assert.DoesNotContain("geometryHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("landRevisionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requirementsRevisionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revisionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revisions", body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient AuthenticatedClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-M3-Integration-Auth", "yes");
        return client;
    }

    private static async Task<WebApplication> Host(
        ControlledAuthorizationPort authorization,
        ControlledRevisionHistoryPort history)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication("M3Integration")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("M3Integration", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Apply);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        builder.Services.AddSingleton<IProjectAuthorizationPort>(authorization);
        builder.Services.AddSingleton<IPlanRevisionHistoryQueryPort>(history);
        builder.Services.AddScoped<IPlanRevisionHistoryService, PlanRevisionHistoryService>();

        var app = builder.Build();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
        PlanEditorEndpointModule.MapRevisionHistoryEndpoints(app.MapGroup("/api/v1"));
        await app.StartAsync();
        return app;
    }

    private sealed class ControlledAuthorizationPort(ProjectAuthorizationResult defaultResult)
        : IProjectAuthorizationPort
    {
        public Queue<ProjectAuthorizationResult> Results { get; } = new();
        public List<Guid> Actors { get; } = [];
        public List<Guid> Projects { get; } = [];
        public int Calls => Actors.Count;

        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
            Guid projectId,
            Guid authenticatedActorId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Projects.Add(projectId);
            Actors.Add(authenticatedActorId);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : defaultResult);
        }
    }

    private sealed class ControlledRevisionHistoryPort : IPlanRevisionHistoryQueryPort
    {
        public Func<Guid, Guid, PlanRevisionHistoryCursor?, int, CancellationToken,
            Task<PlanRevisionHistorySourcePage>>? PageHandler { get; init; }
        public Func<Guid, Guid, Guid, CancellationToken,
            Task<PlanRevisionHistorySourceRevision>>? RevisionHandler { get; init; }
        public int PageCalls { get; private set; }
        public int RevisionCalls { get; private set; }
        public List<PlanRevisionHistoryCursor?> Cursors { get; } = [];
        public (Guid ProjectId, Guid PlanId, Guid RevisionId) LastRevisionScope { get; private set; }

        public Task<PlanRevisionHistorySourcePage> ReadPageAsync(
            Guid projectId,
            Guid planId,
            PlanRevisionHistoryCursor? cursor,
            int pageSize,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageCalls++;
            Cursors.Add(cursor);
            return PageHandler is null
                ? Task.FromResult(new PlanRevisionHistorySourcePage(
                    PlanRevisionHistorySourceStatus.Succeeded, [], false))
                : PageHandler(projectId, planId, cursor, pageSize, cancellationToken);
        }

        public Task<PlanRevisionHistorySourceRevision> ReadRevisionAsync(
            Guid projectId,
            Guid planId,
            Guid revisionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevisionCalls++;
            LastRevisionScope = (projectId, planId, revisionId);
            return RevisionHandler is null
                ? Task.FromResult(new PlanRevisionHistorySourceRevision(
                    PlanRevisionHistorySourceStatus.NotFound))
                : RevisionHandler(projectId, planId, revisionId, cancellationToken);
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["X-M3-Integration-Auth"] != "yes")
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
