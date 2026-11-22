using System.Collections.Concurrent;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Domain.Entities;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>
/// In-memory stand-in for the M1 MySQL repositories, used only to host the real API composition root in tests
/// without a database. Persistence semantics (atomic SQL, constraints) are covered by the MySQL live tests.
/// </summary>
public sealed class InMemoryM1Store
    : IUserRepository, IUserProfileRepository, IProjectRepository, IUserSessionStore, IRefreshTokenStore
{
    private readonly ConcurrentDictionary<Guid, User> _users = new();
    private readonly ConcurrentDictionary<Guid, Project> _projects = new();
    private readonly object _gate = new();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Values.SingleOrDefault(u => u.NormalizedEmail == normalizedEmail));

    public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Values.Any(u => u.NormalizedEmail == normalizedEmail));

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_users.Values.Any(u => u.NormalizedEmail == user.NormalizedEmail))
            {
                throw new DuplicateEmailException();
            }

            _users[user.Id] = user;
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    /// <summary>Test helper: replaces a user's active flag WITHOUT touching the token version.</summary>
    public void SetActive(Guid userId, bool isActive) => Replace(userId, isActive, null);

    /// <summary>When set, account-state reads throw (simulates a database outage).</summary>
    public Exception? StateReadFailure { get; set; }

    private void Replace(Guid userId, bool? isActive, long? tokenVersion)
    {
        lock (_gate)
        {
            var u = _users[userId];
            _users[userId] = User.Restore(u.Id, u.Email, u.NormalizedEmail, u.PasswordHash, u.FullName, u.PhoneNumber,
                isActive ?? u.IsActive, u.CreatedAtUtc, tokenVersion ?? u.TokenVersion);
        }
    }

    // ----- IUserSessionStore (same observable semantics as the MySQL implementation) -----

    public Task<UserSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (StateReadFailure is not null)
            throw StateReadFailure;
        return Task.FromResult(_users.TryGetValue(userId, out var u) ? new UserSecurityState(u.IsActive, u.TokenVersion) : null);
    }

    public Task<bool> RevokeAllSessionsAsync(Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var u))
                return Task.FromResult(false);
            Replace(userId, null, u.TokenVersion + 1);
            RevokeWhere(t => t.UserId == userId, nowUtc, reason);
            return Task.FromResult(true);
        }
    }

    public Task<bool> DisableAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var u))
                return Task.FromResult(false);
            Replace(userId, false, u.TokenVersion + 1);
            RevokeWhere(t => t.UserId == userId, nowUtc, "account_disabled");
            return Task.FromResult(true);
        }
    }

    // ----- IRefreshTokenStore -----

    public sealed class StoredRefreshToken
    {
        public required NewRefreshToken Token { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public DateTimeOffset? ConsumedAtUtc { get; set; }
        public DateTimeOffset? RevokedAtUtc { get; set; }
        public string? Reason { get; set; }
        public Guid UserId => Token.UserId;
    }

    public List<StoredRefreshToken> RefreshTokens { get; } = [];

    public Task CreateAsync(NewRefreshToken token, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            RefreshTokens.Add(new StoredRefreshToken { Token = token, ExpiresAtUtc = token.ExpiresAtUtc });
        }

        return Task.CompletedTask;
    }

    public Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, Guid replacementId, string replacementTokenHash,
        DateTimeOffset nowUtc, TimeSpan slidingLifetime, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var current = RefreshTokens.SingleOrDefault(t => t.Token.TokenHash == presentedTokenHash);
            if (current is null || current.RevokedAtUtc is not null)
                return Task.FromResult(new RefreshRotationResult(RefreshRotationStatus.Invalid));
            if (current.ConsumedAtUtc is not null)
            {
                RevokeWhere(t => t.Token.FamilyId == current.Token.FamilyId, nowUtc, "replay_detected");
                Replace(current.UserId, null, _users[current.UserId].TokenVersion + 1);
                return Task.FromResult(new RefreshRotationResult(RefreshRotationStatus.ReplayDetected, current.UserId));
            }

            if (current.ExpiresAtUtc <= nowUtc || current.Token.AbsoluteExpiresAtUtc <= nowUtc)
                return Task.FromResult(new RefreshRotationResult(RefreshRotationStatus.Expired));

            current.ConsumedAtUtc = nowUtc;
            var expires = nowUtc.Add(slidingLifetime);
            if (expires > current.Token.AbsoluteExpiresAtUtc)
                expires = current.Token.AbsoluteExpiresAtUtc;
            RefreshTokens.Add(new StoredRefreshToken
            {
                Token = current.Token with { Id = replacementId, TokenHash = replacementTokenHash, CreatedAtUtc = nowUtc, ExpiresAtUtc = expires },
                ExpiresAtUtc = expires,
            });
            return Task.FromResult(new RefreshRotationResult(RefreshRotationStatus.Rotated, current.UserId, expires));
        }
    }

    public Task<bool> RevokeFamilyAsync(string presentedTokenHash, Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var current = RefreshTokens.SingleOrDefault(t => t.Token.TokenHash == presentedTokenHash && t.UserId == userId);
            if (current is null)
                return Task.FromResult(false);
            RevokeWhere(t => t.Token.FamilyId == current.Token.FamilyId, nowUtc, reason);
            return Task.FromResult(true);
        }
    }

    private void RevokeWhere(Func<StoredRefreshToken, bool> predicate, DateTimeOffset nowUtc, string reason)
    {
        foreach (var t in RefreshTokens.Where(t => t.RevokedAtUtc is null && predicate(t)))
        {
            t.RevokedAtUtc = nowUtc;
            t.Reason = reason;
        }
    }

    Task<ProfileResponse?> IUserProfileRepository.GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.TryGetValue(userId, out var u) ? Profile(u) : null);

    Task<ProfileResponse?> IUserProfileRepository.UpdateAsync(
        Guid userId, string fullName, string? phoneNumber, CancellationToken cancellationToken)
    {
        if (!_users.TryGetValue(userId, out var u))
        {
            return Task.FromResult<ProfileResponse?>(null);
        }

        u.FullName = fullName;
        u.PhoneNumber = phoneNumber;
        return Task.FromResult<ProfileResponse?>(Profile(u));
    }

    Task<Project?> IProjectRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<Project?> GetByIdAndOwnerAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.TryGetValue(id, out var p) && p.OwnerId == ownerId ? p : null);

    public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>(_projects.Values.Where(p => p.OwnerId == ownerId).ToArray());

    public Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        _projects[project.Id] = project;
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(Project project, Guid ownerId, long expectedRevision, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(project.Id, out var current) || current.OwnerId != ownerId
                || current.Revision != expectedRevision || project.Revision != expectedRevision + 1)
            {
                return Task.FromResult(false);
            }

            _projects[project.Id] = project;
            return Task.FromResult(true);
        }
    }

    private static ProfileResponse Profile(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        PhoneNumber = u.PhoneNumber,
        IsActive = u.IsActive,
        CreatedAtUtc = u.CreatedAtUtc,
    };
}
