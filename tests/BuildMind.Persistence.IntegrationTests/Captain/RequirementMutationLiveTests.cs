using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Requirements;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using Microsoft.EntityFrameworkCore;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;

namespace BuildMind.Persistence.IntegrationTests.Captain;

public sealed class RequirementMutationLiveTests
{
    [Fact]
    public async Task G12_L01_First_mutation_persists_revision_head_and_receipt()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var operation = Guid.NewGuid();
        var scope = new RequirementMutationScope(
            project, owner, operation, 1);

        var mutation = new RequirementMutation(
            RequirementMutationCategory.Full,
            new SaveRequirementsRequest(
                null, "synthetic-fixture-v1", 1,
                [new("bedroom", 2), new("kitchen", 1)]));

        var proposal = new RequirementRevision(
            Guid.NewGuid(), project, 1,
            "requirements/1", "synthetic-fixture-v1",
            new RequirementContent(1,
                [new RoomCount("bedroom", 2),
                 new RoomCount("kitchen", 1)]),
            Now.AddMinutes(1));

        await using var writer = Context(db);
        var transactions = Transactions(writer);
        var store = new RequirementMutationStore(
            writer, transactions,
            new RequirementRevisionRepository(writer, transactions));

        var result = await store.CommitAsync(
            scope, mutation, proposal, default);

        Assert.Equal(RequirementMutationStatus.Committed, result.Status);
        Assert.NotNull(result.Revision);
        Assert.Equal(proposal.Id, result.Revision!.RevisionId);

        await using var observer = Context(db);

        var revision = Assert.Single(
            await observer.Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync());

