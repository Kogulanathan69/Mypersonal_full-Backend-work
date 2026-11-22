using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Services;
using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Application.Features.Users.Services;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Tests.Projects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class OwnerScopedStartupTests
{
    private const string Projects = "/api/v1/projects";
    private const string Profile = "/api/v1/profile";

    [Theory]
    [InlineData("POST", Projects)]
    [InlineData("GET", Projects)]
    [InlineData("GET", Projects + "/10000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", Projects + "/10000000-0000-0000-0000-000000000001")]
    [InlineData("GET", Profile)]
    [InlineData("PUT", Profile)]
    public async Task ProtectedRoutes_RejectAnonymousRequests(string method, string route)
    {
        await using var startup = new AuthenticationStartupTests.AuthFactory();
        await using var factory = AddTestModules(startup);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method != "GET")
            request.Content = JsonContent.Create(new { name = "Synthetic project", projectType = "Residential", fullName = "Synthetic user", revision = 1 });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task ProjectCrud_IsOwnerScopedAndRejectsStaleRevision()
    {
        await using var startup = new AuthenticationStartupTests.AuthFactory();
        // M1-02 fail-closed session check: a token is only honoured for an existing, active user.
        await startup.Users.AddAsync(ProfileEndpointTests.Owner, default);
        await startup.Users.AddAsync(ProfileEndpointTests.Other, default);
        await using var factory = AddTestModules(startup);
        using var owner = CreateClient(factory, ProfileEndpointTests.Owner);
        using var other = CreateClient(factory, ProfileEndpointTests.Other);
        using var create = await owner.PostAsJsonAsync(Projects, new { name = "Original project", projectType = "Residential" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var project = Assert.IsType<ProjectResponse>(await create.Content.ReadFromJsonAsync<ProjectResponse>());
        var route = $"{Projects}/{project.Id}";
        Assert.Equal(route, create.Headers.Location?.OriginalString);
        Assert.Equal(project.Id, Assert.Single((await owner.GetFromJsonAsync<ProjectResponse[]>(Projects))!).Id);
        Assert.Empty((await other.GetFromJsonAsync<ProjectResponse[]>(Projects))!);
        Assert.Equal(project.Name, (await owner.GetFromJsonAsync<ProjectResponse>(route))?.Name);

        using var deniedRead = await other.GetAsync(route);
        using var deniedWrite = await other.PatchAsJsonAsync(route, new { name = "Denied edit", revision = project.Revision });
        Assert.Equal(HttpStatusCode.NotFound, deniedRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedWrite.StatusCode);
        using var update = await owner.PatchAsJsonAsync(route, new { name = "Saved edit", revision = project.Revision });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = Assert.IsType<ProjectResponse>(await update.Content.ReadFromJsonAsync<ProjectResponse>());
        Assert.Equal(project.Revision + 1, saved.Revision);
        using var stale = await owner.PatchAsJsonAsync(route, new { name = "Stale edit", revision = project.Revision });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await owner.GetFromJsonAsync<ProjectResponse>(route);
        Assert.Equal("Saved edit", current?.Name);
        Assert.Equal(saved.Revision, current?.Revision);
    }

    [Fact]
    public async Task ProfileReadAndUpdate_UseJwtSubjectAndKeepProtectedFields()
    {
        await using var startup = new AuthenticationStartupTests.AuthFactory();
        // M1-02 fail-closed session check: a token is only honoured for an existing, active user.
        await startup.Users.AddAsync(ProfileEndpointTests.Owner, default);
        await startup.Users.AddAsync(ProfileEndpointTests.Other, default);
        await using var factory = AddTestModules(startup);
        using var owner = CreateClient(factory, ProfileEndpointTests.Owner);
        using var other = CreateClient(factory, ProfileEndpointTests.Other);
        var original = Assert.IsType<ProfileResponse>(await owner.GetFromJsonAsync<ProfileResponse>(Profile));
        var otherId = ProfileEndpointTests.Other.Id;
        Assert.Equal(ProfileEndpointTests.Owner.Id, original.Id);
        using var invalid = await owner.PutAsJsonAsync(Profile, new { fullName = " ", phoneNumber = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(original.FullName, (await owner.GetFromJsonAsync<ProfileResponse>(Profile))?.FullName);

        using var update = await owner.PutAsJsonAsync(Profile, new
        {
            fullName = "Saved profile",
            phoneNumber = "+94770000000",
            userId = otherId,
            ownerId = otherId,
            email = "replacement@example.test",
            isActive = false,
            role = "synthetic-admin",
            passwordHash = "synthetic-replacement-hash"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = Assert.IsType<ProfileResponse>(await owner.GetFromJsonAsync<ProfileResponse>(Profile));
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.Email, saved.Email);
        Assert.Equal(original.IsActive, saved.IsActive);
        Assert.Equal(original.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Equal("Saved profile", saved.FullName);
        Assert.Equal("+94770000000", saved.PhoneNumber);
        var otherProfile = Assert.IsType<ProfileResponse>(await other.GetFromJsonAsync<ProfileResponse>(Profile));
        Assert.Equal(otherId, otherProfile.Id);
        Assert.Equal("Original profile", otherProfile.FullName);
        var responseBody = await update.Content.ReadAsStringAsync();
        Assert.DoesNotContain("synthetic-admin", responseBody);
        Assert.DoesNotContain("synthetic-replacement-hash", responseBody);
    }

    private static WebApplicationFactory<Program> AddTestModules(WebApplicationFactory<Program> startup) =>
        startup.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Profile/Project modules and services are registered by AddM1AuthenticationApi (M1-01); only the stores are replaced.
            services.AddScoped<ProjectService>();
            services.AddScoped<ProfileService>();
            services.AddSingleton<IProjectRepository>(new ProjectHttpIntegrationTests.InMemoryProjectRepository([]));
            services.AddSingleton<IUserProfileRepository, ProfileEndpointTests.ProfileStore>();
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, User user)
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
