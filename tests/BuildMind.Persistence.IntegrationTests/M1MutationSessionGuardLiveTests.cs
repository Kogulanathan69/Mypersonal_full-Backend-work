using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Persistence.IntegrationTests;

public sealed class M1MutationSessionGuardLiveTests
{
    [Fact]
    public async Task D07_account_and_session_version_are_checked_under_lock()
    {
        await using var db = await DisposableMySqlDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(options, db.ConnectionString, 30);
        await using var setup = new BuildMindDbContext(options.Options);
        await setup.Database.MigrateAsync();
        var id = Guid.NewGuid();
        var email = $"{id:N}@example.invalid";
        setup.Add(new UserRecord { Id = id, Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "synthetic", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        await setup.SaveChangesAsync();

        await using var transaction = await setup.Database.BeginTransactionAsync();
        var guard = new M1MutationSessionGuard(setup);
        Assert.True(await guard.LockAndCheckAsync(id, 1, default));
        Assert.False(await guard.LockAndCheckAsync(id, 2, default));
        Assert.False(await guard.LockAndCheckAsync(Guid.NewGuid(), 1, default));
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task D07_disabled_account_and_revoked_version_are_denied()
    {
        await using var db = await DisposableMySqlDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(options, db.ConnectionString, 30);
        await using var context = new BuildMindDbContext(options.Options);
        await context.Database.MigrateAsync();
        var id = Guid.NewGuid();
        var email = $"{id:N}@example.invalid";
        context.Add(new UserRecord { Id = id, Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "synthetic", IsActive = false, CreatedAtUtc = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();

        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            var guard = new M1MutationSessionGuard(context);
            Assert.False(await guard.LockAndCheckAsync(id, 1, default));
            await tx.CommitAsync();
        }

        await context.Set<UserRecord>().Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true).SetProperty(u => u.TokenVersion, 2L));

        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            var guard = new M1MutationSessionGuard(context);
            Assert.False(await guard.LockAndCheckAsync(id, 1, default));
            Assert.True(await guard.LockAndCheckAsync(id, 2, default));
            await tx.CommitAsync();
        }
    }

    [Fact]
    public async Task D07_project_mutation_authorization_denies_revoked_token_version_in_transaction()
    {
        await using var db = await DisposableMySqlDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(options, db.ConnectionString, 30);
        await using var context = new BuildMindDbContext(options.Options);
        await context.Database.MigrateAsync();

        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var email = $"{ownerId:N}@example.invalid";
        context.Add(new UserRecord { Id = ownerId, Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "synthetic", IsActive = true, TokenVersion = 1, CreatedAtUtc = DateTimeOffset.UtcNow });
        context.Add(new ProjectRecord { Id = projectId, OwnerId = ownerId, Name = "M1 Guard Project", AuthorizationVersion = 1, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();

        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            var auth = new ProjectMutationAuthorization(context);
            Assert.True(await auth.LockAndCheckAsync(projectId, ownerId, 1, default, expectedTokenVersion: 1));
            await tx.CommitAsync();
        }

        // Simulate concurrent logout-all / token_version bump while project authorization_version is unchanged.
        await context.Set<UserRecord>().Where(u => u.Id == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, 2L));

        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            var auth = new ProjectMutationAuthorization(context);
            Assert.False(await auth.LockAndCheckAsync(projectId, ownerId, 1, default, expectedTokenVersion: 1));
            Assert.True(await auth.LockAndCheckAsync(projectId, ownerId, 1, default, expectedTokenVersion: 2));
            await tx.CommitAsync();
        }
    }
}
