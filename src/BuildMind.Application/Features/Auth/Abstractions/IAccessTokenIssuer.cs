using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Auth.Abstractions;

public interface IAccessTokenIssuer
{
    AccessTokenResult Issue(User user);
}

public sealed record AccessTokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);