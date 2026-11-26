using System.Security.Cryptography;

namespace BuildMind.Application.Features.Auth.Services;

/// <summary>
/// Opaque refresh tokens: 256 random bits, base64url without padding (43 characters). Only the SHA-256 hash
/// (upper-case hex) is persisted; the high entropy makes a slow password hash unnecessary.
/// </summary>
public static class RefreshTokenCodec
{
    public const int TokenSizeBytes = 32;
    public const int EncodedLength = 43;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool IsWellFormed(string? token) =>
        token is { Length: EncodedLength }
        && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string Hash(string token)
    {
        if (!IsWellFormed(token))
            throw new ArgumentException("Invalid refresh token format.", nameof(token));

        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));
    }
}
