using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// STEP 05 decision 4 on disposable MySQL 8.4: the authorization version is persisted and mapped, increments are an
/// atomic compare-and-set, metadata edits never touch it, and M3's port returns owner-only snapshots bound to it
/// (an older snapshot becomes stale after an increment).
/// </summary>
public sealed class ProjectAuthorizationLiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task P01_version_round_trips_and_metadata_updates_leave_it_unchanged()
    {
        await using var db = await SchemaAsync();
        var (ownerId, projectId) = await SeedAsync(db);

        await using (var context = Context(db))
        {
            var repository = new ProjectRepository(context);
            var project = (await repository.GetByIdAsync(projectId, default))!;
            Assert.Equal(1, project.AuthorizationVersion);
            Assert.True(await repository.UpdateAsync(project.WithUpdatedDetails("renamed", null, null, Now), ownerId, 1, default));
        }

        await using var check = Context(db);
        var row = await check.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId);
        Assert.Equal(2, row.Revision);
        Assert.Equal(1, row.AuthorizationVersion);
    }

    [Fact]
    public async Task P02_concurrent_increments_with_the_same_expected_version_have_one_winner()
    {
        await using var db = await SchemaAsync();
        var (_, projectId) = await SeedAsync(db);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var context = Context(db);
            return await new ProjectAuthorizationVersionStore(context).TryIncrementAsync(projectId, 1, default);
        }));

        Assert.Single(results, r => r == 2);
        Assert.Equal(4, results.Count(r => r is null));
        await using var check = Context(db);
        Assert.Equal(2, (await check.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId)).AuthorizationVersion);
        Assert.Null(await new ProjectAuthorizationVersionStore(check).TryIncrementAsync(Guid.NewGuid(), 1, default));
    }

    [Fact]
    public async Task P03_port_authorizes_only_the_owner_and_old_snapshots_become_stale()
    {
        await using var db = await SchemaAsync();
        var (ownerId, projectId) = await SeedAsync(db);
        var (otherId, _) = await SeedAsync(db);

        await using var context = Context(db);
        var port = new ProjectOwnerAuthorizationPort(context);

        var before = await port.AuthorizeOwnerAsync(projectId, ownerId, default);
        Assert.Equal(ProjectAuthorizationStatus.Authorized, before.Status);
        Assert.Equal(new AuthorizationVersion(1), before.Snapshot!.Version);
        Assert.Equal(ProjectAuthorizationStatus.Denied, (await port.AuthorizeOwnerAsync(projectId, otherId, default)).Status);
        Assert.Null((await port.AuthorizeOwnerAsync(projectId, otherId, default)).Snapshot);
        Assert.Equal(ProjectAuthorizationStatus.Denied, (await port.AuthorizeOwnerAsync(Guid.NewGuid(), ownerId, default)).Status);

        Assert.Equal(2, await new ProjectAuthorizationVersionStore(context).TryIncrementAsync(projectId, 1, default));

        var after = await port.AuthorizeOwnerAsync(projectId, ownerId, default);
        Assert.Equal(new AuthorizationVersion(2), after.Snapshot!.Version);
        Assert.NotEqual(before.Snapshot.Version, after.Snapshot.Version);   // M3 rejects mutations bound to `before`
    }

    [Fact]
    public async Task P04_cross_owner_and_cross_project_requests_are_denied_without_disclosure()
    {
        await using var db = await SchemaAsync();
        var (ownerA, projectA) = await SeedAsync(db);
        var (ownerB, projectB) = await SeedAsync(db);
        await using var context = Context(db);
        var port = new ProjectOwnerAuthorizationPort(context);

        foreach (var (project, actor) in new[] { (projectB, ownerA), (projectA, ownerB) })
        {
            var denied = await port.AuthorizeOwnerAsync(project, actor, default);
            Assert.Equal(ProjectAuthorizationStatus.Denied, denied.Status);
            Assert.Null(denied.Snapshot);
        }

        foreach (var (project, actor) in new[] { (projectA, ownerA), (projectB, ownerB) })
        {
            var allowed = await port.AuthorizeOwnerAsync(project, actor, default);
            Assert.Equal(ProjectAuthorizationStatus.Authorized, allowed.Status);
            Assert.Equal(project, allowed.Snapshot!.ProjectId);
            Assert.Equal(actor, allowed.Snapshot.ActorId);
        }

        // Unknown project: same Denied shape as "exists but not yours" (no existence disclosure).
        var unknown = await port.AuthorizeOwnerAsync(Guid.NewGuid(), ownerA, default);
        Assert.Equal(ProjectAuthorizationStatus.Denied, unknown.Status);
        Assert.Null(unknown.Snapshot);
    }

    [Fact]
    public async Task P05_ownership_revocation_takes_effect_and_invalidates_earlier_snapshots()
    {
        await using var db = await SchemaAsync();
        var (oldOwner, projectId) = await SeedAsync(db);
        var (newOwner, _) = await SeedAsync(db);
        await using var context = Context(db);
        var port = new ProjectOwnerAuthorizationPort(context);
        var before = await port.AuthorizeOwnerAsync(projectId, oldOwner, default);
        Assert.Equal(ProjectAuthorizationStatus.Authorized, before.Status);

        // A committed ownership change records itself with an atomic increment in the SAME transaction (decision 2).
        await using (var change = Context(db))
        await using (var transaction = await change.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await change.Set<ProjectRecord>().Where(p => p.Id == projectId && p.OwnerId == oldOwner)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerId, newOwner)));
            Assert.Equal(2, await new ProjectAuthorizationVersionStore(change).TryIncrementAsync(projectId, 1, default));
            await transaction.CommitAsync();
        }

        var revoked = await port.AuthorizeOwnerAsync(projectId, oldOwner, default);
        Assert.Equal(ProjectAuthorizationStatus.Denied, revoked.Status);
        Assert.Null(revoked.Snapshot);
        var granted = await port.AuthorizeOwnerAsync(projectId, newOwner, default);
        Assert.Equal(new AuthorizationVersion(2), granted.Snapshot!.Version);
        Assert.NotEqual(before.Snapshot!.Version, granted.Snapshot.Version);
    }

    [Fact]
    public async Task P06_stale_expected_version_never_increments_and_reauthorization_reports_the_current_version()
    {
        await using var db = await SchemaAsync();
        var (ownerId, projectId) = await SeedAsync(db);
        await using var context = Context(db);
        var store = new ProjectAuthorizationVersionStore(context);
        var stale = (await new ProjectOwnerAuthorizationPort(context).AuthorizeOwnerAsync(projectId, ownerId, default)).Snapshot!;

        Assert.Equal(2, await store.TryIncrementAsync(projectId, 1, default));
        Assert.Null(await store.TryIncrementAsync(projectId, stale.Version.Value, default));   // stale expectation
        Assert.Null(await store.TryIncrementAsync(projectId, 5, default));                     // future expectation

        await using var check = Context(db);
        Assert.Equal(2, (await check.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId)).AuthorizationVersion);
        var current = await new ProjectOwnerAuthorizationPort(check).AuthorizeOwnerAsync(projectId, ownerId, default);
        Assert.Equal(new AuthorizationVersion(2), current.Snapshot!.Version);
        Assert.NotEqual(stale.Version, current.Snapshot.Version);
    }

    [Fact]
    public async Task P07_rolled_back_authorization_change_leaves_owner_and_version_unchanged()
    {
        await using var db = await SchemaAsync();
        var (ownerId, projectId) = await SeedAsync(db);
        var (otherId, _) = await SeedAsync(db);

        await using (var change = Context(db))
        await using (var transaction = await change.Database.BeginTransactionAsync())
        {
            await change.Set<ProjectRecord>().Where(p => p.Id == projectId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerId, otherId));
            Assert.Equal(2, await new ProjectAuthorizationVersionStore(change).TryIncrementAsync(projectId, 1, default));
            await transaction.RollbackAsync();
        }

        await using var check = Context(db);
        var row = await check.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId);
        Assert.Equal(1, row.AuthorizationVersion);
        Assert.Equal(ownerId, row.OwnerId);
        var port = new ProjectOwnerAuthorizationPort(check);
        Assert.Equal(new AuthorizationVersion(1), (await port.AuthorizeOwnerAsync(projectId, ownerId, default)).Snapshot!.Version);
        Assert.Equal(ProjectAuthorizationStatus.Denied, (await port.AuthorizeOwnerAsync(projectId, otherId, default)).Status);
    }

    [Fact]
    public async Task P08_concurrent_rounds_each_have_one_winner_and_increments_persist_monotonically()
    {
        await using var db = await SchemaAsync();
        var (_, projectId) = await SeedAsync(db);

        for (var expected = 1L; expected <= 3; expected++)
        {
            var round = expected;
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var context = Context(db);
                return await new ProjectAuthorizationVersionStore(context).TryIncrementAsync(projectId, round, default);
            }));
            Assert.Single(results, r => r == round + 1);
            Assert.Equal(7, results.Count(r => r is null));

            await using var check = Context(db);
            Assert.Equal(round + 1, (await check.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId)).AuthorizationVersion);
        }
    }

    [Fact]
    public async Task P09_a_row_with_a_non_positive_version_never_authorizes()
    {
        await using var db = await SchemaAsync();
        var (ownerId, projectId) = await SeedAsync(db);
        await using (var corrupt = Context(db))
        {
            await corrupt.Set<ProjectRecord>().Where(p => p.Id == projectId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AuthorizationVersion, 0L));
        }

        await using var context = Context(db);
        var result = await new ProjectOwnerAuthorizationPort(context).AuthorizeOwnerAsync(projectId, ownerId, default);
        Assert.Equal(ProjectAuthorizationStatus.Denied, result.Status);
        Assert.Null(result.Snapshot);
    }

    private static BuildMindDbContext Context(DisposableMySqlDatabase db)
    {
        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, db.ConnectionString, 30);
        return new BuildMindDbContext(builder.Options);
    }

    private static async Task<DisposableMySqlDatabase> SchemaAsync()
    {
        var db = await DisposableMySqlDatabase.CreateAsync();
        try
        {
            await using var context = Context(db);
            await context.Database.MigrateAsync();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static async Task<(Guid OwnerId, Guid ProjectId)> SeedAsync(DisposableMySqlDatabase db)
    {
        await using var context = Context(db);
        var ownerId = Guid.NewGuid();
        var email = $"{ownerId:N}@example.invalid";
        context.Add(new UserRecord { Id = ownerId, Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "synthetic-hash", IsActive = true, CreatedAtUtc = Now });
        await context.SaveChangesAsync();
        var project = new Project { OwnerId = ownerId, Name = "synthetic project", CreatedAtUtc = Now };
        await new ProjectRepository(context).AddAsync(project, default);
        return (ownerId, project.Id);
    }
}
