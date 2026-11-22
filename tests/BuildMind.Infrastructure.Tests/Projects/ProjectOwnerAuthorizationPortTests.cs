using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Projects;

/// <summary>
/// M1-M3-AUTH-V1.0 (Option A) offline security tests for <see cref="ProjectOwnerAuthorizationPort"/> and
/// <see cref="ProjectAuthorizationVersionStore"/>. The context points at a CLOSED loopback port, so any database access
/// fails loudly: a result returned without an exception proves no database call was made, and an exception proves
/// failures propagate instead of becoming a silent Denied (or a silent "no change") result. No real database is used.
/// </summary>
public sealed class ProjectOwnerAuthorizationPortTests
{
    private const string UnreachableConnection =
        "Server=127.0.0.1;Port=1;Database=buildmind_synthetic;User ID=bm_synthetic;SslMode=Required;Password=synthetic-not-a-secret;Connection Timeout=2";

    private static readonly Guid Project = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static TheoryData<Guid, Guid> EmptyIdentities() => new()
    {
        { Guid.Empty, Actor },     // empty project
        { Project, Guid.Empty },   // empty (anonymous) actor
        { Guid.Empty, Guid.Empty },
    };

    [Theory]
    [MemberData(nameof(EmptyIdentities))]
    public async Task Empty_project_or_actor_is_denied_without_a_snapshot_or_database_call(Guid projectId, Guid actorId)
    {
        await using var context = UnreachableContext();

        var result = await new ProjectOwnerAuthorizationPort(context).AuthorizeOwnerAsync(projectId, actorId, CancellationToken.None);

        Assert.Equal(ProjectAuthorizationStatus.Denied, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_never_reported_as_denied()
    {
        await using var context = UnreachableContext();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProjectOwnerAuthorizationPort(context).AuthorizeOwnerAsync(Project, Actor, cancelled.Token));
    }

    [Fact]
    public async Task Database_failure_propagates_and_never_authorizes_or_denies()
    {
        // An unknown project cannot be distinguished from an unavailable database offline: the port must not answer
        // either way. It must surface the dependency failure (fail closed, no Authorized, no disguised Denied).
        await using var context = UnreachableContext();

        var failure = await Record.ExceptionAsync(() =>
            new ProjectOwnerAuthorizationPort(context).AuthorizeOwnerAsync(Project, Actor, CancellationToken.None));

        Assert.NotNull(failure);
        Assert.IsNotAssignableFrom<OperationCanceledException>(failure);
    }

    public static TheoryData<Guid, long> InvalidIncrements() => new()
    {
        { Guid.Empty, 1 },
        { Project, 0 },
        { Project, -1 },
        { Project, long.MinValue },
        { Project, long.MaxValue },   // overflow: an increment would wrap; refused before any write
    };

    [Theory]
    [MemberData(nameof(InvalidIncrements))]
    public async Task Invalid_increment_arguments_are_refused_without_a_database_call(Guid projectId, long expectedVersion)
    {
        await using var context = UnreachableContext();

        Assert.Null(await new ProjectAuthorizationVersionStore(context).TryIncrementAsync(projectId, expectedVersion, CancellationToken.None));
    }

    [Fact]
    public async Task Increment_cancellation_propagates()
    {
        await using var context = UnreachableContext();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProjectAuthorizationVersionStore(context).TryIncrementAsync(Project, 1, cancelled.Token));
    }

    [Fact]
    public async Task Increment_database_failure_propagates_instead_of_reporting_a_lost_race()
    {
        // Null means "the version moved or the project does not exist"; a dependency failure must not be confused with it.
        await using var context = UnreachableContext();

        var failure = await Record.ExceptionAsync(() =>
            new ProjectAuthorizationVersionStore(context).TryIncrementAsync(Project, 1, CancellationToken.None));

        Assert.NotNull(failure);
        Assert.IsNotAssignableFrom<OperationCanceledException>(failure);
    }

    private static BuildMindDbContext UnreachableContext()
    {
        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, UnreachableConnection, 5);
        return new BuildMindDbContext(builder.Options);
    }
}
