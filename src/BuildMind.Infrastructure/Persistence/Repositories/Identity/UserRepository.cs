using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Application.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Identity;

public sealed class UserRepository : IUserRepository
{
    private readonly BuildMindDbContext _context;

    public UserRepository(BuildMindDbContext context)
    {
        _context = context
            ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<User?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var record = await _context.Set<UserRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.Id == id,
                cancellationToken);

        return record is null ? null : MapToDomain(record);
    }

    public async Task<User?> GetByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var record = await _context.Set<UserRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.NormalizedEmail == normalizedEmail,
                cancellationToken);

        return record is null ? null : MapToDomain(record);
    }

    public Task<bool> ExistsByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        return _context.Set<UserRecord>()
            .AnyAsync(
                u => u.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    public async Task AddAsync(
    User user,
    CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var record = new UserRecord
        {
            Id = user.Id,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail,
            PasswordHash = user.PasswordHash,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            TokenVersion = user.TokenVersion,
            CreatedAtUtc = user.CreatedAtUtc
        };

        await _context.Set<UserRecord>()
            .AddAsync(record, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            var failure =
                PersistenceErrorClassifier.Classify(exception);

            if (failure.Kind == PersistenceFailureKind.DuplicateKey &&
                string.Equals(
                    failure.ConstraintName,
                    "ux_users_normalized_email",
                    StringComparison.OrdinalIgnoreCase))
            {
                _context.Entry(record).State = EntityState.Detached;

                throw new DuplicateEmailException();
            }

            throw;
        }
    }

    public async Task UpdateAsync(
        User user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var record = await _context.Set<UserRecord>()
            .SingleOrDefaultAsync(
                u => u.Id == user.Id,
                cancellationToken);

        if (record is null)
        {
            throw new InvalidOperationException(
                "User record was not found.");
        }

        // Profile fields only. Password changes go through IPasswordResetRepository, which also increments the
        // token version atomically; writing a (possibly stale) hash here could silently undo a reset.
        record.FullName = user.FullName;
        record.PhoneNumber = user.PhoneNumber;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static User MapToDomain(UserRecord record)
    {
        return User.Restore(
            record.Id,
            record.Email,
            record.NormalizedEmail,
            record.PasswordHash,
            record.FullName,
            record.PhoneNumber,
            record.IsActive,
            record.CreatedAtUtc,
            record.TokenVersion);
    }
}