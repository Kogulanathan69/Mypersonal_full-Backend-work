using System.Security.Cryptography;
using BuildMind.Application.Features.Auth.Abstractions;

namespace BuildMind.Infrastructure.Identity;

public sealed class PasswordHashService : IPasswordHashService
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Algorithm = "PBKDF2-SHA256-V1";

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return string.Join(
            "$",
            Algorithm,
            Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(
        string password,
        string passwordHash)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(passwordHash) ||
            passwordHash.Length > 256)
        {
            return false;
        }

        var parts = passwordHash.Split('$');

        if (parts.Length != 4 ||
            parts[0] != Algorithm ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations != Iterations)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);

            var expectedHash =
                Convert.FromBase64String(parts[3]);

            if (salt.Length != SaltSize ||
                expectedHash.Length != HashSize)
            {
                return false;
            }

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return CryptographicOperations.FixedTimeEquals(
                actualHash,
                expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}