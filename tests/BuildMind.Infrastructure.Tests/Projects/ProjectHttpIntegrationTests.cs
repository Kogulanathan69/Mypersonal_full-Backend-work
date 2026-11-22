using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Services;
using BuildMind.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Projects;

public sealed class ProjectHttpIntegrationTests
{
    private const string Route = "/api/v1/projects";
    private const string AuthHeader = "X-Project-Test-Subject";
    private static readonly Guid Owner = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOwner = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid MissingId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Theory]
    [InlineData("POST", false)]
    [InlineData("GET", false)]
    [InlineData("GET", true)]
    [InlineData("PATCH", true)]
    public async Task AnonymousRequest_ReturnsUnauthorized(string method, bool itemRoute)
    {
        var project = NewProject(Owner, "Owner project");
        await using var app = await StartHostAsync(project);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), itemRoute ? ItemRoute(project.Id) : Route);
        request.Content = method switch
        {
            "POST" => JsonContent.Create(new { name = "New project", projectType = "Residential" }),
            "PATCH" => JsonContent.Create(new { name = "Changed project", revision = project.Revision }),
            _ => null
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsCreatedProjectAndLocationForAuthenticatedOwner()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app, Owner);
        using var response = await client.PostAsJsonAsync(Route, new
        {
            name = "  New home  ",
            projectType = "  Residential  ",
            locationReference = "Synthetic plot",
            ownerId = OtherOwner
        });

        AssertStatusAndNoStore(response, HttpStatusCode.Created);
        var created = Assert.IsType<ProjectResponse>(await response.Content.ReadFromJsonAsync<ProjectResponse>());
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(ItemRoute(created.Id), response.Headers.Location?.OriginalString);
        Assert.Equal("New home", created.Name);
        Assert.Equal("Residential", created.ProjectType);
        Assert.Equal("Synthetic plot", created.LocationReference);
        Assert.Equal("Created", created.Status);
        Assert.Equal(1L, created.Revision);
        Assert.Equal(Now, created.CreatedAtUtc);
        Assert.Null(created.UpdatedAtUtc);

        using var read = await client.GetAsync(response.Headers.Location);
        AssertStatusAndNoStore(read, HttpStatusCode.OK);
        Assert.Equal(created.Id, (await read.Content.ReadFromJsonAsync<ProjectResponse>())?.Id);
        using var otherClient = CreateClient(app, OtherOwner);
        using var otherRead = await otherClient.GetAsync(response.Headers.Location);
        await AssertProblemAsync(otherRead, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_ReturnsOnlyProjectsBelongingToEachOwner()
    {
        var first = NewProject(Owner, "First owner project");
        var second = NewProject(OtherOwner, "Second owner project");
        await using var app = await StartHostAsync(first, second);

        foreach (var project in new[] { first, second })
        {
            using var client = CreateClient(app, project.OwnerId);
            using var response = await client.GetAsync(Route);

            AssertStatusAndNoStore(response, HttpStatusCode.OK);
            var projects = await response.Content.ReadFromJsonAsync<ProjectResponse[]>();
            Assert.NotNull(projects);
            Assert.Equal(project.Id, Assert.Single(projects).Id);
        }
    }

    [Fact]
    public async Task Get_OtherOwnersProjectAndMissingProjectReturnSameNotFound()
    {
        var project = NewProject(OtherOwner, "Private project");
        await using var app = await StartHostAsync(project);
        using var client = CreateClient(app, Owner);
        using var foreign = await client.GetAsync(ItemRoute(project.Id));
        using var missing = await client.GetAsync(ItemRoute(MissingId));

        var foreignProblem = await AssertProblemAsync(foreign, HttpStatusCode.NotFound);
        var missingProblem = await AssertProblemAsync(missing, HttpStatusCode.NotFound);
        Assert.Equal(missingProblem.Detail, foreignProblem.Detail);
        Assert.DoesNotContain(project.Name, await foreign.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_OtherOwnersProjectReturnsNotFoundAndPreservesProject()
    {
        var project = NewProject(OtherOwner, "Private project");
        await using var app = await StartHostAsync(project);
        using var client = CreateClient(app, Owner);
        using var response = await client.PatchAsJsonAsync(
            ItemRoute(project.Id), new { name = "Foreign edit", revision = project.Revision });

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.DoesNotContain(project.Name, await response.Content.ReadAsStringAsync());
        using var ownerClient = CreateClient(app, OtherOwner);
        var current = await ReadProjectAsync(ownerClient, project.Id);
        Assert.Equal(project.Name, current.Name);
        Assert.Equal(project.Revision, current.Revision);
    }

    [Fact]
    public async Task Create_InvalidNameReturnsBadRequestAndCreatesNothing()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app, Owner);
        using var response = await client.PostAsJsonAsync(Route, new { name = " ", projectType = "Residential" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using var list = await client.GetAsync(Route);
        AssertStatusAndNoStore(list, HttpStatusCode.OK);
        Assert.Empty(Assert.IsType<ProjectResponse[]>(await list.Content.ReadFromJsonAsync<ProjectResponse[]>()));
    }

    [Fact]
    public async Task Update_InvalidRevisionReturnsBadRequestAndPreservesProject()
    {
        var project = NewProject(Owner, "Original project");
        await using var app = await StartHostAsync(project);
        using var client = CreateClient(app, Owner);
        using var response = await client.PatchAsJsonAsync(
            ItemRoute(project.Id), new { name = "Invalid edit", revision = 0 });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var current = await ReadProjectAsync(client, project.Id);
        Assert.Equal(project.Name, current.Name);
        Assert.Equal(project.Revision, current.Revision);
    }

    [Fact]
    public async Task Update_OwnerCanSaveButStaleRevisionReturnsConflictAndPreservesSavedProject()
    {
        var project = NewProject(Owner, "Original project");
        await using var app = await StartHostAsync(project);
        using var client = CreateClient(app, Owner);
        using var updated = await client.PatchAsJsonAsync(
            ItemRoute(project.Id), new { name = "Saved edit", revision = project.Revision });

        AssertStatusAndNoStore(updated, HttpStatusCode.OK);
        var saved = Assert.IsType<ProjectResponse>(await updated.Content.ReadFromJsonAsync<ProjectResponse>());
        Assert.Equal(project.Id, saved.Id);
        Assert.Equal("Saved edit", saved.Name);
        Assert.Equal(project.Revision + 1, saved.Revision);
        Assert.Equal(Now, saved.UpdatedAtUtc);

        using var stale = await client.PatchAsJsonAsync(
            ItemRoute(project.Id), new { name = "Stale edit", revision = project.Revision });
        await AssertProblemAsync(stale, HttpStatusCode.Conflict);

        var current = await ReadProjectAsync(client, project.Id);
        Assert.Equal(saved.Name, current.Name);
        Assert.Equal(saved.Revision, current.Revision);
    }

    [Fact]
    public async Task Update_ClientSuppliedStatusCannotChangeProjectStatus()
    {
        var project = NewProject(Owner, "Original project");
        await using var app = await StartHostAsync(project);
        using var client = CreateClient(app, Owner);
        using var response = await client.PatchAsJsonAsync(ItemRoute(project.Id), new
        {
            status = "Confirmed",
            revision = project.Revision
        });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var current = await ReadProjectAsync(client, project.Id);
        Assert.Equal(project.Status, current.Status);
        Assert.Equal(project.Revision, current.Revision);
    }

    [Fact]
    public async Task ConcurrentUpdates_WithSameRevisionPersistOnlyOneCompleteEdit()
    {
        var project = NewProject(Owner, "Original project");
        var repository = new InMemoryProjectRepository([project], synchronizeUpdates: true);
        await using var app = await StartHostAsync(repository);
        using var firstClient = CreateClient(app, Owner);
        using var secondClient = CreateClient(app, Owner);

        var firstRequest = firstClient.PatchAsJsonAsync(ItemRoute(project.Id), new
        {
            name = "First edit", projectType = "First type", locationReference = "First plot", revision = project.Revision
        });
        var secondRequest = secondClient.PatchAsJsonAsync(ItemRoute(project.Id), new
        {
            name = "Second edit", projectType = "Second type", locationReference = "Second plot", revision = project.Revision
        });
        await Task.WhenAll(firstRequest, secondRequest);
        using var first = await firstRequest;
        using var second = await secondRequest;

        var winner = Assert.Single(new[] { first, second }, response => response.StatusCode == HttpStatusCode.OK);
        var loser = ReferenceEquals(winner, first) ? second : first;
        AssertStatusAndNoStore(winner, HttpStatusCode.OK);
        await AssertProblemAsync(loser, HttpStatusCode.Conflict);

        var current = await ReadProjectAsync(firstClient, project.Id);
        var prefix = ReferenceEquals(winner, first) ? "First" : "Second";
        Assert.Equal($"{prefix} edit", current.Name);
        Assert.Equal($"{prefix} type", current.ProjectType);
        Assert.Equal($"{prefix} plot", current.LocationReference);
        Assert.Equal(project.Revision + 1, current.Revision);
        Assert.Equal(project.Status, current.Status);
    }

    private static string ItemRoute(Guid id) => $"{Route}/{id}";

    private static Project NewProject(Guid owner, string name) =>
        new() { OwnerId = owner, Name = name, ProjectType = "Residential", CreatedAtUtc = Now };

    private static async Task<ProjectResponse> ReadProjectAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(ItemRoute(id));
        AssertStatusAndNoStore(response, HttpStatusCode.OK);
        return Assert.IsType<ProjectResponse>(await response.Content.ReadFromJsonAsync<ProjectResponse>());
    }

    private static void AssertStatusAndNoStore(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static async Task<ProblemDetails> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        AssertStatusAndNoStore(response, status);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = Assert.IsType<ProblemDetails>(await response.Content.ReadFromJsonAsync<ProblemDetails>());
        Assert.Equal((int)status, problem.Status);
        return problem;
    }

    private static HttpClient CreateClient(WebApplication app, Guid owner)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(AuthHeader, owner.ToString());
        return client;
    }

    private static Task<WebApplication> StartHostAsync(params Project[] projects) =>
        StartHostAsync(new InMemoryProjectRepository(projects));

    private static async Task<WebApplication> StartHostAsync(IProjectRepository repository)
    {
        const string scheme = "ProjectTest";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        builder.Services.AddScoped<ProjectService>();
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider());
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<IApiEndpointModule, ProjectEndpointModule>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBuildMindApiV1();
        await app.StartAsync();
        return app;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers[AuthHeader].ToString(), out var subject))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    internal sealed class InMemoryProjectRepository(
        IEnumerable<Project> projects, bool synchronizeUpdates = false) : IProjectRepository
    {
        private readonly Dictionary<Guid, Project> _projects = projects.ToDictionary(project => project.Id);
        private readonly object _updateLock = new();
        private readonly TaskCompletionSource _bothUpdatesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _updateAttempts;

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_projects.GetValueOrDefault(id));

        public Task<Project?> GetByIdAndOwnerAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            var project = _projects.GetValueOrDefault(id);
            return Task.FromResult(project?.OwnerId == ownerId ? project : null);
        }

        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Project>>(_projects.Values.Where(project => project.OwnerId == ownerId).ToArray());

        public Task AddAsync(Project project, CancellationToken cancellationToken)
        {
            _projects.Add(project.Id, project);
            return Task.CompletedTask;
        }

        public async Task<bool> UpdateAsync(Project project, Guid ownerId, long expectedRevision, CancellationToken cancellationToken)
        {
            if (synchronizeUpdates)
            {
                // Wait until both requests have read the original revision and reached the write.
                if (Interlocked.Increment(ref _updateAttempts) == 2)
                    _bothUpdatesReady.SetResult();

                await _bothUpdatesReady.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            lock (_updateLock)
            {
                if (!_projects.TryGetValue(project.Id, out var current) ||
                    current.OwnerId != ownerId || current.Revision != expectedRevision)
                    return false;

                _projects[project.Id] = project;
                return true;
            }
        }
    }
}
