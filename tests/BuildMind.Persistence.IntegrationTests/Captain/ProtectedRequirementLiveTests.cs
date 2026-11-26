using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.CaptainIntegration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Application.Features.Requirements.Contracts;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace BuildMind.Persistence.IntegrationTests.Captain;

/// <summary>Captain protected requirement adapter on disposable MySQL 8.4 (A1–A8 project-row lock path).</summary>
public sealed class ProtectedRequirementLiveTests
{
    [Fact]
    public async Task R01_owner_saves_and_chains_revisions()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var context = Context(db);
        var adapter = Adapter(context, owner);

        var first = await adapter.TrySaveAtomicallyAsync(null, Revision(project, 1), default);
        Assert.Equal(RequirementSaveStatus.Saved, first.Status);
        var second = await adapter.TrySaveAtomicallyAsync(first.SavedRevision!.Id, Revision(project, 2), default);
        Assert.Equal(RequirementSaveStatus.Saved, second.Status);
        Assert.Equal(RequirementSaveStatus.RevisionConflict,
            (await adapter.TrySaveAtomicallyAsync(first.SavedRevision.Id, Revision(project, 3), default)).Status);
    }

    [Fact]
    public async Task R02_other_user_cannot_write_and_nothing_is_stored()
    {
        await using var db = await SchemaAsync();
        var (_, project) = await SeedAsync(db);
        var (intruder, _) = await SeedAsync(db);
        await using var context = Context(db);

        var result = await Adapter(context, intruder).TrySaveAtomicallyAsync(null, Revision(project, 1), default);

        Assert.Equal(RequirementSaveStatus.ProjectNotFound, result.Status);
        Assert.Null(await new RequirementRevisionRepository(context, Transactions(context)).GetCurrentAsync(project, default));
    }

    [Fact]
    public async Task R03_stale_authorization_version_is_rejected_inside_the_locked_transaction()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var context = Context(db);
        var port = new BumpingAuthorization(new ProjectOwnerAuthorizationPort(context), db);
        var adapter = new OwnerProtectedRequirementRevisionRepository(
            new RequirementRevisionRepository(context, Transactions(context)), new User(owner), port);

        var result = await adapter.TrySaveAtomicallyAsync(null, Revision(project, 1), default);

        Assert.Equal(RequirementSaveStatus.ProjectNotFound, result.Status);
        await using var check = Context(db);
        Assert.Null(await new RequirementRevisionRepository(check, Transactions(check)).GetCurrentAsync(project, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task REQ_G09_Competing_transfer_and_protected_save_commit_in_lock_order(bool mutationFirst)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var (newOwner, _) = await SeedAsync(db);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        var gate = new BeforeCommit(ct);
        await using var leader = Context(db, gate);
        await using var follower = Context(db);
        await using var observer = Context(db);
        await follower.Database.OpenConnectionAsync(ct);
        var connectionId = (await follower.Database.SqlQuery<int>($"SELECT CONNECTION_ID() AS Value").ToListAsync(ct)).Single();
        var proposal = Revision(project, 1);
        RequirementSaveResult? saved = null;
        async Task Save(BuildMindDbContext c) =>
            saved = await Adapter(c, owner).TrySaveAtomicallyAsync(null, proposal, ct);
        async Task Transfer(BuildMindDbContext c)
        {
            await using var tx = await c.Database.BeginTransactionAsync(ct);
            Assert.Equal(1, await c.Set<ProjectRecord>().Where(p => p.Id == project && p.OwnerId == owner)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerId, newOwner), ct));
            Assert.Equal(2L, await new ProjectAuthorizationVersionStore(c).TryIncrementAsync(project, 1, ct));
            await tx.CommitAsync(ct);
        }

        var first = mutationFirst ? Save(leader) : Transfer(leader);
        Task second = Task.CompletedTask;
        try
        {
            await Task.WhenAny(first, gate.Reached.Task).WaitAsync(ct);
            if (first.IsCompleted) await first;
            Assert.True(gate.Reached.Task.IsCompletedSuccessfully);
            second = mutationFirst ? Transfer(follower) : Save(follower);
            // bm_test can see its own connections. Observe the actual competitor on the server
            // while the leader still holds the project row, before allowing either commit.
            while (true)
            {
                if (second.IsCompleted) { await second; Assert.Fail("Competitor finished before the lock was released."); }
                var commands = await observer.Database.SqlQuery<string>(
                    $"SELECT COALESCE(INFO, '') AS Value FROM information_schema.PROCESSLIST WHERE ID = {connectionId}").ToListAsync(ct);
                var sql = commands.SingleOrDefault() ?? "";
                if (sql.Contains("projects", StringComparison.OrdinalIgnoreCase) &&
                    sql.Contains(mutationFirst ? "UPDATE" : "FOR UPDATE", StringComparison.OrdinalIgnoreCase)) break;
                await Task.Delay(10, ct);
            }
            // Independently verify that the exact target row is locked, without relying
            // on a delay or requiring global performance_schema lock-table privileges.
            await using var probe = await observer.Database.BeginTransactionAsync(ct);
            var contention = await Assert.ThrowsAsync<MySql.Data.MySqlClient.MySqlException>(() =>
                observer.Database.SqlQuery<int>($"SELECT 1 AS Value FROM projects WHERE id = {project} FOR UPDATE NOWAIT").ToListAsync(ct));
            Assert.Equal(3572, contention.Number);
        }
        finally
        {
            gate.Release.TrySetResult();
            await Task.WhenAll(first, second);
        }
        Assert.Equal(mutationFirst ? RequirementSaveStatus.Saved : RequirementSaveStatus.ProjectNotFound, saved!.Status);
        var access = await new ProjectOwnerAuthorizationPort(observer).AuthorizeOwnerAsync(project, newOwner, ct);
        Assert.Equal(2L, access.Snapshot!.Version.Value);
        var stale = await Adapter(observer, owner).TrySaveAtomicallyAsync(mutationFirst ? proposal.Id : null,
            Revision(project, mutationFirst ? 2 : 1), ct);
        Assert.Equal(RequirementSaveStatus.ProjectNotFound, stale.Status);
        await AssertChain(observer, project, mutationFirst ? [proposal] : [], ct);
    }

    private sealed class BeforeCommit(CancellationToken deadline) : DbTransactionInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(deadline);
            return result;
        }
    }

    [Fact]
    public async Task REQ_G12_Lost_commit_acknowledgement_leaves_one_complete_revision_and_no_retry()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var lostAck = new LostAcknowledgement();
        await using var writer = Context(db, lostAck);
        var proposal = Revision(project, 1);
        await Assert.ThrowsAsync<TransactionOutcomeUnknownException>(() =>
            Adapter(writer, owner).TrySaveAtomicallyAsync(null, proposal, default));
        Assert.Equal(1, lostAck.Commits);
        await using var observer = Context(db);
        await AssertChain(observer, project, [proposal], default);
    }

    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        public int Commits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Commits++;
            throw new IOException("Synthetic lost acknowledgement after database commit.");
        }
    }

    [Fact]
    public async Task REQ_G13_Concurrent_next_saves_preserve_only_the_winning_content()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var observer = Context(db);
        var first = Revision(project, 1);
        var adapter = Adapter(observer, owner);
        Assert.Equal(RequirementSaveStatus.Saved, (await adapter.TrySaveAtomicallyAsync(null, first, default)).Status);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        async Task<(RequirementRevision Proposal, RequirementSaveResult Result)> Compete(int bedrooms)
        {
            await using var c = Context(db);
            await c.Database.OpenConnectionAsync(ct);
            var proposal = Revision(project, 2, bedrooms);
            if (Interlocked.Increment(ref count) == 3) ready.TrySetResult();
            await ready.Task.WaitAsync(ct);
            return (proposal, await Adapter(c, owner).TrySaveAtomicallyAsync(first.Id, proposal, ct));
        }
        var attempts = await Task.WhenAll(Enumerable.Range(1, 3).Select(Compete));
        var winner = Assert.Single(attempts, a => a.Result.Status == RequirementSaveStatus.Saved).Proposal;
        Assert.All(attempts.Where(a => a.Proposal.Id != winner.Id),
            a => Assert.Equal(RequirementSaveStatus.RevisionConflict, a.Result.Status));
        await AssertChain(observer, project, [first, winner], ct);
        var third = Revision(project, 3);
        Assert.Equal(RequirementSaveStatus.Saved, (await adapter.TrySaveAtomicallyAsync(winner.Id, third, ct)).Status);
        await AssertChain(observer, project, [first, winner, third], ct);
    }

    private static async Task AssertChain(BuildMindDbContext c, Guid project, RequirementRevision[] expected, CancellationToken ct)
    {
        var rows = await c.Set<RequirementRevisionRecord>().AsNoTracking().OrderBy(r => r.RevisionNumber).ToListAsync(ct);
        Assert.Equal(expected.Select(r => r.Id), rows.Select(r => r.Id));
        Assert.Equal(expected.Select(r => r.RevisionNumber), rows.Select(r => r.RevisionNumber));
        var heads = await c.Set<RequirementHeadRecord>().AsNoTracking().ToListAsync(ct);
        if (expected.Length == 0) { Assert.Empty(heads); return; }
        var head = Assert.Single(heads);
        var last = expected[^1];
        Assert.Equal(last.Id, head.CurrentRevisionId);
        Assert.Equal(last.RevisionNumber, head.CurrentRevisionNumber);
        var current = await new RequirementRevisionRepository(c, Transactions(c)).GetCurrentAsync(project, ct);
        Assert.Equal(last.Content.GroundFloorRooms, current!.Content.GroundFloorRooms);
    }

    [Theory]
    [InlineData(true, "unchanged")]
    [InlineData(true, "transfer")]
    [InlineData(true, "bump")]
    [InlineData(false, "unchanged")]
    [InlineData(false, "transfer")]
    [InlineData(false, "bump")]
    [InlineData(true, "nonowner")]
    [InlineData(true, "newowner")]
    public async Task REQ_G14_Live_read_rechecks_committed_project_authority(bool hasRevision, string scenario)
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var (other, _) = await SeedAsync(db);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var setup = Context(db);
        var revision = Revision(project, 1);
        if (hasRevision)
            Assert.Equal(RequirementSaveStatus.Saved, (await Adapter(setup, owner).TrySaveAtomicallyAsync(null, revision, ct)).Status);

        async Task ChangeOwnerOrVersion(bool transfer)
        {
            await using var change = Context(db);
            await using var transaction = await change.Database.BeginTransactionAsync(ct);
            if (transfer)
                Assert.Equal(1, await change.Set<ProjectRecord>().Where(p => p.Id == project && p.OwnerId == owner)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerId, other), ct));
            Assert.Equal(2L, await new ProjectAuthorizationVersionStore(change).TryIncrementAsync(project, 1, ct));
            await transaction.CommitAsync(ct);
        }

        async Task CheckRequest(Guid actor, bool initiallyDenied, bool changeDuringRead, long initialVersion)
        {
            await using var request = Context(db);
            await request.Database.OpenConnectionAsync(ct);
            var events = new List<string>();
            var authorization = new ObservedAuthorization(new ProjectOwnerAuthorizationPort(request), events, project, actor);
            var store = new AfterCurrentRead(Adapter(request, actor), async current =>
            {
                Assert.Equal(hasRevision ? revision.Id : (Guid?)null, current?.Id);
                events.Add("read");
                if (changeDuringRead)
                {
                    await ChangeOwnerOrVersion(scenario == "transfer");
                    events.Add("committed");
                }
            });
            var service = new RequirementsApplicationService(new User(actor), new ProjectRepository(request),
                new ConfiguredRequirementCatalogueProvider(new ConfigurationBuilder().Build()), new(store),
                new(store, TimeProvider.System), authorization);
            var result = await service.GetAsync(project, ct);
            if (initiallyDenied)
                Assert.Equal(new[] { "denied" }, events);
            else if (changeDuringRead)
                Assert.Equal(new[] { "v1", "read", "committed", scenario == "transfer" ? "denied" : "v2" }, events);
            else
                Assert.Equal(new[] { $"v{initialVersion}", "read", $"v{initialVersion}" }, events);

            if (initiallyDenied || changeDuringRead)
            {
                Assert.Equal(RequirementsOperationStatus.NotFound, result.Status);
                Assert.Null(result.Revision);
                Assert.Empty(result.Issues);
            }
            else if (!hasRevision)
            {
                Assert.Equal(RequirementsOperationStatus.NoSavedRevision, result.Status);
                Assert.Null(result.Revision);
            }
            else
            {
                Assert.Equal(RequirementsOperationStatus.Success, result.Status);
                Assert.Equal(JsonSerializer.Serialize(new RequirementRevisionDto(revision)), JsonSerializer.Serialize(result.Revision));
            }
        }

        if (scenario == "newowner")
        {
            await ChangeOwnerOrVersion(true);
            await CheckRequest(other, false, false, 2);
            await CheckRequest(owner, true, false, 2);
        }
        else
            await CheckRequest(scenario == "nonowner" ? other : owner, scenario == "nonowner",
                scenario is "transfer" or "bump", 1);

        var state = await setup.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == project, ct);
        Assert.Equal(scenario is "transfer" or "newowner" ? other : owner, state.OwnerId);
        Assert.Equal(scenario is "transfer" or "newowner" or "bump" ? 2L : 1L, state.AuthorizationVersion);
        await AssertChain(setup, project, hasRevision ? [revision] : [], ct);
        if (hasRevision)
        {
            var persisted = await Adapter(setup, state.OwnerId).GetCurrentAsync(project, ct);
            Assert.Equal(JsonSerializer.Serialize(new RequirementRevisionDto(revision)),
                JsonSerializer.Serialize(new RequirementRevisionDto(persisted!)));
        }
    }

    private sealed class ObservedAuthorization(IProjectAuthorizationPort inner, List<string> events, Guid project, Guid actor)
        : IProjectAuthorizationPort
    {
        public async Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken ct)
        {
            Assert.Equal(project, projectId);
            Assert.Equal(actor, actorId);
            var result = await inner.AuthorizeOwnerAsync(projectId, actorId, ct);
            if (result.Status == ProjectAuthorizationStatus.Authorized)
            {
                Assert.Equal(project, result.Snapshot!.ProjectId);
                Assert.Equal(actor, result.Snapshot.ActorId);
                events.Add($"v{result.Snapshot.Version.Value}");
            }
            else
            {
                Assert.Null(result.Snapshot);
                events.Add("denied");
            }
            return result;
        }
    }

    private sealed class AfterCurrentRead(IRequirementRevisionRepository inner, Func<RequirementRevision?, Task> afterRead)
        : IRequirementRevisionRepository
    {
        public async Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken ct)
        {
            var current = await inner.GetCurrentAsync(projectId, ct);
            await afterRead(current);
            return current;
        }
        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken ct) =>
            inner.GetAsync(projectId, revisionId, ct);
        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expected, RequirementRevision proposed, CancellationToken ct) =>
            inner.TrySaveAtomicallyAsync(expected, proposed, ct);
    }

    private static OwnerProtectedRequirementRevisionRepository Adapter(BuildMindDbContext context, Guid actor) =>
        new(new RequirementRevisionRepository(context, Transactions(context)), new User(actor),
            new ProjectOwnerAuthorizationPort(context));

    private static RequirementRevision Revision(Guid project, long number, int bedrooms = 2) => new(
        Guid.NewGuid(), project, number, "requirements/1", "synthetic-fixture-v1",
        new RequirementContent(1, [new RoomCount("bedroom", bedrooms), new RoomCount("kitchen", 1)]), Now.AddMinutes(number));

    private sealed class User(Guid id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => id;
    }

    private sealed class BumpingAuthorization(IProjectAuthorizationPort inner, Support.DisposableMySqlDatabase db)
        : IProjectAuthorizationPort
    {
        public async Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken token)
        {
            var result = await inner.AuthorizeOwnerAsync(projectId, actorId, token);
            await BumpAuthorizationVersionAsync(db, projectId, result.Snapshot!.Version.Value);
            return result;
        }
    }
}
