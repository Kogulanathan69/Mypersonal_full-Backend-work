using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using Flow = BuildMind.Infrastructure.Tests.RequirementsEndpointTests.FlowDependencies;

namespace BuildMind.Infrastructure.Tests;

// Existing authenticated TestServer host, real M5 services and synthetic in-memory dependencies only.
public sealed class RequirementsSectionsEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Day05Routes_RejectUntrustedIdentity(bool otherCustomer)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = otherCustomer ? RequirementsEndpointTests.SectionAuthenticatedClient(app) : app.GetTestClient();
        if (otherCustomer) client.DefaultRequestHeaders.Add("X-Requirements-Test-Subject", "90000000-0000-0000-0000-000000000009");
        using var summary = await client.GetAsync($"/api/v1/projects/{flow.Project.Id}/requirements/summary");
        await AssertProblem(summary, otherCustomer ? 404 : 401);
        using var method = await client.PutAsJsonAsync($"/api/v1/projects/{flow.Project.Id}/design-method",
            new { expectedRevisionId = Guid.NewGuid(), catalogueVersion = "synthetic-policy", designMethod = "METHOD_A" });
        await AssertProblem(method, otherCustomer ? 404 : 401);
        Assert.Empty(flow.Store.History);
        Assert.Equal(0, flow.Store.Reads);
    }

    [Theory]
    [InlineData("{}", 400)]
    [InlineData("{\"designMethod\":null}", 200)]
    [InlineData("{\"designMethod\":\"UNKNOWN\"}", 422)]
    public async Task MethodBindingAndPolicy_RejectInvalidInput(string selection, int status)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app);
        var first = await Seed(client, flow);
        var body = JsonNode.Parse(selection)!.AsObject();
        body["expectedRevisionId"] = first.ToString();
        body["catalogueVersion"] = "synthetic-policy";
        using var response = await client.PutAsJsonAsync($"/api/v1/projects/{flow.Project.Id}/design-method", body);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(status == 200 ? 2 : 1, flow.Store.History.Count);
        if (status == 200) Assert.Null(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["designMethod"]);
    }

    [Fact]
    public async Task SummaryAndMethod_PreserveExactHistory()
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app);
        var first = await Seed(client, flow);
        using var response = await client.PutAsJsonAsync($"/api/v1/projects/{flow.Project.Id}/design-method",
            new { expectedRevisionId = first, catalogueVersion = "synthetic-policy", designMethod = "METHOD_A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotEqual(first, saved["revisionId"]!.GetValue<Guid>());
        Assert.Equal(2L, saved["revisionNumber"]!.GetValue<long>());
        using var summary = await client.GetAsync($"/api/v1/projects/{flow.Project.Id}/requirements/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.True(summary.Headers.CacheControl?.NoStore);
        Assert.Equal(flow.Project.Id, JsonNode.Parse(await summary.Content.ReadAsStringAsync())!["projectId"]!.GetValue<Guid>());
        Assert.Equal("METHOD_A", flow.Store.History[first].Content.DesignMethod);
        Assert.Equal(2, flow.Store.History.Count);
    }

    [Theory]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public async Task ClearSection_PreservesOtherFieldsAndExactHistory(string section)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app);
        var first = await Seed(client, flow);
        using var response = await client.PutAsJsonAsync(Route(flow, section), Body(section, first));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var dto = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(2L, dto["revisionNumber"]!.GetValue<long>());
        Assert.Equal(2, dto["floorCount"]!.GetValue<int>());
        Assert.Equal("METHOD_A", dto["designMethod"]!.GetValue<string>());
        Assert.Equal(2, dto["groundFloorRooms"]!.AsArray().Count);
        Assert.Empty(dto[Collection(section)]!.AsArray());
        Assert.Single(dto[section == "spaces" ? "relationships" : "additionalSpaces"]!.AsArray());
        Assert.Single(flow.Store.History[first].Content.AdditionalSpaces);
        Assert.Single(flow.Store.History[first].Content.Relationships);
        using var get = await client.GetAsync(Route(flow, section));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(dto["revisionId"]!.GetValue<Guid>(), JsonNode.Parse(await get.Content.ReadAsStringAsync())!["revisionId"]!.GetValue<Guid>());
    }

    [Theory]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public async Task ProvisionalMissingBase_Get204AndPut409(string section)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app);
        using var get = await client.GetAsync(Route(flow, section));
        Assert.Equal(HttpStatusCode.NoContent, get.StatusCode);
        using var put = await client.PutAsJsonAsync(Route(flow, section), Body(section, Guid.NewGuid()));
        await AssertProblem(put, 409, "REVISION_CONFLICT");
        Assert.Empty(flow.Store.History);
    }

    [Theory]
    [InlineData("spaces", "GET")]
    [InlineData("spaces", "PUT")]
    [InlineData("relationships", "GET")]
    [InlineData("relationships", "PUT")]
    public async Task Unauthenticated_UsesExistingAuthorization(string section, string method)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route(flow, section)));
        await AssertProblem(response, 401, "UNAUTHENTICATED");
        Assert.Equal(0, flow.Store.Reads);
    }

    [Theory]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public async Task CrossAccountAndMissingProject_AreUniform404(string section)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var owner = RequirementsEndpointTests.SectionAuthenticatedClient(app); var first = await Seed(owner, flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app);
        client.DefaultRequestHeaders.Add("X-Requirements-Test-Subject", "90000000-0000-0000-0000-000000000009");
        var reads = flow.Store.Reads;
        foreach (var project in new[] { flow.Project.Id, Guid.NewGuid() })
        {
            var route = $"/api/v1/projects/{project}/requirements/{section}";
            using var get = await client.GetAsync(route); await AssertProblem(get, 404, "NOT_FOUND");
            var body = Body(section, first); body["ownerId"] = flow.Project.OwnerId.ToString();
            using var put = await client.PutAsJsonAsync(route, body); await AssertProblem(put, 404, "NOT_FOUND");
        }
        Assert.Equal(reads, flow.Store.Reads);
        Assert.Single(flow.Store.History);
    }

    [Theory]
    [InlineData("spaces", "expectedRevisionId")]
    [InlineData("spaces", "catalogueVersion")]
    [InlineData("spaces", "additionalSpaces")]
    [InlineData("relationships", "expectedRevisionId")]
    [InlineData("relationships", "catalogueVersion")]
    [InlineData("relationships", "relationships")]
    public async Task EverySectionField_MustBePresent(string section, string field)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var body = Body(section, Guid.NewGuid()); body.Remove(field);
        using var response = await client.PutAsJsonAsync(Route(flow, section), body);
        await AssertProblem(response, 400); Assert.Equal(0, flow.Store.Reads);
    }

    [Theory]
    [InlineData("spaces", "expectedRevisionId")]
    [InlineData("spaces", "catalogueVersion")]
    [InlineData("spaces", "additionalSpaces")]
    [InlineData("relationships", "expectedRevisionId")]
    [InlineData("relationships", "catalogueVersion")]
    [InlineData("relationships", "relationships")]
    public async Task RequiredSectionFields_CannotBeNull(string section, string field)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var body = Body(section, Guid.NewGuid()); body[field] = null;
        using var response = await client.PutAsJsonAsync(Route(flow, section), body);
        await AssertProblem(response, 400); Assert.Equal(0, flow.Store.Reads);
    }

    [Theory]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public async Task EmptyExpectedIdAndNullItems_Are400(string section)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var body = Body(section, Guid.Empty);
        using var emptyId = await client.PutAsJsonAsync(Route(flow, section), body); await AssertProblem(emptyId, 400);
        body = Body(section, Guid.NewGuid()); body[Collection(section)] = new JsonArray((JsonNode?)null);
        using var nullItem = await client.PutAsJsonAsync(Route(flow, section), body); await AssertProblem(nullItem, 400);
        Assert.Equal(0, flow.Store.Reads);
    }

    [Theory]
    [InlineData("spaces", "unknown")]
    [InlineData("spaces", "duplicate")]
    [InlineData("spaces", "length")]
    [InlineData("relationships", "reference")]
    [InlineData("relationships", "duplicate")]
    [InlineData("relationships", "self")]
    public async Task InvalidPolicySelections_Are422WithoutMutation(string section, string kind)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var first = await Seed(client, flow);
        var body = Body(section, first);
        body[Collection(section)] = JsonNode.Parse((section, kind) switch
        {
            ("spaces", "unknown") => "[{\"option\":\"UNKNOWN\",\"otherText\":null}]",
            ("spaces", "duplicate") => "[{\"option\":\"SPACE_A\",\"otherText\":null},{\"option\":\"SPACE_A\",\"otherText\":null}]",
            ("spaces", _) => "[{\"option\":\"SPACE_TEXT\",\"otherText\":\"123456\"}]",
            (_, "reference") => "[{\"sourceRoomType\":\"UNKNOWN\",\"targetRoomType\":\"ROOM_B\",\"kind\":\"NEAR\"}]",
            (_, "duplicate") => "[{\"sourceRoomType\":\"ROOM_A\",\"targetRoomType\":\"ROOM_B\",\"kind\":\"NEAR\"},{\"sourceRoomType\":\"ROOM_B\",\"targetRoomType\":\"ROOM_A\",\"kind\":\"NEAR\"}]",
            _ => "[{\"sourceRoomType\":\"ROOM_A\",\"targetRoomType\":\"ROOM_A\",\"kind\":\"NEAR\"}]"
        });
        using var response = await client.PutAsJsonAsync(Route(flow, section), body);
        await AssertProblem(response, 422, "VALIDATION_FAILED");
        Assert.Equal(first, flow.Store.Current!.Id); Assert.Single(flow.Store.History);
    }

    [Fact]
    public async Task PopulatedSelectionsAndSyntheticLengthBoundary_RoundTrip()
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var first = await Seed(client, flow);
        var spaces = Body("spaces", first); spaces["additionalSpaces"] = JsonNode.Parse("[{\"option\":\"SPACE_TEXT\",\"otherText\":\"12345\"}]");
        using var saved = await client.PutAsJsonAsync(Route(flow, "spaces"), spaces);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = JsonNode.Parse(await saved.Content.ReadAsStringAsync())!;
        Assert.Equal("12345", dto["additionalSpaces"]![0]!["otherText"]!.GetValue<string>());
        var relations = Body("relationships", dto["revisionId"]!.GetValue<Guid>());
        relations["relationships"] = JsonNode.Parse("[{\"sourceRoomType\":\"ROOM_A\",\"targetRoomType\":\"ROOM_B\",\"kind\":\"FLOWS_TO\"},{\"sourceRoomType\":\"ROOM_B\",\"targetRoomType\":\"ROOM_A\",\"kind\":\"FLOWS_TO\"}]");
        using var second = await client.PutAsJsonAsync(Route(flow, "relationships"), relations);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, flow.Store.Current!.Content.Relationships.Count);
        Assert.Equal("12345", flow.Store.Current.Content.AdditionalSpaces.Single().OtherText);
    }

    [Fact]
    public async Task StaleAndConcurrentDifferentSections_DoNotLoseUpdates()
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var first = await Seed(client, flow);
        var results = await Task.WhenAll(client.PutAsJsonAsync(Route(flow, "spaces"), Body("spaces", first)),
            client.PutAsJsonAsync(Route(flow, "relationships"), Body("relationships", first)));
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(2, flow.Store.History.Count);
            Assert.Equal(2L, flow.Store.Current!.RevisionNumber);
            Assert.True((flow.Store.Current.Content.AdditionalSpaces.Count == 0) ^ (flow.Store.Current.Content.Relationships.Count == 0));
            using var stale = await client.PutAsJsonAsync(Route(flow, "spaces"), Body("spaces", first));
            await AssertProblem(stale, 409, "REVISION_CONFLICT");
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Fact]
    public async Task UntrustedCoreFields_CannotChangePreservedData()
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var first = await Seed(client, flow);
        var body = Body("spaces", first); body["floorCount"] = 999; body["groundFloorRooms"] = new JsonArray(); body["designMethod"] = "UNTRUSTED";
        body["relationships"] = new JsonArray(); body["ownerId"] = Guid.NewGuid().ToString();
        using var response = await client.PutAsJsonAsync(Route(flow, "spaces"), body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, flow.Store.Current!.Content.FloorCount);
        Assert.Equal("METHOD_A", flow.Store.Current.Content.DesignMethod);
        Assert.Single(flow.Store.Current.Content.Relationships);
        Assert.Equal(2, flow.Store.Current.Content.GroundFloorRooms.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogueConflictAndUnavailability_AreNotEmptySuccess(bool unavailable)
    {
        var catalogue = new SyntheticCatalogue(); var flow = new Flow { Catalogue = catalogue };
        await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app); var first = await Seed(client, flow);
        var body = Body("spaces", first);
        if (unavailable) catalogue.Available = false; else body["catalogueVersion"] = "UNKNOWN_VERSION";
        using var response = await client.PutAsJsonAsync(Route(flow, "spaces"), body);
        await AssertProblem(response, unavailable ? 503 : 409);
        Assert.Single(flow.Store.History);
    }

    [Theory]
    [InlineData("spaces", "null")]
    [InlineData("relationships", "{malformed")]
    public async Task MalformedBody_IsSafe400(string section, string body)
    {
        var flow = CreateFlow(); await using var app = await RequirementsEndpointTests.CreateSectionTestHost(flow);
        using var client = RequirementsEndpointTests.SectionAuthenticatedClient(app);
        using var response = await client.PutAsync(Route(flow, section), new StringContent(body, Encoding.UTF8, "application/json"));
        await AssertProblem(response, 400); Assert.Equal(0, flow.Store.Reads);
    }

    private static Flow CreateFlow() => new() { Catalogue = new SyntheticCatalogue() };
    private static string Route(Flow flow, string section) => $"/api/v1/projects/{flow.Project.Id}/requirements/{section}";
    private static string Collection(string section) => section == "spaces" ? "additionalSpaces" : "relationships";
    private static JsonObject Body(string section, Guid expected) => new()
    {
        ["expectedRevisionId"] = expected.ToString(), ["catalogueVersion"] = "synthetic-policy", [Collection(section)] = new JsonArray()
    };
    private static async Task<Guid> Seed(HttpClient client, Flow flow)
    {
        var body = JsonNode.Parse("""
            {"expectedRevisionId":null,"catalogueVersion":"synthetic-policy","floorCount":2,
             "groundFloorRooms":[{"roomType":"ROOM_A","count":2},{"roomType":"ROOM_B","count":1}],
             "additionalSpaces":[{"option":"SPACE_TEXT","otherText":"text"}],
             "relationships":[{"sourceRoomType":"ROOM_A","targetRoomType":"ROOM_B","kind":"NEAR"}],"designMethod":"METHOD_A"}
            """);
        using var response = await client.PutAsJsonAsync($"/api/v1/projects/{flow.Project.Id}/requirements", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["revisionId"]!.GetValue<Guid>();
    }
    private static async Task AssertProblem(HttpResponseMessage response, int status, string? code = null)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync(); var json = JsonNode.Parse(body)!;
        Assert.False(string.IsNullOrWhiteSpace(json["traceId"]?.GetValue<string>()));
        if (code is not null) Assert.Equal(code, json["code"]!.GetValue<string>());
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vastu", body, StringComparison.OrdinalIgnoreCase);
    }
    private sealed class SyntheticCatalogue : IRequirementCatalogueProvider
    {
        public bool Available { get; set; } = true;
        public Task<RequirementCatalogue?> GetForProjectAsync(Guid projectId, CancellationToken token) => Task.FromResult<RequirementCatalogue?>(Available
            ? new("synthetic-policy", "synthetic-schema", new(1, 3), [new("ROOM_A", new(1, 4), true), new("ROOM_B", new(0, 2), false)],
                [new("SPACE_A", false, null), new("SPACE_TEXT", true, 5)],
                [new("NEAR", false, false), new("FLOWS_TO", true, false)], ["METHOD_A"], false) : null);
    }
}
