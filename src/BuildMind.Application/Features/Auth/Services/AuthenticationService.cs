using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Validators;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Auth.Services;

internal sealed record AuthenticatedLogin(AuthResponse Response, long TokenVersion);

public sealed class AuthenticationService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHashService _passwords;
    private readonly IAccessTokenIssuer _tokens;

    public AuthenticationService(
        IUserRepository users,
        IPasswordHashService passwords,
        IAccessTokenIssuer tokens)
    {
        _users = users;
        _passwords = passwords;
        _tokens = tokens;
    }

    public async Task<UserResponse?> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var validation =
            new RegisterRequestValidator().Validate(request);

        if (!validation.IsValid)
            return null;

        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        if (await _users.ExistsByEmailAsync(
            normalizedEmail, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = _passwords.HashPassword(
                request.Password)
        };

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (DuplicateEmailException)
        {
            return null;
        }

        return MapUser(user);
    }

    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        (await AuthenticateAsync(request, cancellationToken))?.Response;

    internal async Task<AuthenticatedLogin?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var validation =
            new LoginRequestValidator().Validate(request);

        if (!validation.IsValid)
            return null;

        var normalizedEmail =
            request.Email.Trim().ToUpperInvariant();

        var user = await _users.GetByEmailAsync(
            normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Perform hashing work to reduce timing differences.
            _ = _passwords.HashPassword(request.Password);
            return null;
        }

        var passwordValid = _passwords.VerifyPassword(
            request.Password,
            user.PasswordHash);

        if (!passwordValid || !user.IsActive)
            return null;

        var token = _tokens.Issue(user);

        return new AuthenticatedLogin(new AuthResponse
        {
            AccessToken = token.AccessToken,
            TokenType = "Bearer",
            ExpiresAtUtc = token.ExpiresAtUtc,
            User = MapUser(user)
        }, user.TokenVersion);
    }

    private static UserResponse MapUser(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}