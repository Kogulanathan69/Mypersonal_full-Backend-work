using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildMind.API.Endpoints.Regulations;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using BuildMind.API.Routing;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

/// <summary>
/// M7 production-capable registration path (default off) through the REAL composition root with M1 authentication.
/// The internal RAG service is replaced by a stub evidence source; no network, no corpus, no database.
/// </summary>
public sealed class RegulationRetrievalRegistrationTests
{
    private const string Route = "/api/v1/regulations/retrieve";

    [Fact]
    public async Task Default_configuration_does_not_register_the_endpoint()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), RagOn, StubSource(new StubEvidenceSource()));
        using var client = await SignedInClientAsync(factory, "default@example.invalid");

        using var response = await client.PostAsJsonAsync(Route, new { query = "rear setback" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_endpoint_requires_authentication_and_returns_cited_evidence()
    {
        var source = new StubEvidenceSource();
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => { RagOn(s); FlagOn(s); }, StubSource(source));

        using (var anonymous = factory.CreateClient())
        using (var rejected = await anonymous.PostAsJsonAsync(Route, new { query = "rear setback" }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        Assert.Equal(0, source.Calls);

        using var client = await SignedInClientAsync(factory, "evidence@example.invalid");
        using var response = await client.PostAsJsonAsync(Route, new { query = "  rear setback  ", maxResults = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EVIDENCE_FOUND", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("legalStatusVerified").GetBoolean());
        Assert.False(body.GetProperty("answerGenerated").GetBoolean());
        var passage = body.GetProperty("passages")[0];
        Assert.Equal(new string('a', 64), passage.GetProperty("documentId").GetString());
        Assert.Equal(7, passage.GetProperty("page").GetInt32());
        Assert.Equal("Synthetic planning regulations", passage.GetProperty("sourceTitle").GetString());
        Assert.Equal(1, source.Calls);
        Assert.Equal("rear setback", source.LastQuery?.Query);
        Assert.Equal(3, source.LastQuery?.MaxPassages);
    }

    [Fact]
    public async Task Rag_dependency_failure_surfaces_as_503_problem_details()
    {
        var source = new StubEvidenceSource { Failure = new DependencyFailureException("rag", DependencyFailureKind.Unavailable) };
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => { RagOn(s); FlagOn(s); }, StubSource(source));
        using var client = await SignedInClientAsync(factory, "outage@example.invalid");

        using var response = await client.PostAsJsonAsync(Route, new { query = "rear setback" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DEPENDENCY_UNAVAILABLE", body.GetProperty("code").GetString());
    }

    [Fact]
    public void Enabled_without_authentication_is_refused_at_registration()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddRegulationRetrievalFeature(Configuration(FlagOn, RagOn), authenticationEnabled: false));
        Assert.Contains("authentication", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(services);
    }

    [Fact]
    public void Enabled_without_the_rag_service_is_refused_at_registration()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddRegulationRetrievalFeature(Configuration(FlagOn), authenticationEnabled: true));
        Assert.Contains("Services:Rag:Enabled", error.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Disabled_flag_registers_nothing(string? flag)
    {
        var services = new ServiceCollection();
        var registered = services.AddRegulationRetrievalFeature(
            Configuration(s => s[RegulationRetrievalFeatureRegistration.EnabledKey] = flag, RagOn), authenticationEnabled: true);
        Assert.False(registered);
        Assert.Empty(services);
    }

    [Fact]
    public void Enabling_without_authentication_fails_host_startup()
    {
        // Real composition root, authentication off: the flag alone must not start an anonymous endpoint.
        using var factory = new StartupSettingsApiFactory("Development", new Dictionary<string, string?>
        {
            [RegulationRetrievalFeatureRegistration.EnabledKey] = "true",
            ["Services:Rag:Enabled"] = "true",
            ["Services:Rag:BaseUrl"] = "http://127.0.0.1:8098",
            ["Services:Rag:AuthToken"] = "synthetic-internal-token-0123456789abcdef",
        });
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("requires end-user authentication", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_calls_beyond_the_per_user_expensive_limit_are_rejected()
    {
        var source = new StubEvidenceSource { HoldUntilCancelled = true };
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), s => { RagOn(s); FlagOn(s); }, StubSource(source));
        using var client = await SignedInClientAsync(factory, "busy@example.invalid");
        using var stop = new CancellationTokenSource();

        var first = client.PostAsJsonAsync(Route, new { query = "one" }, stop.Token);
        var second = client.PostAsJsonAsync(Route, new { query = "two" }, stop.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (source.Calls < 2)
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for two in-flight requests");
            await Task.Delay(10);
        }

        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var third = await client.PostAsJsonAsync(Route, new { query = "three" }, bound.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(2, source.Calls);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Theory]
    [InlineData("Production", "true", "Authentication activation is not approved")]
    [InlineData("Production", "false", "requires end-user authentication")]
    [InlineData("Staging", "true", "Authentication activation is not approved")]
    [InlineData("Staging", "false", "requires end-user authentication")]
    public void Enabled_flag_refuses_startup_outside_development(string environment, string authentication, string reason)
    {
        // Real composition root. With authentication requested, the Development-only sign-in guard refuses first;
        // without it, the M7 guard refuses. Either way no host starts and no endpoint is exposed.
        var settings = new Dictionary<string, string?>
        {
            [RegulationRetrievalFeatureRegistration.EnabledKey] = "true",
            ["Authentication:Enabled"] = authentication,
        };
        RagOn(settings);
        using var factory = new StartupSettingsApiFactory(environment, settings);

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(reason, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_through_the_flag_path_twice_is_refused()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(FlagOn, RagOn);
        Assert.True(services.AddRegulationRetrievalFeature(configuration, authenticationEnabled: true));

        var error = Assert.Throws<InvalidOperationException>(() => services.AddRegulationRetrievalFeature(configuration, authenticationEnabled: true));
        Assert.Contains("already registered", error.Message, StringComparison.Ordinal);
        Assert.Single(ModuleRegistrations(services));
    }

    [Fact]
    public void Mixing_the_isolated_and_flag_paths_is_refused_in_either_order()
    {
        var configuration = Configuration(FlagOn, RagOn);

        var isolatedFirst = new ServiceCollection();
        isolatedFirst.AddIsolatedRegulationRetrievalFeature(new TestEnvironment("Testing"));
        Assert.Throws<InvalidOperationException>(() => isolatedFirst.AddRegulationRetrievalFeature(configuration, authenticationEnabled: true));
        Assert.Single(ModuleRegistrations(isolatedFirst));

        var flagFirst = new ServiceCollection();
        flagFirst.AddRegulationRetrievalFeature(configuration, authenticationEnabled: true);
        Assert.Throws<InvalidOperationException>(() => flagFirst.AddIsolatedRegulationRetrievalFeature(new TestEnvironment("Testing")));
        Assert.Single(ModuleRegistrations(flagFirst));
    }

    [Fact]
    public void A_module_registered_as_an_instance_is_detected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApiEndpointModule>(new RegulationRetrievalEndpointModule());

        Assert.Throws<InvalidOperationException>(() => services.AddRegulationRetrievalFeature(Configuration(FlagOn, RagOn), authenticationEnabled: true));
        Assert.Single(ModuleRegistrations(services));
    }

    private static IEnumerable<ServiceDescriptor> ModuleRegistrations(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IApiEndpointModule));

    private static void FlagOn(Dictionary<string, string?> settings) =>
        settings[RegulationRetrievalFeatureRegistration.EnabledKey] = "true";

    private static void RagOn(Dictionary<string, string?> settings)
    {
        settings["Services:Rag:Enabled"] = "true";
        settings["Services:Rag:BaseUrl"] = "http://127.0.0.1:8098";
        settings["Services:Rag:AuthToken"] = "synthetic-internal-token-0123456789abcdef";
    }

    private static IConfiguration Configuration(params Action<Dictionary<string, string?>>[] changes)
    {
        var values = new Dictionary<string, string?>();
        foreach (var change in changes)
        {
            change(values);
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Action<IServiceCollection> StubSource(StubEvidenceSource source) => services =>
    {
        services.RemoveAll<IRegulationEvidenceSource>();
        services.AddSingleton<IRegulationEvidenceSource>(source);
    };

    private static async Task<HttpClient> SignedInClientAsync(StartupSettingsApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await M1ApiIntegrationTests.RegisterAndLoginAsync(client, email));
        return client;
    }

    private sealed class StubEvidenceSource : IRegulationEvidenceSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public RegulationSearchQuery? LastQuery { get; private set; }
        public Exception? Failure { get; init; }
        public bool HoldUntilCancelled { get; init; }

        public async Task<RegulationEvidence> SearchAsync(RegulationSearchQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            Interlocked.Increment(ref _calls);
            if (Failure is not null)
            {
                throw Failure;
            }

            if (HoldUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new RegulationEvidence(
                RegulationEvidenceStatus.EvidenceFound,
                [new RegulationPassage("p-1", new string('a', 64), "Synthetic planning regulations", "Synthetic", 7, 10, 90,
                    "Synthetic excerpt.", 2.5, PassageEvidenceStatus.EvidenceCandidate, [])],
                []);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BuildMind.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
