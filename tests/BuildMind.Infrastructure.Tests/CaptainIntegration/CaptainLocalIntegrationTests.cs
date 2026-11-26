using System.Net;
using BuildMind.API.Composition;
using BuildMind.API.Endpoints.Requirements;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.CaptainIntegration;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using BuildMind.Infrastructure.Tests.Identity;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Infrastructure.Tests.CaptainIntegration;

public sealed class CaptainLocalIntegrationTests
{
    private const string Flag = CaptainLocalIntegration.EnabledKey;

    [Fact]
    public async Task REQ_G14_Captain_scope_checks_the_version_before_and_after_read()
    {
        var actor = Guid.NewGuid();
        var project = Guid.NewGuid();
        var store = new RequirementsEndpointTests.FlowStore();
        var auth = new ReadAuthorization("bump", store);
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => s[Flag] = "true", services =>
        {
            services.AddSingleton<ICurrentUser>(new FixedUser(actor));
            services.AddSingleton<IProjectAuthorizationPort>(auth);
            services.AddSingleton<IRequirementRevisionRepository>(store);
        });
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRequirementsApplicationService>();
        Assert.IsType<RequirementsApplicationService>(service);
        var result = await service.GetAsync(project, default);
        Assert.Equal(RequirementsOperationStatus.NotFound, result.Status);
        Assert.Null(result.Revision);
        Assert.Equal(new[] { (project, actor, 0), (project, actor, 1) }, auth.Calls);
    }

    [Fact]
    public void REQ_G14_Captain_scope_resolves_owner_protected_repository()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => s[Flag] = "true");
        using var scope = factory.Services.CreateScope();
        Assert.IsType<OwnerProtectedRequirementRevisionRepository>(scope.ServiceProvider.GetRequiredService<IRequirementRevisionRepository>());
        Assert.IsType<RequirementsApplicationService>(scope.ServiceProvider.GetRequiredService<IRequirementsApplicationService>());
    }

    [Fact]
    public void REQ_G14_Missing_Captain_authorization_fails_at_service_resolution()
    {
        using var original = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => s[Flag] = "true", services =>
        {
            services.RemoveAll<IProjectAuthorizationPort>();
            // Keep all five isolated-constructor dependencies resolvable.
            services.AddSingleton<IRequirementRevisionRepository>(new RequirementsEndpointTests.FlowStore());
        });
        using var factory = original.WithWebHostBuilder(b => b.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = false; // exercise resolution in this intentionally incomplete provider
        }));
        using var scope = factory.Services.CreateScope();
        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IRequirementsApplicationService>());
        Assert.Contains(typeof(IProjectAuthorizationPort).FullName!, failure.Message);
    }

    internal sealed class ReadAuthorization(string change, RequirementsEndpointTests.FlowStore store) : IProjectAuthorizationPort
    {
        public List<(Guid Project, Guid Actor, int Reads)> Calls { get; } = [];
        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken ct)
        {
            Calls.Add((projectId, actorId, store.Reads));
            var result = store.Reads > 0 && change == "deny"
                ? new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied)
                : new ProjectAuthorizationResult(ProjectAuthorizationStatus.Authorized,
                    new(projectId, actorId, new(store.Reads > 0 && change == "bump" ? 2 : 1)));
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Profile_is_off_by_default_even_with_authentication()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();

        Assert.DoesNotContain(factory.Services.GetServices<IApiEndpointModule>(), m => m is RequirementsEndpointModule);
        using var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/requirements");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Production", "false", "restricted to Development")]
    [InlineData("Staging", "false", "restricted to Development")]
    [InlineData("Production", "true", "Authentication activation is not approved")]
    [InlineData("Development", "false", "requires end-user authentication")]
    public void Profile_refuses_startup_outside_development_or_without_authentication(
        string environment, string authentication, string reason)
    {
        var settings = M1ApiIntegrationTests.AuthSettings();
        settings["Authentication:Enabled"] = authentication;
        settings[Flag] = "true";
        using var factory = new StartupSettingsApiFactory(environment, settings);

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(reason, failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/requirements")]
    [InlineData("/requirements/summary")]
    [InlineData("/plans/00000000-0000-4000-8000-000000000002/versions")]
    [InlineData("/land")]
    [InlineData("/generation-jobs/00000000-0000-4000-8000-000000000003")]
    public async Task Enabled_profile_maps_protected_routes_that_reject_anonymous_callers(string suffix)
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => s[Flag] = "true");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}{suffix}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(factory.Services.GetServices<IApiEndpointModule>(), m => m is RequirementsEndpointModule);
        Assert.Contains(factory.Services.GetServices<IApiEndpointModule>(), m => m is BuildMind.API.Endpoints.PlanEditor.PlanEditorEndpointModule);
    }

    [Fact]
    public void Enabled_profile_replaces_the_requirement_port_with_the_protected_adapter()
    {
        var services = new ServiceCollection();
        services.AddCaptainIntegrationInfrastructure();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRequirementRevisionRepository));
        Assert.Equal(typeof(OwnerProtectedRequirementRevisionRepository), descriptor.ImplementationType);
    }

    [Fact]
    public async Task Protected_save_without_an_actor_is_denied_without_reaching_persistence()
    {
        var authorization = new CountingAuthorization(new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied));
        var adapter = new OwnerProtectedRequirementRevisionRepository(
            new RequirementRevisionRepository(null!, null!), new FixedUser(null), authorization);

        var result = await adapter.TrySaveAtomicallyAsync(null, Revision(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(RequirementSaveStatus.ProjectNotFound, result.Status);
        Assert.Equal(0, authorization.Calls);
    }

    [Fact]
    public async Task Protected_save_for_a_non_owner_is_denied_without_reaching_persistence()
    {
        var authorization = new CountingAuthorization(new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied));
        var adapter = new OwnerProtectedRequirementRevisionRepository(
            new RequirementRevisionRepository(null!, null!), new FixedUser(Guid.NewGuid()), authorization);

        var result = await adapter.TrySaveAtomicallyAsync(null, Revision(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(RequirementSaveStatus.ProjectNotFound, result.Status);
        Assert.Equal(1, authorization.Calls);
    }

    [Fact]
    public async Task Protected_save_rejects_a_snapshot_for_another_project_or_actor()
    {
        var actor = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        var authorization = new CountingAuthorization(new ProjectAuthorizationResult(
            ProjectAuthorizationStatus.Authorized,
            new ProjectAuthorizationSnapshot(otherProject, actor, new AuthorizationVersion(1))));
        var adapter = new OwnerProtectedRequirementRevisionRepository(
            new RequirementRevisionRepository(null!, null!), new FixedUser(actor), authorization);

        var result = await adapter.TrySaveAtomicallyAsync(null, Revision(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(RequirementSaveStatus.ProjectNotFound, result.Status);
    }

    [Fact]
    public async Task Catalogue_provider_is_unavailable_without_a_configured_file()
    {
        var provider = new ConfiguredRequirementCatalogueProvider(Config(null));
        Assert.Null(await provider.GetForProjectAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"version\":\"v\",\"unknownMember\":1}")]
    [InlineData("{\"version\":\"v\",\"version\":\"w\"}")]
    [InlineData("{\"version\":\"v\",\"schemaVersion\":\"s\",\"floorCountBounds\":{\"minimum\":0,\"maximum\":1},\"rooms\":[],\"additionalSpaces\":[],\"relationships\":[],\"designMethods\":null,\"designMethodRequired\":false}")]
    public async Task Catalogue_provider_treats_invalid_files_as_unavailable(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bm-catalogue-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var provider = new ConfiguredRequirementCatalogueProvider(Config(path));
            Assert.Null(await provider.GetForProjectAsync(Guid.NewGuid(), CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Catalogue_provider_loads_the_labeled_synthetic_sample()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "captain-development", "samples", "requirement-catalogue.synthetic.json");
        var provider = new ConfiguredRequirementCatalogueProvider(Config(path));

        var catalogue = await provider.GetForProjectAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(catalogue);
        Assert.Equal("synthetic-fixture-v1", catalogue.Version);
        Assert.Equal(["bedroom", "livingRoom", "kitchen", "bathroom"], catalogue.Rooms.Select(r => r.RoomType));
    }

    private static IConfiguration Config(string? path) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [ConfiguredRequirementCatalogueProvider.FileKey] = path })
        .Build();

    private static RequirementRevision Revision(Guid projectId) => new(
        Guid.NewGuid(), projectId, 1, "requirements/1", "synthetic-fixture-v1",
        new RequirementContent(1, [new RoomCount("bedroom", 1)]), DateTimeOffset.UtcNow);

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BuildMind.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FixedUser(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsAuthenticated => id is not null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => id ?? throw new InvalidOperationException();
    }

    private sealed class CountingAuthorization(ProjectAuthorizationResult result) : IProjectAuthorizationPort
    {
        public int Calls { get; private set; }

        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
