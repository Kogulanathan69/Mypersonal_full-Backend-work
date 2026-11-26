using System.Security.Cryptography;

namespace BuildMind.Infrastructure.Identity;

public static class PasswordResetTokenService
{
    public const int TokenSizeBytes = 32;
    public const int ExpirationMinutes = 15;

    public static string Generate()
    {
        return Convert.ToHexString(
            RandomNumberGenerator.GetBytes(TokenSizeBytes));
    }

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (token.Length != TokenSizeBytes * 2)
            throw new ArgumentException("Invalid token format.");

        return Convert.ToHexString(
            SHA256.HashData(Convert.FromHexString(token)));
    }

    public static bool Verify(string? token, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(storedHash) ||
            token.Length != 64 ||
            storedHash.Length != 64)
            return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Hash(token)),
                Convert.FromHexString(storedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}