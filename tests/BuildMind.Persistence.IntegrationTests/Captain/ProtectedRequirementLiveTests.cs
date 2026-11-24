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

    private static OwnerProtectedRequirementRevisionRepository Adapter(BuildMindDbContext context, Guid actor) =>
        new(new RequirementRevisionRepository(context, Transactions(context)), new User(actor),
            new ProjectOwnerAuthorizationPort(context));

    private static RequirementRevision Revision(Guid project, long number) => new(
        Guid.NewGuid(), project, number, "requirements/1", "synthetic-fixture-v1",
        new RequirementContent(1, [new RoomCount("bedroom", 2), new RoomCount("kitchen", 1)]), Now.AddMinutes(number));

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
