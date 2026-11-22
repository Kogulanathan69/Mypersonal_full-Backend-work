namespace BuildMind.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public required string Email { get; init; }

    public required string NormalizedEmail { get; init; }

    public required string PasswordHash { get; set; }

    public string? FullName { get; set; }

    public string? PhoneNumber { get; set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Session generation: access tokens carry it and are rejected once it changes (password reset, disabling,
    /// logout-all). Only persistence increments it, atomically.
    /// </summary>
    public long TokenVersion { get; private set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; init; }
        = DateTimeOffset.UtcNow;

    public static User Restore(
        Guid id,
        string email,
        string normalizedEmail,
        string passwordHash,
        string? fullName,
        string? phoneNumber,
        bool isActive,
        DateTimeOffset createdAtUtc,
        long tokenVersion = 1)
    {
        if (id == Guid.Empty)
            throw new ArgumentException(
                "User ID cannot be empty.", nameof(id));

        if (tokenVersion < 1)
            throw new ArgumentOutOfRangeException(
                nameof(tokenVersion), "A token version must be positive.");

        return new User
        {
            Id = id,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHash,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            TokenVersion = tokenVersion
        };
    }
}
