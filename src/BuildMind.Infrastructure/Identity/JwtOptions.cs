namespace BuildMind.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public const int AccessTokenMinutes = 15;

    public const int ClockSkewSeconds = 30;

    public byte[] GetSigningKey()
    {
        byte[] key;

        try
        {
            key = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Invalid JWT signing key format.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key must be at least 256 bits.");
        }

        return key;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException(
                "JWT issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException(
                "JWT audience is required.");
        }

        _ = GetSigningKey();
    }
}