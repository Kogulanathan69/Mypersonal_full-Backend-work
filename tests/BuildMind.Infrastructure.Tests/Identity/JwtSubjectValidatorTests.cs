using System.Security.Claims;
using BuildMind.Infrastructure.Identity;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class JwtSubjectValidatorTests
{
    private static ClaimsPrincipal CreatePrincipal(
        params string[] subjects)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                subjects.Select(s => new Claim("sub", s)),
                "TestAuthentication"));
    }

    [Fact]
    public void ValidSubject_ShouldPass()
    {
        Assert.True(JwtSubjectValidator.IsValid(
            CreatePrincipal(Guid.NewGuid().ToString())));
    }

    [Fact]
    public void MissingSubject_ShouldFail()
    {
        Assert.False(JwtSubjectValidator.IsValid(
            CreatePrincipal()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void InvalidSubject_ShouldFail(string subject)
    {
        Assert.False(JwtSubjectValidator.IsValid(
            CreatePrincipal(subject)));
    }

    [Fact]
    public void DuplicateSubject_ShouldFail()
    {
        var id = Guid.NewGuid().ToString();

        Assert.False(JwtSubjectValidator.IsValid(
            CreatePrincipal(id, id)));
    }

    [Fact]
    public void AnonymousUser_ShouldFail()
    {
        Assert.False(JwtSubjectValidator.IsValid(
            new ClaimsPrincipal(new ClaimsIdentity())));
    }
}