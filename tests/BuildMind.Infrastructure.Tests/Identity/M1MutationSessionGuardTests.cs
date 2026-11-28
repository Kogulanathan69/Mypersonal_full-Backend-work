using BuildMind.Infrastructure.Persistence.DesignTime;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class M1MutationSessionGuardTests
{
    [Fact]
    public async Task Invalid_scope_is_denied_and_valid_scope_requires_transaction()
    {
        await using var context = BuildMindDesignTimeDbContextFactory.Create(
            "Server=127.0.0.1;Port=1;Database=offline;User ID=bm_test;Password=offline;SslMode=Required");
        var guard = new M1MutationSessionGuard(context);
        var actor = Guid.NewGuid();
        Assert.False(await guard.LockAndCheckAsync(Guid.Empty, 1, default));
        Assert.False(await guard.LockAndCheckAsync(actor, 0, default));
        Assert.False(await guard.LockAndCheckAsync(actor, -1, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.LockAndCheckAsync(actor, 1, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guard.LockAndCheckAsync(actor, 1, new CancellationToken(canceled: true)));
    }
}
