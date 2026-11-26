using BuildMind.Infrastructure.Persistence.DesignTime;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;

namespace BuildMind.Infrastructure.Tests.Projects;

public sealed class ProjectMutationAuthorizationTests
{
    [Fact]
    public async Task Invalid_scope_is_denied_and_valid_scope_requires_a_transaction_before_io()
    {
        await using var context = BuildMindDesignTimeDbContextFactory.Create(
            "Server=127.0.0.1;Port=1;Database=offline;User ID=bm_test;Password=offline;SslMode=Required");
        var gate = new ProjectMutationAuthorization(context);
        var projectId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        Assert.False(await gate.LockAndCheckAsync(Guid.Empty, actorId, 1, default));
        Assert.False(await gate.LockAndCheckAsync(projectId, Guid.Empty, 1, default));
        Assert.False(await gate.LockAndCheckAsync(projectId, actorId, 0, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.LockAndCheckAsync(projectId, actorId, 1, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.LockAndCheckAsync(
            projectId, actorId, 1, new CancellationToken(canceled: true)));
    }
}
