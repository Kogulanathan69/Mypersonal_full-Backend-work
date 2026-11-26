using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using BuildMind.Infrastructure.Persistence.Transactions;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using BuildMind.API.Security;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Infrastructure.CaptainIntegration;
using BuildMind.Infrastructure.Identity;
using System.Data.Common;
using System.Security.Claims;
using MySql.Data.MySqlClient;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// STEP 05 decisions 1-3 and 7 on a disposable MySQL 8.4 database (synthetic data only): atomic token-version
/// increments, refresh-token rotation and replay (including concurrent use), and single-use password reset.
/// The schema comes only from the committed migrations.
/// </summary>
public sealed class SessionSecurityLiveTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero).AddTicks(123_450);

    [Fact]
    public async Task S01_concurrent_revocations_never_lose_a_token_version_increment()
    {
        await using var db = await SchemaAsync();
        var userId = await SeedUserAsync(db);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = Context(db);
            Assert.True(await Sessions(context).RevokeAllSessionsAsync(userId, Now, "test", default));
        }));

        await using var check = Context(db);
        Assert.Equal(new UserSecurityState(true, 9), await Sessions(check).GetAsync(userId, default));
        Assert.False(await Sessions(check).RevokeAllSessionsAsync(Guid.NewGuid(), Now, "test", default));
    }

    [Fact]
    public async Task S02_rotation_then_replay_revokes_the_family_and_the_access_tokens()
    {
        await using var db = await SchemaAsync();
        var userId = await SeedUserAsync(db);
        var first = await SeedRefreshAsync(db, userId, "A");

        await using (var context = Context(db))
        {
            var rotated = await Refresh(context).RotateAsync(first.TokenHash, Guid.NewGuid(), Hash("B"), Now, TimeSpan.FromDays(7), default);
            Assert.Equal(RefreshRotationStatus.Rotated, rotated.Status);
            Assert.Equal(userId, rotated.UserId);
        }

        await using (var context = Context(db))
        {
            var replay = await Refresh(context).RotateAsync(first.TokenHash, Guid.NewGuid(), Hash("C"), Now, TimeSpan.FromDays(7), default);
            Assert.Equal(RefreshRotationStatus.ReplayDetected, replay.Status);
            var second = await Refresh(context).RotateAsync(Hash("B"), Guid.NewGuid(), Hash("D"), Now, TimeSpan.FromDays(7), default);
            Assert.Equal(RefreshRotationStatus.Invalid, second.Status);  // the replacement was revoked with its family
        }

        await using var check = Context(db);
        Assert.Equal(2L, (await Sessions(check).GetAsync(userId, default))!.TokenVersion);
        Assert.All(await check.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync(),
            t => Assert.Equal("replay_detected", t.RevocationReason));
    }

    [Fact]
    public async Task S03_concurrent_use_of_one_refresh_token_has_exactly_one_winner()
    {
        await using var db = await SchemaAsync();
        var userId = await SeedUserAsync(db);
        var token = await SeedRefreshAsync(db, userId, "A");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            await using var context = Context(db);
            return (await Refresh(context).RotateAsync(token.TokenHash, Guid.NewGuid(), Hash("R" + i), Now, TimeSpan.FromDays(7), default)).Status;
        }));

        Assert.Equal(1, outcomes.Count(s => s == RefreshRotationStatus.Rotated));
        Assert.All(outcomes.Where(s => s != RefreshRotationStatus.Rotated),
            s => Assert.True(s is RefreshRotationStatus.ReplayDetected or RefreshRotationStatus.Invalid));
        await using var check = Context(db);
        Assert.DoesNotContain(await check.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync(), t => t.RevokedAtUtc == null);
    }

    [Fact]
    public async Task S04_expiry_and_absolute_lifetime_are_enforced()
    {
        await using var db = await SchemaAsync();
        var userId = await SeedUserAsync(db);
        var expired = await SeedRefreshAsync(db, userId, "old", expiresAt: Now.AddSeconds(-1));
        var capped = await SeedRefreshAsync(db, userId, "cap", absoluteExpiresAt: Now.AddDays(2));

        await using var context = Context(db);
        Assert.Equal(RefreshRotationStatus.Expired,
            (await Refresh(context).RotateAsync(expired.TokenHash, Guid.NewGuid(), Hash("x"), Now, TimeSpan.FromDays(7), default)).Status);
        var rotated = await Refresh(context).RotateAsync(capped.TokenHash, Guid.NewGuid(), Hash("y"), Now, TimeSpan.FromDays(7), default);
        Assert.Equal(Now.AddDays(2), rotated.ExpiresAtUtc);   // never beyond the absolute session limit
    }

    [Fact]
    public async Task S05_password_reset_is_single_use_and_revokes_every_session()
    {
        await using var db = await SchemaAsync();
        var userId = await SeedUserAsync(db);
        await SeedRefreshAsync(db, userId, "session");

        await using (var context = Context(db))
        {
            Assert.True(await Resets(context).ReplaceTokenAsync(new PasswordResetTokenRecord(userId, Hash("reset"), Now, Now.AddMinutes(15)), default));
        }

        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(async i =>
        {
            await using var context = Context(db);
            return await Resets(context).ResetPasswordAtomicallyAsync(userId, Hash("reset"), "new-hash-" + i, Now, default);
        }));

        Assert.Equal(1, attempts.Count(ok => ok));
        await using var check = Context(db);
        var user = await check.Set<UserRecord>().AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.StartsWith("new-hash-", user.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(2L, user.TokenVersion);
        Assert.All(await check.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync(), t => Assert.Equal("password_reset", t.RevocationReason));
    }

    [Fact]
    public async Task S06_expired_wrong_or_inactive_resets_change_nothing()
    {
        await using var db = await SchemaAsync();
        var userId = await SeedUserAsync(db);
        var inactiveId = await SeedUserAsync(db, isActive: false);

        await using (var context = Context(db))
        {
            await Resets(context).ReplaceTokenAsync(new PasswordResetTokenRecord(userId, Hash("expired"), Now.AddHours(-1), Now.AddSeconds(-1)), default);
            Assert.False(await Resets(context).ReplaceTokenAsync(new PasswordResetTokenRecord(inactiveId, Hash("inactive"), Now, Now.AddMinutes(15)), default));
            context.Add(new PasswordResetRecord { UserId = inactiveId, TokenHash = Hash("inactive"), CreatedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(15) });
            await context.SaveChangesAsync();
        }

        await using (var context = Context(db))
        {
            Assert.False(await Resets(context).ResetPasswordAtomicallyAsync(userId, Hash("expired"), "x", Now, default));
            Assert.False(await Resets(context).ResetPasswordAtomicallyAsync(userId, Hash("wrong"), "x", Now, default));
            Assert.False(await Resets(context).ResetPasswordAtomicallyAsync(inactiveId, Hash("inactive"), "x", Now, default));
        }

        await using var check = Context(db);
        Assert.All(await check.Set<UserRecord>().AsNoTracking().ToListAsync(), u =>
        {
            Assert.Equal("synthetic-hash", u.PasswordHash);
            Assert.Equal(1L, u.TokenVersion);
        });
        // A token issued before disable remains unconsumed.
        Assert.Null((await check.Set<PasswordResetRecord>().AsNoTracking().SingleAsync(r => r.UserId == inactiveId)).ConsumedAtUtc);
    }

    [Fact]
    public async Task S07_disable_and_family_revocation_are_scoped()
    {
        await using var db = await SchemaAsync();
        var alice = await SeedUserAsync(db);
        var bob = await SeedUserAsync(db);
        var aliceToken = await SeedRefreshAsync(db, alice, "alice");
        await SeedRefreshAsync(db, bob, "bob");

        await using (var context = Context(db))
        {
            Assert.False(await Refresh(context).RevokeFamilyAsync(aliceToken.TokenHash, bob, Now, "logout", default)); // not Bob's
            Assert.True(await Sessions(context).DisableAsync(alice, Now, default));
        }

        await using var check = Context(db);
        Assert.Equal(new UserSecurityState(false, 2), await Sessions(check).GetAsync(alice, default));
        Assert.Equal(new UserSecurityState(true, 1), await Sessions(check).GetAsync(bob, default));
        Assert.Null((await check.Set<RefreshTokenRecord>().AsNoTracking().SingleAsync(t => t.UserId == bob)).RevokedAtUtc);
    }

    [Fact]
    public async Task S08_mixed_same_family_rotation_replay_logout_and_logout_all_never_deadlock()
    {
        // Captain review (lock order users -> tokens): different tokens of one family (consumed T1, current T2) are
        // presented concurrently together with logout (family) and logout-all (user). Repeated to expose ordering bugs.
        for (var round = 0; round < 8; round++)
        {
            await using var db = await SchemaAsync();
            var userId = await SeedUserAsync(db);
            var t1 = await SeedRefreshAsync(db, userId, $"family-{round}");
            var t2Hash = Hash($"family-{round}-next");
            await using (var setup = Context(db))
                Assert.Equal(RefreshRotationStatus.Rotated,
                    (await Refresh(setup).RotateAsync(t1.TokenHash, Guid.NewGuid(), t2Hash, Now, TimeSpan.FromDays(7), default)).Status);

            var operations = new List<Func<Task>>
            {
                () => WithContext(db, c => Refresh(c).RotateAsync(t2Hash, Guid.NewGuid(), Hash($"r{round}a"), Now, TimeSpan.FromDays(7), default)),
                () => WithContext(db, c => Refresh(c).RotateAsync(t2Hash, Guid.NewGuid(), Hash($"r{round}b"), Now, TimeSpan.FromDays(7), default)),
                () => WithContext(db, c => Refresh(c).RotateAsync(t1.TokenHash, Guid.NewGuid(), Hash($"r{round}c"), Now, TimeSpan.FromDays(7), default)),
                () => WithContext(db, c => Refresh(c).RevokeFamilyAsync(t2Hash, userId, Now, "logout", default)),
                () => WithContext(db, c => Sessions(c).RevokeAllSessionsAsync(userId, Now, "logout_all", default)),
            };
            await Task.WhenAll(operations.Select(op => Task.Run(op)));  // any deadlock surfaces as an exception here

            await using var check = Context(db);
            // Revocation semantics preserved: replay of T1 and logout-all both revoke, so nothing stays usable.
            Assert.DoesNotContain(await check.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync(), t => t.RevokedAtUtc == null);
            // logout-all always bumps once; the T1 replay bumps again only if it observed T1 before revocation.
            Assert.InRange((await Sessions(check).GetAsync(userId, default))!.TokenVersion, 2L, 3L);
        }
    }

    [Fact]
    public async Task S09_rotation_racing_logout_all_and_password_reset_never_deadlocks()
    {
        for (var round = 0; round < 8; round++)
        {
            await using var db = await SchemaAsync();
            var userId = await SeedUserAsync(db);
            var token = await SeedRefreshAsync(db, userId, $"reset-{round}");
            var resetHash = Hash($"reset-token-{round}");
            await using (var setup = Context(db))
                await Resets(setup).ReplaceTokenAsync(new PasswordResetTokenRecord(userId, resetHash, Now.AddMinutes(-1), Now.AddHours(1)), default);

            await Task.WhenAll(
                Task.Run(() => WithContext(db, c => Refresh(c).RotateAsync(token.TokenHash, Guid.NewGuid(), Hash($"n{round}"), Now, TimeSpan.FromDays(7), default))),
                Task.Run(() => WithContext(db, c => Sessions(c).RevokeAllSessionsAsync(userId, Now, "logout_all", default))),
                Task.Run(() => WithContext(db, c => Resets(c).ResetPasswordAtomicallyAsync(userId, resetHash, "synthetic-new-hash", Now, default))));

            await using var check = Context(db);
            Assert.DoesNotContain(await check.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync(), t => t.RevokedAtUtc == null);
            Assert.Equal(3L, (await Sessions(check).GetAsync(userId, default))!.TokenVersion); // logout-all + reset
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S10_matching_family_ids_remain_isolated(bool replay)
    {
        await using var db = await SchemaAsync();
        var alice = await SeedUserAsync(db);
        var bob = await SeedUserAsync(db);
        var first = await SeedRefreshAsync(db, alice, "first");
        await using var c = Context(db);
        await Refresh(c).CreateAsync(first with { Id = Guid.NewGuid(), UserId = bob, TokenHash = Hash("second") }, default);
        if (replay)
        {
            var rotated = await Refresh(c).RotateAsync(first.TokenHash, Guid.NewGuid(), Hash("next"), Now, TimeSpan.FromDays(7), default);
            Assert.Equal(RefreshRotationStatus.Rotated, rotated.Status);
            var repeated = await Refresh(c).RotateAsync(first.TokenHash, Guid.NewGuid(), Hash("unused"), Now, TimeSpan.FromDays(7), default);
            Assert.Equal(RefreshRotationStatus.ReplayDetected, repeated.Status);
        }
        else
        {
            Assert.True(await Refresh(c).RevokeFamilyAsync(first.TokenHash, alice, Now, "logout", default));
        }
        Assert.All(await c.Set<RefreshTokenRecord>().AsNoTracking().Where(t => t.UserId == alice).ToListAsync(), t => Assert.NotNull(t.RevokedAtUtc));
        Assert.Null((await c.Set<RefreshTokenRecord>().AsNoTracking().SingleAsync(t => t.UserId == bob)).RevokedAtUtc);
    }

    [Fact]
    public async Task S11_disabled_account_cannot_create_or_rotate()
    {
        await using var db = await SchemaAsync();
        var id = await SeedUserAsync(db);
        var existing = await SeedRefreshAsync(db, id, "existing");
        await using var c = Context(db);
        await c.Set<UserRecord>().Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        await Assert.ThrowsAsync<RefreshSessionRejectedException>(() => Refresh(c).CreateAsync(
            existing with { Id = Guid.NewGuid(), TokenHash = Hash("disabled") }, default));
        var rotation = await Refresh(c).RotateAsync(existing.TokenHash, Guid.NewGuid(), Hash("replacement"), Now, TimeSpan.FromDays(7), default);
        Assert.Equal(RefreshRotationStatus.Invalid, rotation.Status);
        Assert.Null((await c.Set<RefreshTokenRecord>().AsNoTracking().SingleAsync()).ConsumedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S12_stale_login_after_revocation_cannot_create_a_session(bool resetPassword)
    {
        await using var db = await SchemaAsync();
        var id = await SeedUserAsync(db);
        await using var c = Context(db);
        var login = new NewRefreshToken(Guid.NewGuid(), id, Guid.NewGuid(), Hash("stale"), Now, Now.AddDays(7), Now.AddDays(30), 1);
        if (resetPassword)
        {
            Assert.True(await Resets(c).ReplaceTokenAsync(new(id, Hash("reset"), Now, Now.AddMinutes(10)), default));
            Assert.True(await Resets(c).ResetPasswordAtomicallyAsync(id, Hash("reset"), "changed", Now, default));
        }
        else
        {
            Assert.True(await Sessions(c).RevokeAllSessionsAsync(id, Now, "logout_all", default));
        }
        await Assert.ThrowsAsync<RefreshSessionRejectedException>(() => Refresh(c).CreateAsync(login, default));
        Assert.False(await c.Set<RefreshTokenRecord>().AnyAsync());
    }

    [Fact]
    public async Task S13_rotation_returns_locked_account_version()
    {
        await using var db = await SchemaAsync();
        var id = await SeedUserAsync(db);
        await using var c = Context(db);
        Assert.True(await Sessions(c).RevokeAllSessionsAsync(id, Now, "logout_all", default));
        await Refresh(c).CreateAsync(new(Guid.NewGuid(), id, Guid.NewGuid(), Hash("v2"), Now, Now.AddDays(7), Now.AddDays(30), 2), default);
        var rotation = await Refresh(c).RotateAsync(Hash("v2"), Guid.NewGuid(), Hash("replacement"), Now, TimeSpan.FromDays(7), default);
        Assert.Equal(RefreshRotationStatus.Rotated, rotation.Status);
        Assert.Equal(2L, rotation.TokenVersion);
    }

    [Fact]
    public async Task AUTH_G01_Password_change_rejects_previously_verified_login()
    {
        await using var db = await SchemaAsync();
        var passwords = new PasswordHashService();
        const string oldPassword = "Synthetic-old-password-26!";
        const string newPassword = "Synthetic-new-password-26!";
        var id = await SeedUserAsync(db, passwordHash: passwords.HashPassword(oldPassword));
        var other = await SeedUserAsync(db);
        var otherSession = await SeedRefreshAsync(db, other, "unrelated");
        await using var c = Context(db);
        var users = new UserRepository(c);
        var auth = new AuthenticationService(users, passwords, new JwtAccessTokenIssuer(new JwtOptions
        {
            Issuer = "synthetic", Audience = "synthetic-api",
            SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        }, TimeProvider.System));
        var account = (await users.GetByIdAsync(id, default))!;
        var login = await auth.LoginAsync(new() { Email = account.Email, Password = oldPassword }, default);
        Assert.NotNull(login);
        var verifiedVersion = long.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(login.AccessToken).Claims.Single(claim => claim.Type == "tv").Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var security = new MySqlAccountSecurityStore(c, Runner(c));
        Assert.True(await security.ChangePasswordAsync(id, account.PasswordHash, passwords.HashPassword(newPassword), Now, default));
        Assert.Equal(new UserSecurityState(true, verifiedVersion + 1), await Sessions(c).GetAsync(id, default));
        var stale = new NewRefreshToken(Guid.NewGuid(), id, Guid.NewGuid(), Hash("obsolete"), Now, Now.AddDays(1), Now.AddDays(7), verifiedVersion);
        await Assert.ThrowsAsync<RefreshSessionRejectedException>(() => Refresh(c).CreateAsync(stale, default));
        var only = await c.Set<RefreshTokenRecord>().AsNoTracking().SingleAsync();
        Assert.Equal(otherSession.Id, only.Id);
        Assert.Null(only.RevokedAtUtc);
        var unaffected = (await users.GetByIdAsync(other, default))!;
        Assert.True(unaffected.IsActive);
        Assert.Equal(1L, unaffected.TokenVersion);
        Assert.Equal("synthetic-hash", unaffected.PasswordHash);
        Assert.Null(await auth.LoginAsync(new() { Email = account.Email, Password = oldPassword }, default));
        Assert.NotNull(await auth.LoginAsync(new() { Email = account.Email, Password = newPassword }, default));
    }

    [Fact]
    public async Task AUTH_G04_Captain_individual_revocation_is_owner_scoped()
    {
        await using var db = await SchemaAsync();
        var alice = await SeedUserAsync(db);
        var bob = await SeedUserAsync(db);
        var shared = await SeedRefreshAsync(db, alice, "alice");
        var bobOnly = await SeedRefreshAsync(db, bob, "private");
        await using var c = Context(db);
        var refresh = Refresh(c);
        var matching = shared with { Id = Guid.NewGuid(), UserId = bob, TokenHash = Hash("bob") };
        await refresh.CreateAsync(matching, default);
        var captain = new CaptainReadModels(c, TimeProvider.System, new SurveyBlobReader(new ConfigurationBuilder().Build()));
        Assert.False(await captain.RevokeSessionAsync(alice, bobOnly.FamilyId, default));
        Assert.True(await captain.RevokeSessionAsync(alice, shared.FamilyId, default));
        var rows = await c.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync();
        Assert.Equal("SESSION_REVOKED_BY_USER", rows.Single(t => t.Id == shared.Id).RevocationReason);
        Assert.All(rows.Where(t => t.UserId == bob), t => Assert.Null(t.RevokedAtUtc));
        foreach (var session in new[] { matching, bobOnly })
        {
            var rotation = await refresh.RotateAsync(session.TokenHash, Guid.NewGuid(), Hash(session.Id.ToString()), Now, TimeSpan.FromDays(7), default);
            Assert.Equal(RefreshRotationStatus.Rotated, rotation.Status);
            Assert.Equal(bob, rotation.UserId);
        }
        Assert.Equal(new UserSecurityState(true, 1), await Sessions(c).GetAsync(bob, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AUTH_G06_Create_and_revoke_respect_both_commit_orders(bool revokeFirst)
    {
        await using var db = await SchemaAsync();
        var id = await SeedUserAsync(db);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var gate = new CommitGate(ct);
        await using var first = Context(db, gate);
        await using var second = Context(db);
        var session = new NewRefreshToken(Guid.NewGuid(), id, Guid.NewGuid(), Hash("ordered"), Now, Now.AddDays(1), Now.AddDays(7), 1);
        async Task Revoke(BuildMindDbContext c) =>
            Assert.True(await Sessions(c).RevokeAllSessionsAsync(id, Now, "logout_all", ct));
        async Task AttemptStaleCreate() =>
            await Assert.ThrowsAsync<RefreshSessionRejectedException>(() => Refresh(second).CreateAsync(session, ct));
        var winner = revokeFirst ? Revoke(first) : Refresh(first).CreateAsync(session, ct);
        Task? follower = null;
        try
        {
            await Task.WhenAny(winner, gate.Ready.Task).WaitAsync(ct);
            if (winner.IsCompleted) await winner;
            Assert.True(gate.Ready.Task.IsCompletedSuccessfully);
            // NOWAIT proves another connection cannot acquire the user's row before this commit.
            await using (var probe = Context(db))
            await using (var transaction = await probe.Database.BeginTransactionAsync(ct))
            {
                var locked = await Assert.ThrowsAsync<MySqlException>(() => probe.Database.SqlQuery<int>(
                    $"SELECT 1 AS Value FROM users WHERE id = {id} FOR UPDATE NOWAIT").ToListAsync(ct));
                Assert.Equal(3572, locked.Number);
            }
            follower = revokeFirst ? AttemptStaleCreate() : Revoke(second);
        }
        finally
        {
            gate.Release.TrySetResult();
            await winner;
        }
        await follower!;
        await using var check = Context(db);
        Assert.Equal(new UserSecurityState(true, 2), await Sessions(check).GetAsync(id, ct));
        var rows = await check.Set<RefreshTokenRecord>().AsNoTracking().ToListAsync(ct);
        if (revokeFirst) Assert.Empty(rows);
        else
        {
            var row = Assert.Single(rows);
            Assert.Equal(session.Id, row.Id);
            Assert.NotNull(row.RevokedAtUtc);
            var rotation = await Refresh(check).RotateAsync(session.TokenHash, Guid.NewGuid(), Hash("invalid"), Now, TimeSpan.FromDays(1), ct);
            Assert.Equal(RefreshRotationStatus.Invalid, rotation.Status);
        }
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString()), new Claim("tv", "1")], "test"));
        await new ActiveSessionMiddleware(ctx =>
        {
            ctx.Response.StatusCode = ctx.User.Identity?.IsAuthenticated == true ? 200 : 401;
            return Task.CompletedTask;
        }).InvokeAsync(http, Sessions(check));
        Assert.Equal(401, http.Response.StatusCode);
    }

    private sealed class CommitGate(CancellationToken deadline) : DbTransactionInterceptor
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Ready.TrySetResult();
            await Release.Task.WaitAsync(deadline);
            return result;
        }
    }

    private static async Task WithContext<T>(DisposableMySqlDatabase db, Func<BuildMindDbContext, Task<T>> operation)
    {
        await using var context = Context(db);
        await operation(context);
    }

    private static string Hash(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));

    private static UserSessionStore Sessions(BuildMindDbContext c) => new(c, Runner(c));
    private static RefreshTokenStore Refresh(BuildMindDbContext c) => new(c, Runner(c));
    private static PasswordResetRepository Resets(BuildMindDbContext c) => new(c, Runner(c));
    private static TransactionRunner Runner(BuildMindDbContext c) => new(new DbContextTransactionSource(c));

    private static BuildMindDbContext Context(DisposableMySqlDatabase db, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, db.ConnectionString, 30);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new BuildMindDbContext(builder.Options);
    }

    private static async Task<DisposableMySqlDatabase> SchemaAsync()
    {
        var db = await DisposableMySqlDatabase.CreateAsync();
        try
        {
            await using var context = Context(db);
            Assert.Contains("20261120202215_AddSessionSecurity", context.Database.GetMigrations());
            await context.Database.MigrateAsync();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static async Task<Guid> SeedUserAsync(DisposableMySqlDatabase db, bool isActive = true, string passwordHash = "synthetic-hash")
    {
        await using var context = Context(db);
        var id = Guid.NewGuid();
        var email = $"{id:N}@example.invalid";
        context.Add(new UserRecord
        {
            Id = id, Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = passwordHash,
            IsActive = isActive, CreatedAtUtc = Now,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<NewRefreshToken> SeedRefreshAsync(DisposableMySqlDatabase db, Guid userId, string seed,
        DateTimeOffset? expiresAt = null, DateTimeOffset? absoluteExpiresAt = null)
    {
        var token = new NewRefreshToken(Guid.NewGuid(), userId, Guid.NewGuid(), Hash(seed), Now.AddMinutes(-5),
            expiresAt ?? Now.AddDays(7), absoluteExpiresAt ?? Now.AddDays(30), 1);
        await using var context = Context(db);
        await Refresh(context).CreateAsync(token, default);
        return token;
    }
}
