using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.Auth.DTOs;

public sealed class AuthResponse
{
    public required string AccessToken { get; init; }

    public string TokenType { get; init; } = "Bearer";

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public required UserResponse User { get; init; }

    /// <summary>Present only when refresh tokens are enabled. Single use: every refresh returns a new one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; init; }
}

public sealed class RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}