        var head = Assert.Single(
            await observer.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        var receipt = Assert.Single(
            await observer.Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(proposal.Id, revision.Id);
        Assert.Equal(proposal.Id, head.CurrentRevisionId);
        Assert.Equal(1L, head.CurrentRevisionNumber);

        Assert.Equal(project, receipt.ProjectId);
        Assert.Equal(owner, receipt.ActorId);
        Assert.Equal(operation, receipt.OperationId);
        Assert.Equal(1L, receipt.AuthorizationVersion);
        Assert.Equal((byte)1, receipt.FormatVersion);
        Assert.Equal((byte)RequirementMutationCategory.Full, receipt.Category);
        Assert.Equal(proposal.Id, receipt.SavedRevisionId);

        var expectedDigest = RequirementMutationFingerprint.Compute(
            project, owner, operation, 1, null,
            RequirementMutationCanonicalPayload.Encode(mutation));

        Assert.Equal(expectedDigest, receipt.Digest);
    }

    [Theory]
    [InlineData("L02", RequirementMutationStatus.Committed)]
    [InlineData("L03", RequirementMutationStatus.Reused)]
    [InlineData("L04", RequirementMutationStatus.Absent)]
    public async Task G12_L02_L03_L04_Receipt_replay_and_observation(
        string scenario, RequirementMutationStatus expected)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        var scope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        static RequirementMutation Mutation(int bedrooms) =>
            new(RequirementMutationCategory.Full,
                new SaveRequirementsRequest(
                    null, "synthetic-fixture-v1", 1,
                    [new("bedroom", bedrooms), new("kitchen", 1)]));

        RequirementRevision Proposal(int bedrooms) =>
            new(Guid.NewGuid(), project, 1,
                "requirements/1", "synthetic-fixture-v1",
                new RequirementContent(1,
                    [new RoomCount("bedroom", bedrooms),
                     new RoomCount("kitchen", 1)]),
                Now.AddMinutes(1));

        var mutation = Mutation(2);
        var proposal = Proposal(2);

        await using (var writer = Context(db))
        {
            var tx = Transactions(writer);
            var store = new RequirementMutationStore(
                writer, tx,
                new RequirementRevisionRepository(writer, tx));

            var first = await store.CommitAsync(
                scope, mutation, proposal, default);

            Assert.Equal(RequirementMutationStatus.Committed, first.Status);
            Assert.Equal(proposal.Id, first.Revision!.RevisionId);
        }

        await using (var replay = Context(db))
        {
            var tx = Transactions(replay);
            var store = new RequirementMutationStore(
                replay, tx,
                new RequirementRevisionRepository(replay, tx));

            var result = scenario switch
            {
                "L02" => await store.CommitAsync(
                    scope, Mutation(2), Proposal(2), default),
                "L03" => await store.CommitAsync(
                    scope, Mutation(3), Proposal(3), default),
                "L04" => await store.ObserveAsync(
                    scope with { OperationId = Guid.NewGuid() },
                    null, default),
                _ => throw new InvalidOperationException(scenario)
            };

            Assert.Equal(expected, result.Status);
            Assert.Null(result.Failure);

            if (expected == RequirementMutationStatus.Committed)
            {
                Assert.NotNull(result.Revision);
                Assert.Equal(proposal.Id, result.Revision!.RevisionId);
                Assert.Equal(1L, result.Revision.RevisionNumber);
            }
            else
            {
                Assert.Null(result.Revision);
            }
        }

        await using var observer = Context(db);

        var revision = Assert.Single(
            await observer.Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync());

        var head = Assert.Single(
            await observer.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        var receipt = Assert.Single(
            await observer.Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(proposal.Id, revision.Id);
        Assert.Equal(proposal.Id, head.CurrentRevisionId);
        Assert.Equal(1L, head.CurrentRevisionNumber);
        Assert.Equal(proposal.Id, receipt.SavedRevisionId);
        Assert.Equal(scope.OperationId, receipt.OperationId);

        var persisted = await new RequirementRevisionRepository(
            observer, Transactions(observer))
            .GetCurrentAsync(project, default);

        Assert.NotNull(persisted);
        Assert.Equal(proposal.Content.GroundFloorRooms,
            persisted!.Content.GroundFloorRooms);
    }

    [Theory]
    [InlineData("L05")]
    [InlineData("L06")]
    public async Task G12_L05_L06_Authorization_denies_receipt_access(
        string scenario)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        var scope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        var mutation = new RequirementMutation(
            RequirementMutationCategory.Full,
            new SaveRequirementsRequest(
                null, "synthetic-fixture-v1", 1,
                [new("bedroom", 2), new("kitchen", 1)]));

        var proposal = new RequirementRevision(
            Guid.NewGuid(), project, 1,
            "requirements/1", "synthetic-fixture-v1",
            new RequirementContent(1,
                [new RoomCount("bedroom", 2),
                 new RoomCount("kitchen", 1)]),
            Now.AddMinutes(1));

        await using (var writer = Context(db))
        {
            var tx = Transactions(writer);
            var store = new RequirementMutationStore(
                writer, tx,
                new RequirementRevisionRepository(writer, tx));

            var first = await store.CommitAsync(
                scope, mutation, proposal, default);

            Assert.Equal(RequirementMutationStatus.Committed, first.Status);
            Assert.Equal(proposal.Id, first.Revision!.RevisionId);
        }

        RequirementMutationScope[] deniedScopes;

        if (scenario == "L05")
        {
            var (otherActor, _) = await SeedAsync(db);

            deniedScopes =
            [
                scope with { ActorId = otherActor }
            ];
        }
        else if (scenario == "L06")
        {
            await BumpAuthorizationVersionAsync(db, project, 1);

            deniedScopes =
            [
                scope,
                scope with { Version = 2 }
            ];
        }
        else
        {
            throw new InvalidOperationException(scenario);
        }

        foreach (var deniedScope in deniedScopes)
        {
            await using var context = Context(db);
            var tx = Transactions(context);
            var store = new RequirementMutationStore(
                context, tx,
                new RequirementRevisionRepository(context, tx));

            var observation = await store.ObserveAsync(
                deniedScope, null, default);

            Assert.Equal(
                RequirementMutationStatus.Denied, observation.Status);
            Assert.Null(observation.Revision);
            Assert.Null(observation.Failure);

            var replay = await store.CommitAsync(
                deniedScope, mutation, proposal, default);

            Assert.Equal(
                RequirementMutationStatus.Denied, replay.Status);
            Assert.Null(replay.Revision);
            Assert.Null(replay.Failure);
        }

        await using var observer = Context(db);

        var revision = Assert.Single(
            await observer.Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync());

        var head = Assert.Single(
            await observer.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        var receipt = Assert.Single(
            await observer.Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(proposal.Id, revision.Id);
        Assert.Equal(proposal.Id, head.CurrentRevisionId);
        Assert.Equal(1L, head.CurrentRevisionNumber);

        Assert.Equal(project, receipt.ProjectId);
        Assert.Equal(owner, receipt.ActorId);
        Assert.Equal(scope.OperationId, receipt.OperationId);
        Assert.Equal(1L, receipt.AuthorizationVersion);
        Assert.Equal(proposal.Id, receipt.SavedRevisionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task G12_L07_L08_Concurrent_mutations_are_serialized(
        bool identicalPayload)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        using var deadline =
            new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var ct = deadline.Token;
        var gate = new MutationBeforeCommit(ct);

        await using var leader = Context(db, gate);
        await using var follower = Context(db);
        await using var observer = Context(db);

        await follower.Database.OpenConnectionAsync(ct);

        var connectionId = (await follower.Database
            .SqlQuery<int>($"SELECT CONNECTION_ID() AS Value")
            .ToListAsync(ct)).Single();

        var scope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        static RequirementMutation Mutation(int bedrooms) =>
            new(RequirementMutationCategory.Full,
                new SaveRequirementsRequest(
                    null, "synthetic-fixture-v1", 1,
                    [new("bedroom", bedrooms), new("kitchen", 1)]));

        RequirementRevision Proposal(int bedrooms) =>
            new(Guid.NewGuid(), project, 1,
                "requirements/1", "synthetic-fixture-v1",
                new RequirementContent(1,
                    [new RoomCount("bedroom", bedrooms),
                     new RoomCount("kitchen", 1)]),
                Now.AddMinutes(1));

        var firstProposal = Proposal(2);
        var secondBedrooms = identicalPayload ? 2 : 3;
        var secondProposal = Proposal(secondBedrooms);

        var leaderTx = Transactions(leader);
        var followerTx = Transactions(follower);

        var leaderStore = new RequirementMutationStore(
            leader, leaderTx,
            new RequirementRevisionRepository(leader, leaderTx));

        var followerStore = new RequirementMutationStore(
            follower, followerTx,
            new RequirementRevisionRepository(follower, followerTx));

        var first = leaderStore.CommitAsync(
            scope, Mutation(2), firstProposal, ct);

        Task<RequirementMutationResult>? second = null;

        try
        {
            await Task.WhenAny(first, gate.Reached.Task)
                .WaitAsync(ct);

            if (first.IsCompleted)
                await first;

            Assert.True(gate.Reached.Task.IsCompletedSuccessfully);

            second = followerStore.CommitAsync(
                scope, Mutation(secondBedrooms), secondProposal, ct);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (second.IsCompleted)
                {
                    await second;
                    Assert.Fail(
                        "Follower completed before leader released its lock.");
                }

                var commands = await observer.Database
                    .SqlQuery<string>(
                        $"SELECT COALESCE(INFO, '') AS Value FROM information_schema.PROCESSLIST WHERE ID = {connectionId}")
                    .ToListAsync(ct);

                var sql = commands.SingleOrDefault() ?? "";

                if ((sql.Contains("users",
                        StringComparison.OrdinalIgnoreCase) ||
                     sql.Contains("projects",
                        StringComparison.OrdinalIgnoreCase)) &&
                    sql.Contains("FOR UPDATE",
                        StringComparison.OrdinalIgnoreCase))
                    break;

                await Task.Delay(10, ct);
            }

            await using var probe =
                await observer.Database.BeginTransactionAsync(ct);

            var contention = await Assert.ThrowsAsync<
                MySql.Data.MySqlClient.MySqlException>(() =>
                    observer.Database.SqlQuery<int>(
                        $"SELECT 1 AS Value FROM projects WHERE id = {project} FOR UPDATE NOWAIT")
                        .ToListAsync(ct));

            Assert.Equal(3572, contention.Number);
        }
        finally
        {
            gate.Release.TrySetResult();

            if (second is null)
                await first;
            else
                await Task.WhenAll(first, second);
        }

        Assert.NotNull(second);

        var firstResult = await first;
        var secondResult = await second!;

        Assert.Equal(RequirementMutationStatus.Committed,
            firstResult.Status);

        Assert.Equal(firstProposal.Id,
            firstResult.Revision!.RevisionId);

        Assert.Equal(
            identicalPayload
                ? RequirementMutationStatus.Committed
                : RequirementMutationStatus.Reused,
            secondResult.Status);

        Assert.Null(secondResult.Failure);

        if (identicalPayload)
        {
            Assert.NotNull(secondResult.Revision);
            Assert.Equal(firstProposal.Id,
                secondResult.Revision!.RevisionId);
        }
        else
        {
            Assert.Null(secondResult.Revision);
        }

        var revision = Assert.Single(
            await observer.Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync(ct));

        var head = Assert.Single(
            await observer.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync(ct));

        var receipt = Assert.Single(
            await observer.Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync(ct));

        Assert.Equal(firstProposal.Id, revision.Id);
        Assert.Equal(firstProposal.Id, head.CurrentRevisionId);
        Assert.Equal(1L, head.CurrentRevisionNumber);
        Assert.Equal(firstProposal.Id, receipt.SavedRevisionId);
        Assert.Equal(scope.OperationId, receipt.OperationId);

        var digest = RequirementMutationFingerprint.Compute(
            project, owner, scope.OperationId, 1, null,
            RequirementMutationCanonicalPayload.Encode(Mutation(2)));

        Assert.Equal(digest, receipt.Digest);
    }

    private sealed class MutationBeforeCommit(
        CancellationToken deadline)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult>
            TransactionCommittingAsync(
                System.Data.Common.DbTransaction transaction,
                Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
                CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(deadline);
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G12_L09_L10_Transaction_failure_and_reconciliation(
        bool lostAcknowledgement)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        var scope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        var mutation = new RequirementMutation(
            RequirementMutationCategory.Full,
            new SaveRequirementsRequest(
                null, "synthetic-fixture-v1", 1,
                [new("bedroom", 2), new("kitchen", 1)]));

        var proposal = new RequirementRevision(
            Guid.NewGuid(), project, 1,
            "requirements/1", "synthetic-fixture-v1",
            new RequirementContent(1,
                [new RoomCount("bedroom", 2),
                 new RoomCount("kitchen", 1)]),
            Now.AddMinutes(1));

        var abort = new AbortAfterReceiptSave();
        var lostAck = new MutationLostAcknowledgement();

        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor interceptor =
            lostAcknowledgement ? lostAck : abort;

        await using (var writer = Context(db, interceptor))
        {
            var tx = Transactions(writer);

            var store = new RequirementMutationStore(
                writer, tx,
                new RequirementRevisionRepository(writer, tx));

            if (lostAcknowledgement)
            {
                var result = await store.CommitAsync(
                    scope, mutation, proposal, default);

                Assert.Equal(
                    RequirementMutationStatus.Unknown, result.Status);

                Assert.Null(result.Revision);
                Assert.Null(result.Failure);
                Assert.Equal(1, lostAck.Commits);
            }
            else
            {
                var failure = await Assert.ThrowsAsync<DbUpdateException>(
                    () => store.CommitAsync(
                        scope, mutation, proposal, default));

                Assert.Equal(
                    "Synthetic failure after receipt save.",
                    failure.Message);

                Assert.Equal(1, abort.Failures);
            }

            Assert.Empty(writer.ChangeTracker.Entries());
        }

        await using (var observer = Context(db))
        {
            var tx = Transactions(observer);

            var store = new RequirementMutationStore(
                observer, tx,
                new RequirementRevisionRepository(observer, tx));

            var observation = await store.ObserveAsync(
                scope, null, default);

            Assert.Equal(
                lostAcknowledgement
                    ? RequirementMutationStatus.Committed
                    : RequirementMutationStatus.Absent,
                observation.Status);

            Assert.Null(observation.Failure);

            var revisions = await observer
                .Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync();

            var heads = await observer
                .Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync();

            var receipts = await observer
                .Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync();

            if (lostAcknowledgement)
            {
                Assert.NotNull(observation.Revision);

                Assert.Equal(proposal.Id,
                    observation.Revision!.RevisionId);

                Assert.Equal(proposal.Id,
                    Assert.Single(revisions).Id);

                Assert.Equal(proposal.Id,
                    Assert.Single(heads).CurrentRevisionId);

                var receipt = Assert.Single(receipts);

                Assert.Equal(proposal.Id, receipt.SavedRevisionId);
                Assert.Equal(scope.OperationId, receipt.OperationId);
                Assert.Equal(scope.Version, receipt.AuthorizationVersion);
            }
            else
            {
                Assert.Null(observation.Revision);
                Assert.Empty(revisions);
                Assert.Empty(heads);
                Assert.Empty(receipts);
            }
        }

        // Explicit caller retry using the same operation identity.
        // This is not an automatic transaction retry.
        await using (var retry = Context(db))
        {
            var tx = Transactions(retry);

            var store = new RequirementMutationStore(
                retry, tx,
                new RequirementRevisionRepository(retry, tx));

            var result = await store.CommitAsync(
                scope, mutation, proposal, default);

            Assert.Equal(
                RequirementMutationStatus.Committed, result.Status);

            Assert.NotNull(result.Revision);
            Assert.Equal(proposal.Id, result.Revision!.RevisionId);
        }

        await using var final = Context(db);

        var finalRevision = Assert.Single(
            await final.Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync());

        var finalHead = Assert.Single(
            await final.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        var finalReceipt = Assert.Single(
            await final.Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(proposal.Id, finalRevision.Id);
        Assert.Equal(proposal.Id, finalHead.CurrentRevisionId);
        Assert.Equal(1L, finalHead.CurrentRevisionNumber);
        Assert.Equal(proposal.Id, finalReceipt.SavedRevisionId);
        Assert.Equal(scope.OperationId, finalReceipt.OperationId);

        var digest = RequirementMutationFingerprint.Compute(
            project, owner, scope.OperationId, 1, null,
            RequirementMutationCanonicalPayload.Encode(mutation));

        Assert.Equal(digest, finalReceipt.Digest);
    }

    private sealed class AbortAfterReceiptSave
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null &&
                eventData.Context.ChangeTracker
                    .Entries<RequirementMutationReceiptRecord>()
                    .Any())
            {
                Failures++;

                throw new DbUpdateException(
                    "Synthetic failure after receipt save.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class MutationLostAcknowledgement
        : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public int Commits { get; private set; }

        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Commits++;

            throw new IOException(
                "Synthetic lost acknowledgement after database commit.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G12_L11_L12_Revision_conflict_and_deletion_protection(
        bool verifyDeletion)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        var firstScope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        var secondScope = new RequirementMutationScope(
            project, owner, Guid.NewGuid(), 1);

        var staleScope = new RequirementMutationScope(
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
                RequirementMutationStatus.Committed, initial.Status);

            var next = await store.CommitAsync(
                secondScope, Mutation(first.Id, 3), second, default);

            Assert.Equal(
                RequirementMutationStatus.Committed, next.Status);
        }

        if (!verifyDeletion)
        {
            await using var writer = Context(db);
            var tx = Transactions(writer);

            var store = new RequirementMutationStore(
                writer, tx,
                new RequirementRevisionRepository(writer, tx));

            var stale = await store.CommitAsync(
                staleScope, Mutation(first.Id, 4),
                Revision(3, 4), default);

            Assert.Equal(
                RequirementMutationStatus.Failed, stale.Status);

            Assert.Null(stale.Revision);
            Assert.NotNull(stale.Failure);

            Assert.Equal(
                RequirementsOperationStatus.RevisionConflict,
                stale.Failure!.Status);

            Assert.Empty(writer.ChangeTracker.Entries());
        }
        else
        {
            // The first revision is historical, not the current head.
            // Its mutation receipt must independently prevent deletion.
            await using (var deletion = Context(db))
            {
                var error = await Assert.ThrowsAsync<
                    MySql.Data.MySqlClient.MySqlException>(
                    async () =>
                    {
                        await deletion.Set<RequirementRevisionRecord>()
                            .Where(r => r.ProjectId == project &&
                                        r.Id == first.Id)
                            .ExecuteDeleteAsync();
                    });

                Assert.Equal(1451, error.Number);
                Assert.Contains(
                    "fk_requirement_mutation_receipts_revision",
                    error.Message);
            }

            // Existing project deletion restrictions must remain intact.
            await using (var deletion = Context(db))
            {
                var error = await Assert.ThrowsAsync<
                    MySql.Data.MySqlClient.MySqlException>(
                    async () =>
                    {
                        await deletion.Set<ProjectRecord>()
                            .Where(p => p.Id == project)
                            .ExecuteDeleteAsync();
                    });

                Assert.Equal(1451, error.Number);
            }
        }

        await using var observer = Context(db);

        var revisions = await observer
            .Set<RequirementRevisionRecord>()
            .AsNoTracking()
            .OrderBy(r => r.RevisionNumber)
            .ToListAsync();

        Assert.Equal(2, revisions.Count);
        Assert.Equal(first.Id, revisions[0].Id);
        Assert.Equal(second.Id, revisions[1].Id);

        var head = Assert.Single(
            await observer.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(second.Id, head.CurrentRevisionId);
        Assert.Equal(2L, head.CurrentRevisionNumber);

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

        Assert.DoesNotContain(receipts, r =>
            r.OperationId == staleScope.OperationId);

        Assert.True(await observer.Set<ProjectRecord>()
            .AnyAsync(p => p.Id == project));

        var observerTx = Transactions(observer);

        var observerStore = new RequirementMutationStore(
            observer, observerTx,
            new RequirementRevisionRepository(observer, observerTx));

        var absent = await observerStore.ObserveAsync(
            staleScope, null, default);

        Assert.Equal(RequirementMutationStatus.Absent, absent.Status);

        var replay = await observerStore.ObserveAsync(
            firstScope, Mutation(null, 2), default);

        Assert.Equal(
            RequirementMutationStatus.Committed, replay.Status);

        Assert.NotNull(replay.Revision);
        Assert.Equal(first.Id, replay.Revision!.RevisionId);
    }
}