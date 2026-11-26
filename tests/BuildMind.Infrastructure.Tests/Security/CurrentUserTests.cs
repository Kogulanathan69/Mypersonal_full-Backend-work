using System.Net.Http.Json;
using System.Security.Claims;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests.Security;

/// <summary>
/// Current user context. All principals here are SYNTHETIC test principals built in memory; they do not
/// represent working JWT authentication, which is not implemented (A1/A2, M1 coordination pending).
/// </summary>
public sealed class CurrentUserTests
{
    private const string SyntheticAuthenticationType = "SyntheticTestAuthentication";
    private static readonly Guid SyntheticUserId = Guid.Parse("6f1c2e74-3a5b-4d8e-9c10-2b7a4f5e8d91");

    [Fact]
    public void Missing_http_context_is_unauthenticated()
    {
        AssertAnonymous(new HttpContextCurrentUser(new HttpContextAccessor()));
    }

    [Fact]
    public void Unauthenticated_principal_is_unauthenticated_even_with_identifier_claims()
    {
        var identity = new ClaimsIdentity([new Claim("sub", SyntheticUserId.ToString())]); // no authentication type

        AssertAnonymous(CreateUser(identity));
    }

    [Fact]
    public void Authenticated_identity_without_identifier_is_unauthenticated()
    {
        AssertAnonymous(CreateUser(Authenticated(new Claim(ClaimTypes.Role, "Customer"))));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public void Malformed_or_empty_identifier_fails_closed(string value)
    {
        AssertAnonymous(CreateUser(Authenticated(new Claim("sub", value))));
    }

    [Fact]
    public void Conflicting_identifier_claims_fail_closed()
    {
        var identity = Authenticated(
            new Claim("sub", SyntheticUserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        AssertAnonymous(CreateUser(identity));
    }

    [Fact]
    public void Valid_identifier_is_returned()
    {
        var user = CreateUser(Authenticated(new Claim("sub", SyntheticUserId.ToString())));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(SyntheticUserId, user.UserId);
        Assert.Equal(SyntheticUserId, user.GetRequiredUserId());
        Assert.Empty(user.Roles);
    }

    [Fact]
    public void Matching_sub_and_name_identifier_are_accepted()
    {
        var user = CreateUser(Authenticated(
            new Claim("sub", SyntheticUserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, SyntheticUserId.ToString().ToUpperInvariant())));

        Assert.Equal(SyntheticUserId, user.UserId);
    }

    [Fact]
    public void Roles_are_extracted_without_assuming_privileges()
    {
        var user = CreateUser(Authenticated(
            new Claim("sub", SyntheticUserId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim("role", "Customer"),
            new Claim("role", " "),
            new Claim("role", "SyntheticReviewer")));

        Assert.Equal(new[] { "Customer", "SyntheticReviewer" }, user.Roles.OrderBy(r => r, StringComparer.Ordinal));
        Assert.DoesNotContain("Admin", user.Roles);
    }

    [Fact]
    public void Roles_of_unauthenticated_principal_are_ignored()
    {
        var identity = new ClaimsIdentity([new Claim("sub", SyntheticUserId.ToString()), new Claim(ClaimTypes.Role, "Admin")]);

        Assert.Empty(CreateUser(identity).Roles);
    }

    [Fact]
    public void Roles_are_not_combined_across_unrelated_identities()
    {
        var subjectIdentity = Authenticated(new Claim("sub", SyntheticUserId.ToString()), new Claim("role", "Customer"));
        var unrelatedIdentity = Authenticated(new Claim(ClaimTypes.Role, "Admin"));
        var principal = new ClaimsPrincipal([subjectIdentity, unrelatedIdentity]);

        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

        Assert.Equal(SyntheticUserId, user.UserId);
        Assert.Equal(new[] { "Customer" }, user.Roles.ToArray());
    }

    [Fact]
    public void Multiple_identities_carrying_identifiers_fail_closed()
    {
        var first = Authenticated(new Claim("sub", SyntheticUserId.ToString()));
        var second = Authenticated(new Claim("sub", SyntheticUserId.ToString()), new Claim("role", "Admin"));
        var principal = new ClaimsPrincipal([first, second]);

        AssertAnonymous(new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }));
    }

    [Fact]
    public async Task Identity_cannot_be_spoofed_through_headers_or_query()
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            SyntheticTestEndpoints.Prefix + $"/current-user?userId={SyntheticUserId}&sub={SyntheticUserId}");
        request.Headers.Add("X-User-Id", SyntheticUserId.ToString());
        request.Headers.Add("sub", SyntheticUserId.ToString());
        request.Headers.Add("X-Roles", "Admin");
        // Synthetic, unsigned token-shaped value: it must never be decoded or trusted.
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer synthetic.unsigned.token");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserView>();

        Assert.NotNull(body);
        Assert.False(body!.IsAuthenticated);
        Assert.Null(body.UserId);
        Assert.Empty(body.Roles);
    }

    [Fact]
    public async Task Current_user_is_registered_with_scoped_lifetime()
    {
        await using var factory = new BuildMindApiFactory("Production");
        _ = factory.Services; // build host

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ICurrentUser>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.IsType<HttpContextCurrentUser>(first);
    }

    private static HttpContextCurrentUser CreateUser(ClaimsIdentity identity)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return new HttpContextCurrentUser(accessor);
    }

    private static ClaimsIdentity Authenticated(params Claim[] claims) =>
        new(claims, SyntheticAuthenticationType);

    private static void AssertAnonymous(ICurrentUser user)
    {
        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
        Assert.Empty(user.Roles);
        Assert.Throws<UnauthenticatedAccessException>(() => user.GetRequiredUserId());
    }

    private sealed record CurrentUserView(bool IsAuthenticated, Guid? UserId, string[] Roles);
}
