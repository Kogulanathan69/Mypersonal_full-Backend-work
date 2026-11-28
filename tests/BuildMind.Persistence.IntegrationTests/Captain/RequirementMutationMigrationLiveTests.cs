using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Requirements;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;

namespace BuildMind.Persistence.IntegrationTests.Captain;

public sealed class RequirementMutationMigrationLiveTests
{
    [Fact]
    public async Task M07_g12_rollback_preserves_requirement_history_and_head()
    {
        const string previous =
            "20261125103924_AddLandEdgeRoles";

        const string latest =
            "20261126173737_AddRequirementMutationReceipts";

        const string currentHead =
            "20261127153155_AddLandMetadataAndConstraints";

        await using var db = await SchemaAsync();

        var (owner, project) = await SeedAsync(db);

        var firstScope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        var secondScope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        RequirementMutation Mutation(Guid? expected, int bedrooms) =>
            new(RequirementMutationCategory.Full,
                new SaveRequirementsRequest(
                    expected, "synthetic-fixture-v1", 1,
                    [new("bedroom", bedrooms), new("kitchen", 1)]));

        RequirementRevision Revision(int number, int bedrooms) =>
            new(Guid.NewGuid(), project, number,
                "requirements/1", "synthetic-fixture-v1",
                new RequirementContent(1,
                    [new RoomCount("bedroom", bedrooms),
                     new RoomCount("kitchen", 1)]),
                Now.AddMinutes(number));

        var first = Revision(1, 2);
        var second = Revision(2, 3);

        await using (var writer = Context(db))
        {
            var tx = Transactions(writer);

            var store = new RequirementMutationStore(
                writer, tx,
                new RequirementRevisionRepository(writer, tx));

            var initial = await store.CommitAsync(
                firstScope, Mutation(null, 2), first, default);

            Assert.Equal(
                RequirementMutationStatus.Committed,
                initial.Status);

            var next = await store.CommitAsync(
                secondScope, Mutation(first.Id, 3),
                second, default);

            Assert.Equal(
                RequirementMutationStatus.Committed,
                next.Status);
        }

        var before = await ReadSnapshotAsync(db);

        Assert.Equal(2, before.Revisions.Length);
        Assert.Equal(first.Id, before.Revisions[0].Id);
        Assert.Equal(second.Id, before.Revisions[1].Id);
        Assert.Equal(project, before.Head.ProjectId);
        Assert.Equal(second.Id, before.Head.CurrentRevisionId);
        Assert.Equal(2L, before.Head.CurrentRevisionNumber);

        string[] migrationHistory;

        await using (var observer = Context(db))
        {
            migrationHistory = (await observer.Database
                .GetAppliedMigrationsAsync()).ToArray();

            Assert.Equal(18, migrationHistory.Length);
            Assert.Equal(previous, migrationHistory[^3]);
            Assert.Equal(latest, migrationHistory[^2]);
            Assert.Equal(currentHead, migrationHistory[^1]);

            var receipts = await observer
                .Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync();

            Assert.Equal(2, receipts.Count);

            Assert.Contains(receipts, r =>
                r.OperationId == firstScope.OperationId &&
                r.SavedRevisionId == first.Id);

            Assert.Contains(receipts, r =>
                r.OperationId == secondScope.OperationId &&
                r.SavedRevisionId == second.Id);
        }

        await using (var rollback = Context(db))
        {
            await rollback.GetService<IMigrator>()
                .MigrateAsync(previous);

            Assert.Equal(migrationHistory[..^2],
                await rollback.Database.GetAppliedMigrationsAsync());

            var receiptTableCount = await rollback.Database
                .SqlQuery<int>($"""
                    SELECT CAST(COUNT(*) AS SIGNED) AS Value
                    FROM information_schema.tables
                    WHERE table_schema = DATABASE()
                    AND table_name = 'requirement_mutation_receipts'
                    """).ToListAsync();

            Assert.Equal(0, Assert.Single(receiptTableCount));
        }

        var afterRollback = await ReadSnapshotAsync(db);

        Assert.Equal(before.Revisions, afterRollback.Revisions);
        Assert.Equal(before.Head, afterRollback.Head);

        await using (var reapply = Context(db))
        {
            await reapply.GetService<IMigrator>()
                .MigrateAsync(currentHead);

            Assert.Equal(migrationHistory,
                await reapply.Database.GetAppliedMigrationsAsync());

            Assert.Empty(await reapply.Database
                .GetPendingMigrationsAsync());

            Assert.False(reapply.Database.HasPendingModelChanges());
        }

        var afterReapply = await ReadSnapshotAsync(db);

        Assert.Equal(before.Revisions, afterReapply.Revisions);
        Assert.Equal(before.Head, afterReapply.Head);

        await using var final = Context(db);

        Assert.Empty(await final
            .Set<RequirementMutationReceiptRecord>()
            .AsNoTracking().ToListAsync());

        Assert.True(await final.Set<UserRecord>()
            .AnyAsync(u => u.Id == owner));

        Assert.True(await final.Set<ProjectRecord>()
            .AnyAsync(p => p.Id == project &&
                           p.OwnerId == owner &&
                           p.AuthorizationVersion == 1));

        var persisted = await new RequirementRevisionRepository(
            final, Transactions(final))
            .GetCurrentAsync(project, default);

        Assert.NotNull(persisted);
        Assert.Equal(second.Id, persisted!.Id);

        Assert.Equal(second.Content.GroundFloorRooms,
            persisted.Content.GroundFloorRooms);
    }

    private static async Task<(
        RevisionSnapshot[] Revisions,
        HeadSnapshot Head)> ReadSnapshotAsync(
            DisposableMySqlDatabase db)
    {
        await using var context = Context(db);

        var rows = await context.Set<RequirementRevisionRecord>()
            .AsNoTracking()
            .OrderBy(r => r.RevisionNumber)
            .ToListAsync();

        var revisions = rows.Select(r => new RevisionSnapshot(
            r.Id,
            r.ProjectId,
            r.RevisionNumber,
            r.SchemaVersion,
            r.CatalogueVersion,
            r.ContentJson,
            r.CreatedAtUtc)).ToArray();

        var head = Assert.Single(await context
            .Set<RequirementHeadRecord>()
            .AsNoTracking().ToListAsync());

        return (revisions, new HeadSnapshot(
            head.ProjectId,
            head.CurrentRevisionId,
            head.CurrentRevisionNumber));
    }

    private sealed record RevisionSnapshot(
        Guid Id,
        Guid ProjectId,
        long RevisionNumber,
        string SchemaVersion,
        string CatalogueVersion,
        string ContentJson,
        DateTimeOffset CreatedAtUtc);

    private sealed record HeadSnapshot(
        Guid ProjectId,
        Guid CurrentRevisionId,
        long CurrentRevisionNumber);
}
