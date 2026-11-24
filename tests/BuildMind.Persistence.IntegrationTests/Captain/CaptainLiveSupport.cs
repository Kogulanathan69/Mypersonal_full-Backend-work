using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Persistence.IntegrationTests.Captain;

/// <summary>Shared helpers for Captain live tests: migrated disposable database, synthetic owner and project.</summary>
internal static class CaptainLiveSupport
{
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    public static BuildMindDbContext Context(DisposableMySqlDatabase db)
    {
        // Unpooled: every disposable database would otherwise keep its own idle pool open after it is dropped, and the
        // full live suite then exhausts the server's max_connections (observed: 152/151). TLS settings are unchanged.
        var unpooled = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(db.ConnectionString) { Pooling = false };
        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, unpooled.ConnectionString, 30);
        return new BuildMindDbContext(builder.Options);
    }

    public static TransactionRunner Transactions(BuildMindDbContext context) =>
        new(new DbContextTransactionSource(context));

    public static async Task<DisposableMySqlDatabase> SchemaAsync()
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

    public static async Task<(Guid OwnerId, Guid ProjectId)> SeedAsync(DisposableMySqlDatabase db)
    {
        await using var context = Context(db);
        var ownerId = Guid.NewGuid();
        var email = $"{ownerId:N}@example.invalid";
        context.Add(new UserRecord
        {
            Id = ownerId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "synthetic-hash", IsActive = true, CreatedAtUtc = Now,
        });
        await context.SaveChangesAsync();
        var project = new Project { OwnerId = ownerId, Name = "synthetic project", CreatedAtUtc = Now };
        await new ProjectRepository(context).AddAsync(project, default);
        return (ownerId, project.Id);
    }

    public static async Task BumpAuthorizationVersionAsync(DisposableMySqlDatabase db, Guid projectId, long expected)
    {
        await using var context = Context(db);
        Assert.Equal(expected + 1, await new ProjectAuthorizationVersionStore(context).TryIncrementAsync(projectId, expected, default));
    }
}
