using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Repositories.Survey;
using Microsoft.EntityFrameworkCore;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;
using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Persistence.IntegrationTests.Captain;

/// <summary>Captain Survey persistence on disposable MySQL 8.4: protected insert, scoped read, CAS state updates.</summary>
public sealed class SurveyRepositoryLiveTests
{
    [Fact]
    public async Task S01_owner_insert_round_trips_and_reads_are_project_scoped()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var (_, otherProject) = await SeedAsync(db);
        var survey = Queued(project, owner);

        await using (var context = Context(db))
            Assert.NotNull(await Repository(context).AddAsync(survey, default));

        await using var read = Context(db);
        var stored = await Repository(read).GetAsync(project, survey.Id, default);
        Assert.NotNull(stored);
        Assert.Equal(survey.Sha256Hash, stored.Sha256Hash);
        Assert.Equal(SurveyStatus.Queued, stored.Status);
        Assert.Equal(survey.UploadedAtUtc, stored.UploadedAtUtc);
        Assert.Null(await Repository(read).GetAsync(otherProject, survey.Id, default));
    }

    [Fact]
    public async Task S02_non_owner_insert_is_denied_and_writes_nothing()
    {
        await using var db = await SchemaAsync();
        var (_, project) = await SeedAsync(db);
        var (intruder, _) = await SeedAsync(db);

        await using var context = Context(db);
        Assert.Null(await Repository(context).AddAsync(Queued(project, intruder), default));
        Assert.Equal(0, await context.Set<SurveyRecord>().CountAsync());
    }

    [Fact]
    public async Task S03_authorization_change_between_read_and_write_denies_the_insert()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var context = Context(db);
        var bumping = new BumpingAuthorization(new ProjectOwnerAuthorizationPort(context), db, project);
        var repository = new SurveyRepository(context, Transactions(context), bumping);

        Assert.Null(await repository.AddAsync(Queued(project, owner), default));
        await using var check = Context(db);
        Assert.Equal(0, await check.Set<SurveyRecord>().CountAsync());
    }

    [Fact]
    public async Task S04_state_updates_are_compare_and_set()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var survey = Queued(project, owner);
        await using var context = Context(db);
        var repository = Repository(context);
        await repository.AddAsync(survey, default);

        var processing = survey.BeginProcessing(1);
        Assert.True(await repository.UpdateAsync(processing, 1, default));
        Assert.False(await repository.UpdateAsync(processing, 1, default));
        var completed = processing.Complete(2, Now.AddMinutes(1));
        Assert.True(await repository.UpdateAsync(completed, 2, default));

        var stored = await repository.GetAsync(project, survey.Id, default);
        Assert.Equal(SurveyStatus.Completed, stored!.Status);
        Assert.Equal(3, stored.StateVersion);
        Assert.Equal(Now.AddMinutes(1), stored.CompletedAtUtc);
    }

    private static SurveyRepository Repository(Infrastructure.Persistence.BuildMindDbContext context) =>
        new(context, Transactions(context), new ProjectOwnerAuthorizationPort(context));

    private static SurveyEntity Queued(Guid project, Guid owner) => SurveyEntity.CreateQueued(
        Guid.NewGuid(), project, owner, Guid.NewGuid(), "synthetic-survey.pdf", "application/pdf", 1024,
        new string('a', 64), Now);

    /// <summary>Returns a fresh authorization, then advances the version before the write transaction starts.</summary>
    private sealed class BumpingAuthorization(IProjectAuthorizationPort inner, Support.DisposableMySqlDatabase db, Guid project)
        : IProjectAuthorizationPort
    {
        public async Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken token)
        {
            var result = await inner.AuthorizeOwnerAsync(projectId, actorId, token);
            await BumpAuthorizationVersionAsync(db, project, result.Snapshot!.Version.Value);
            return result;
        }
    }
}
