using System.Net;
using BuildMind.API.Composition;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Infrastructure.Tests.CaptainIntegration;
using BuildMind.Infrastructure.Tests.Identity;
using BuildMind.Infrastructure.Tests.Support;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.API.Endpoints.Requirements;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildMind.Infrastructure.Tests;

public sealed class RequirementsEndpointTests
{
    private static readonly Guid ProjectId = Guid.Parse("60000000-0000-0000-0000-000000000006");
    private static readonly Guid RevisionId = Guid.Parse("50000000-0000-0000-0000-000000000005");
    private static string Route => $"/api/v1/projects/{ProjectId}/requirements";

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task UnauthenticatedRequest_IsRejectedBeforeService(string method)
    {
        var service = new StubService();
        await using var app = await Host(service);
        using var client = app.GetTestClient();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, service.Calls);
        await AssertProblem(response, 401, "UNAUTHENTICATED");
    }

    [Theory]
    [InlineData(RequirementsOperationStatus.Success, 200)]
    [InlineData(RequirementsOperationStatus.NoSavedRevision, 204)]
    [InlineData(RequirementsOperationStatus.Unauthenticated, 401)]
    [InlineData(RequirementsOperationStatus.NotFound, 404)]
    [InlineData(RequirementsOperationStatus.RevisionConflict, 409)]
    [InlineData(RequirementsOperationStatus.CatalogueConflict, 409)]
    [InlineData(RequirementsOperationStatus.ValidationFailed, 422)]
    [InlineData(RequirementsOperationStatus.DependencyUnavailable, 503)]
    [InlineData(RequirementsOperationStatus.DependencyTimeout, 504)]
    [InlineData(RequirementsOperationStatus.InvalidDependencyResponse, 500)]
    public async Task OutcomeMapping_UsesSafeSharedProblemDetails(RequirementsOperationStatus status, int http)
    {
        var service = new StubService { Result = Result(status) };
        await using var app = await Host(service);
        using var client = AuthenticatedClient(app);
        using var response = status is RequirementsOperationStatus.RevisionConflict
                or RequirementsOperationStatus.CatalogueConflict or RequirementsOperationStatus.ValidationFailed
            ? await client.PutAsJsonAsync(Route, Body()) : await client.GetAsync(Route);
        Assert.Equal(http, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(1, service.Calls);
        if (http >= 400) await AssertProblem(response, http);
        else if (http == 204) Assert.Empty(await response.Content.ReadAsStringAsync());
        else
        {
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.Equal(ProjectId, json["projectId"]!.GetValue<Guid>());
            Assert.Equal(RevisionId, json["revisionId"]!.GetValue<Guid>());
        }
    }

    [Fact]
    public async Task CompletePut_BindsExplicitNullAndEmptyValuesWithoutClientOwner()
    {
        var service = new StubService { Result = Result(RequirementsOperationStatus.Success) };
        await using var app = await Host(service);
        using var client = AuthenticatedClient(app);
        var body = Body(); body["ownerId"] = "untrusted-client-owner";
        using var response = await client.PutAsJsonAsync(Route, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ProjectId, service.Project);
        Assert.Null(service.Request!.ExpectedRevisionId);
        Assert.Empty(service.Request.AdditionalSpaces);
        Assert.Empty(service.Request.Relationships);
        Assert.Null(service.Request.DesignMethod);
        Assert.Equal("synthetic-policy", service.Request.CatalogueVersion);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("expectedRevisionId")]
    [InlineData("catalogueVersion")]
    [InlineData("floorCount")]
    [InlineData("groundFloorRooms")]
    [InlineData("additionalSpaces")]
    [InlineData("relationships")]
    [InlineData("designMethod")]
    public async Task EveryTopLevelField_MustBePresent(string field)
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app); var body = Body(); body.Remove(field);
        using var response = await client.PutAsJsonAsync(Route, body);
        await AssertProblem(response, 400);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("catalogueVersion")]
    [InlineData("floorCount")]
    [InlineData("groundFloorRooms")]
    [InlineData("additionalSpaces")]
    [InlineData("relationships")]
    public async Task RequiredNonNullableValues_CannotBeNull(string field)
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app); var body = Body(); body[field] = null;
        using var response = await client.PutAsJsonAsync(Route, body);
        await AssertProblem(response, 400); Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("{malformed")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"floorCount\":\"not-a-number\"}")]
    public async Task MalformedBody_IsSafe400(string body)
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app);
        using var response = await client.PutAsync(Route, new StringContent(body, Encoding.UTF8, "application/json"));
        await AssertProblem(response, 400); Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("roomType")]
    [InlineData("count")]
    public async Task MissingNestedRoomField_Is400(string field)
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app); var body = Body();
        body["groundFloorRooms"]![0]!.AsObject().Remove(field);
        using var response = await client.PutAsJsonAsync(Route, body);
        await AssertProblem(response, 400); Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task ExplicitOptionalSelections_AreMappedWithoutSilentPruning()
    {
        var service = new StubService { Result = Result(RequirementsOperationStatus.Success) }; await using var app = await Host(service);
        using var client = AuthenticatedClient(app); var body = Body();
        body["expectedRevisionId"] = RevisionId.ToString();
        body["additionalSpaces"] = JsonNode.Parse("[{\"option\":\"SYNTHETIC_SPACE\",\"otherText\":null}]");
        body["relationships"] = JsonNode.Parse("[{\"sourceRoomType\":\"ROOM_A\",\"targetRoomType\":\"ROOM_A\",\"kind\":\"SYNTHETIC_KIND\"}]");
        body["designMethod"] = "SYNTHETIC_METHOD";
        using var response = await client.PutAsJsonAsync(Route, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RevisionId, service.Request!.ExpectedRevisionId);
        Assert.Single(service.Request.AdditionalSpaces);
        Assert.Single(service.Request.Relationships);
        Assert.Equal("SYNTHETIC_METHOD", service.Request.DesignMethod);
    }

    [Fact]
    public async Task InvalidProjectId_Is400BeforeService()
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app);
        using var response = await client.GetAsync("/api/v1/projects/not-a-guid/requirements");
        await AssertProblem(response, 400); Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task UnsupportedMedia_Is415()
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app);
        using var response = await client.PutAsync(Route, new StringContent("synthetic", Encoding.UTF8, "text/plain"));
        await AssertProblem(response, 415); Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownTransactionOutcome_IsGeneric500WithoutDetails(bool rollbackUncertain)
    {
        var cause = new IOException("SYNTHETIC_PRIVATE_EXCEPTION");
        var service = new StubService { Failure = rollbackUncertain
            ? new BuildMind.Infrastructure.Persistence.Transactions.TransactionRollbackUncertainException(cause, new IOException("PRIVATE_ROLLBACK"))
            : new BuildMind.Infrastructure.Persistence.Transactions.TransactionOutcomeUnknownException(cause) };
        await using var app = await Host(service); using var client = AuthenticatedClient(app);
        using var response = await client.PutAsJsonAsync(Route, Body());
        await AssertProblem(response, 500, "INTERNAL_ERROR");
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_EXCEPTION", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("PRIVATE_ROLLBACK", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task AbsenceOutcomeOnPut_IsCorruptResponseNot204()
    {
        var service = new StubService(); await using var app = await Host(service);
        using var client = AuthenticatedClient(app);
        using var response = await client.PutAsJsonAsync(Route, Body());
        await AssertProblem(response, 500, "INTERNAL_ERROR");
    }

    [Theory]
    [InlineData(RequirementsOperationStatus.RevisionConflict)]
    [InlineData(RequirementsOperationStatus.CatalogueConflict)]
    [InlineData(RequirementsOperationStatus.ValidationFailed)]
    public async Task WriteOnlyOutcomeOnGet_IsInternalFailure(RequirementsOperationStatus status)
    {
        var service = new StubService { Result = Result(status) };
        await using var app = await Host(service); using var client = AuthenticatedClient(app);
        using var response = await client.GetAsync(Route);
        await AssertProblem(response, 500, "INTERNAL_ERROR");
    }

    [Fact]
    public async Task OtherProjectSuccessPayload_IsNotDisclosed()
    {
        var other = Guid.NewGuid();
        var dto = new RequirementRevisionDto(new RequirementRevision(RevisionId, other, 1, "synthetic-schema",
            "synthetic-policy", new RequirementContent(1, [new RoomCount("ROOM_A", 1)]), DateTimeOffset.UtcNow));
        var service = new StubService { Result = new(RequirementsOperationStatus.Success, dto) };
        await using var app = await Host(service); using var client = AuthenticatedClient(app);
        using var response = await client.GetAsync(Route);
        await AssertProblem(response, 500);
        Assert.DoesNotContain(other.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RealIsolatedService_ReadSaveReplaceConflictAndHistoryFlow()
    {
        var flow = new FlowDependencies(); await using var app = await Host(null, flow);
        using var client = AuthenticatedClient(app);
        var route = $"/api/v1/projects/{flow.Project.Id}/requirements";
        using var empty = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        var body = Body();
        body["additionalSpaces"] = JsonNode.Parse("[{\"option\":\"SYNTHETIC_SPACE\",\"otherText\":null}]");
        using var initial = await client.PutAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var first = JsonNode.Parse(await initial.Content.ReadAsStringAsync())!;
        var firstId = first["revisionId"]!.GetValue<Guid>();
        Assert.Equal(1L, first["revisionNumber"]!.GetValue<long>());
        using var read = await client.GetAsync(route);
        Assert.Equal(firstId, JsonNode.Parse(await read.Content.ReadAsStringAsync())!["revisionId"]!.GetValue<Guid>());
        body = Body(); body["expectedRevisionId"] = firstId.ToString();
        using var next = await client.PutAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(2L, JsonNode.Parse(await next.Content.ReadAsStringAsync())!["revisionNumber"]!.GetValue<long>());
        Assert.Single(flow.Store.History[firstId].Content.AdditionalSpaces);
        Assert.Empty(flow.Store.Current!.Content.AdditionalSpaces);
        using var stale = await client.PutAsJsonAsync(route, body);
        await AssertProblem(stale, 409, "REVISION_CONFLICT");
        body["expectedRevisionId"] = flow.Store.Current.Id.ToString();
        body["groundFloorRooms"] = JsonNode.Parse("[{\"roomType\":\"ROOM_A\",\"count\":1},{\"roomType\":\"ROOM_A\",\"count\":1}]");
        using var invalid = await client.PutAsJsonAsync(route, body);
        await AssertProblem(invalid, 422, "VALIDATION_FAILED");
        body = Body(); body["catalogueVersion"] = "synthetic-wrong-version";
        using var mismatch = await client.PutAsJsonAsync(route, body);
        await AssertProblem(mismatch, 409, "CONFLICT");
        Assert.Equal(2, flow.Store.History.Count);
        // Test authentication and an in-memory repository do not prove real JWT/MySQL isolation.
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task RealIsolatedService_OtherAccountAndMissingProjectAreUniform404(string method)
    {
        var flow = new FlowDependencies(); await using var app = await Host(null, flow);
        using var client = AuthenticatedClient(app);
        client.DefaultRequestHeaders.Add("X-Requirements-Test-Subject", "90000000-0000-0000-0000-000000000009");
        foreach (var id in new[] { flow.Project.Id, Guid.NewGuid() })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/projects/{id}/requirements");
            if (method == "PUT")
            {
                var body = Body(); body["ownerId"] = flow.Project.OwnerId.ToString();
                request.Content = JsonContent.Create(body);
            }
            using var response = await client.SendAsync(request);
            await AssertProblem(response, 404, "NOT_FOUND");
            Assert.DoesNotContain(flow.Project.Name, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, flow.Store.Reads);
        Assert.Empty(flow.Store.History);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Registration_RejectsNonTestingHosts(string environment)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddIsolatedRequirementsFeature(new TestEnvironment(environment)));
        Assert.Empty(services);
    }

    [Fact]
    public void Registration_OnlyAddsFeatureAndNeverInventsProductionDependencies()
    {
        var services = new ServiceCollection().AddIsolatedRequirementsFeature(new TestEnvironment("Testing"));
        Assert.Contains(services, item => item.ServiceType == typeof(IRequirementsApplicationService));
        Assert.Contains(services, item => item.ServiceType == typeof(IApiEndpointModule));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IRequirementCatalogueProvider));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IRequirementRevisionRepository));
    }

    [Fact]
    public async Task REQ_G14_Isolated_testing_read_works_without_versioned_port()
    {
        var flow = new FlowDependencies();
        await using var app = await Host(null, flow);
        using var scope = app.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IProjectAuthorizationPort>());
        using var client = AuthenticatedClient(app);
        using var response = await client.GetAsync($"/api/v1/projects/{flow.Project.Id}/requirements");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, flow.Store.Reads);
    }

    [Theory]
    [InlineData("", "deny")]
    [InlineData("", "bump")]
    [InlineData("", "unchanged")]
    [InlineData("/spaces", "deny")]
    [InlineData("/spaces", "unchanged")]
    [InlineData("/relationships", "bump")]
    [InlineData("/relationships", "unchanged")]
    [InlineData("/summary", "deny")]
    [InlineData("/summary", "unchanged")]
    public async Task REQ_G14_Captain_authenticated_GET_checks_final_authority(string suffix, string change)
    {
        var users = new InMemoryM1Store();
        var store = new FlowStore();
        var authorization = new CaptainLocalIntegrationTests.ReadAuthorization(change, store);
        using var factory = M1ApiIntegrationTests.CreateFactory(users, s => s[CaptainLocalIntegration.EnabledKey] = "true", services =>
        {
            services.AddSingleton<IProjectAuthorizationPort>(authorization);
            services.AddSingleton<IRequirementRevisionRepository>(store);
        });
        using var client = factory.CreateClient();
        const string email = "g14-reader@example.invalid";
        client.DefaultRequestHeaders.Authorization = new("Bearer", await M1ApiIntegrationTests.RegisterAndLoginAsync(client, email));
        var actor = (await users.GetByEmailAsync(email.ToUpperInvariant(), default))!.Id;
        const string marker = "private-g14-room";
        var revision = new RequirementRevision(RevisionId, ProjectId, 1, "requirements/1", "fixture",
            new RequirementContent(1, [new RoomCount(marker, 2)]), DateTimeOffset.UtcNow);
        Assert.Equal(RequirementSaveStatus.Saved, (await store.TrySaveAtomicallyAsync(null, revision, default)).Status);
        using var response = await client.GetAsync($"/api/v1/projects/{ProjectId}/requirements{suffix}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(new[] { (ProjectId, actor, 0), (ProjectId, actor, 1) }, authorization.Calls);
        Assert.Equal(1, store.Reads);
        var body = await response.Content.ReadAsStringAsync();
        if (change == "unchanged")
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(new RequirementRevisionDto(revision), JsonSerializerOptions.Web),
                JsonNode.Parse(body)));
        }
        else
        {
            await AssertProblem(response, 404, "NOT_FOUND");
            Assert.DoesNotContain(marker, body);
            Assert.DoesNotContain(RevisionId.ToString(), body);
            Assert.DoesNotContain("groundFloorRooms", body);
            Assert.DoesNotContain("revisionId", body);
        }
    }

    private static JsonObject Body() => JsonNode.Parse("""
        {"expectedRevisionId":null,"catalogueVersion":"synthetic-policy","floorCount":1,
         "groundFloorRooms":[{"roomType":"ROOM_A","count":1}],"additionalSpaces":[],"relationships":[],"designMethod":null}
        """)!.AsObject();

    private static RequirementsOperationResult Result(RequirementsOperationStatus status) => new(status,
        status == RequirementsOperationStatus.Success ? new RequirementRevisionDto(new RequirementRevision(RevisionId, ProjectId, 1,
            "synthetic-schema", "synthetic-policy", new RequirementContent(1, [new RoomCount("ROOM_A", 1)]), DateTimeOffset.UtcNow)) : null,
        status is RequirementsOperationStatus.ValidationFailed or RequirementsOperationStatus.CatalogueConflict
            ? [new RequirementValidationIssue("CATALOGUE_VERSION_MISMATCH", "catalogueVersion", "Synthetic validation issue")] : null);

    private static async Task AssertProblem(HttpResponseMessage response, int status, string? code = null)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync(); var json = JsonNode.Parse(body)!;
        Assert.Equal(status, json["status"]!.GetValue<int>());
        Assert.False(string.IsNullOrWhiteSpace(json["traceId"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(json["code"]?.GetValue<string>()));
        if (code is not null) Assert.Equal(code, json["code"]!.GetValue<string>());
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vastu", body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient AuthenticatedClient(WebApplication app)
    {
        var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Requirements-Test-Auth", "yes"); return client;
    }

    private static async Task<WebApplication> Host(StubService? service, FlowDependencies? flow = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("RequirementsTest").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("RequirementsTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Apply);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        if (service is not null)
        {
            builder.Services.AddSingleton<IRequirementsApplicationService>(service);
            builder.Services.AddSingleton<IApiEndpointModule, RequirementsEndpointModule>();
        }
        else
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
            builder.Services.AddSingleton<IProjectRepository>(flow!.Projects);
            builder.Services.AddSingleton<IRequirementRevisionRepository>(flow.Store);
            builder.Services.AddSingleton<IRequirementCatalogueProvider>(flow.Catalogue);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddIsolatedRequirementsFeature(builder.Environment);
        }
        var app = builder.Build();
        app.UseMiddleware<SecurityHeadersMiddleware>(); app.UseExceptionHandler(); app.UseStatusCodePages();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapBuildMindApiV1();
        await app.StartAsync(); return app;
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers["X-Requirements-Test-Auth"] == "yes"
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Requirements-Test-Subject"].FirstOrDefault()
                        ?? "70000000-0000-0000-0000-000000000007")], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }
    private sealed class StubService : IRequirementsApplicationService
    {
        public RequirementsOperationResult Result { get; init; } = new(RequirementsOperationStatus.NoSavedRevision);
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Guid Project { get; private set; }
        public SaveRequirementsRequest? Request { get; private set; }
        public Task<RequirementsOperationResult> GetAsync(Guid projectId, CancellationToken cancellationToken) => Call(projectId, null);
        public Task<RequirementsOperationResult> SaveAsync(Guid projectId, SaveRequirementsRequest? request, CancellationToken cancellationToken) => Call(projectId, request);
        private Task<RequirementsOperationResult> Call(Guid project, SaveRequirementsRequest? request)
        {
            Calls++; Project = project; Request = request;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<RequirementsOperationResult>(Failure);
        }
    }
    internal static Task<WebApplication> CreateSectionTestHost(FlowDependencies flow) => Host(null, flow);
    internal static HttpClient SectionAuthenticatedClient(WebApplication app) => AuthenticatedClient(app);

    internal sealed class FlowDependencies
    {
        public Project Project { get; } = new() { OwnerId = Guid.Parse("70000000-0000-0000-0000-000000000007"), Name = "Synthetic private project" };
        public FlowProjects Projects { get; }
        public FlowStore Store { get; } = new();
        public IRequirementCatalogueProvider Catalogue { get; init; } = new FlowCatalogue();
        public FlowDependencies() => Projects = new(Project);
    }
    private sealed class FlowCatalogue : IRequirementCatalogueProvider
    {
        public Task<RequirementCatalogue?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<RequirementCatalogue?>(new("synthetic-policy", "synthetic-schema", new(1, 1),
                [new("ROOM_A", new(0, 3), true)], [new("SYNTHETIC_SPACE", false, null)], [], [], false));
    }
    internal sealed class FlowProjects(Project project) : IProjectRepository
    {
        public Task<Project?> GetByIdAndOwnerAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(id == project.Id && ownerId == project.OwnerId ? project : null);
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(Project value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Project value, Guid ownerId, long expectedRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    internal sealed class FlowStore : IRequirementRevisionRepository
    {
        private readonly object _gate = new();
        public RequirementRevision? Current { get; private set; }
        public Dictionary<Guid, RequirementRevision> History { get; } = [];
        public int Reads { get; private set; }
        public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Reads++;
                return Task.FromResult(Current?.ProjectId == projectId ? Current : null);
            }
        }
        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken)
        {
            lock (_gate)
                return Task.FromResult(History.TryGetValue(revisionId, out var value) && value.ProjectId == projectId ? value : null);
        }
        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (Current?.Id != expectedRevisionId) return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.RevisionConflict));
                Current = proposedRevision; History.Add(Current.Id, Current);
                return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.Saved, Current));
            }
        }
    }
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Synthetic requirements host";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
