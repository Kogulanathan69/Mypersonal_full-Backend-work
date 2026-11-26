using BuildMind.API.Errors;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// BE-M4-D06 on a disposable MySQL 8.4 database with the committed migration: lost-update prevention on the
/// <c>projects.revision</c> concurrency token, and the public mapping of REAL provider failures (duplicate key,
/// stale token, unreachable server). Synthetic data only.
/// </summary>
public sealed class PersistenceConcurrencyLiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CC01_stale_project_revision_is_rejected_and_maps_to_revision_conflict()
    {
        await using var database = await MigratedAsync();
        var projectId = await SeedProjectAsync(database);
        await using var first = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        await using var second = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        var a = await first.Set<ProjectRecord>().SingleAsync(p => p.Id == projectId);
        var b = await second.Set<ProjectRecord>().SingleAsync(p => p.Id == projectId);

        a.Name = "first writer";
        a.Revision++;
        await first.SaveChangesAsync();

        b.Name = "second writer (stale)";
        b.Revision++;
        var stale = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(stale, out var failure));
        Assert.Equal((409, SharedErrorCodes.RevisionConflict), (PersistenceProblemMapping.Map(failure).Status, PersistenceProblemMapping.Map(failure).Code));
        await using var reader = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        var persisted = await reader.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId);
        Assert.Equal(("first writer", 2L), (persisted.Name, persisted.Revision)); // no lost update
    }

    [Fact]
    public async Task CC02_parallel_project_updates_have_exactly_one_winner()
    {
        await using var database = await MigratedAsync();
        var projectId = await SeedProjectAsync(database);

        var updated = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var context = BuildMindMigrationTests.CreateContext(database.ConnectionString);
            return await context.Set<ProjectRecord>()
                .Where(p => p.Id == projectId && p.Revision == 1)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Revision, 2L).SetProperty(p => p.Name, $"writer {i}"));
        }));

        Assert.Equal(1, updated.Sum());
    }

    [Fact]
    public async Task CC03_real_duplicate_key_maps_to_conflict_without_the_value()
    {
        await using var database = await MigratedAsync();
        await SeedProjectAsync(database, "dup@example.invalid");
        await using var context = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        context.Add(User("dup@example.invalid"));

        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(duplicate, out var failure));
        var mapping = PersistenceProblemMapping.Map(failure);
        Assert.Equal((409, SharedErrorCodes.Conflict), (mapping.Status, mapping.Code));
        Assert.Equal(("users", "ux_users_normalized_email"), (failure.ConstraintTable, failure.ConstraintName));
        Assert.DoesNotContain("dup@example.invalid", mapping.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dup@example.invalid", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CC04_unreachable_database_maps_to_503()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("BUILDMIND_TEST_MYSQL")
            ?? throw new InvalidOperationException("BUILDMIND_TEST_MYSQL is not set."))
        {
            Port = (uint)FreeLoopbackPort(),
            Database = "buildmind_test_unreachable",
            ConnectionTimeout = 3,
        };
        await using var context = BuildMindMigrationTests.CreateContext(builder.ConnectionString);

        var failure = await Record.ExceptionAsync(() => context.Database.OpenConnectionAsync());

        Assert.NotNull(failure);
        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(failure!, out var classified));
        var mapping = PersistenceProblemMapping.Map(classified);
        Assert.Equal((503, SharedErrorCodes.DependencyUnavailable), (mapping.Status, mapping.Code));
    }

    private static async Task<DisposableMySqlDatabase> MigratedAsync()
    {
        var database = await DisposableMySqlDatabase.CreateAsync();
        try
        {
            await using var context = BuildMindMigrationTests.CreateContext(database.ConnectionString);
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static UserRecord User(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "synthetic-not-a-real-hash",
        IsActive = true,
        CreatedAtUtc = Now,
    };

    private static async Task<Guid> SeedProjectAsync(DisposableMySqlDatabase database, string? email = null)
    {
        await using var context = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        var owner = User(email ?? $"{Guid.NewGuid():N}@example.invalid");
        var project = new ProjectRecord { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "synthetic project", Status = "Created", Revision = 1, CreatedAtUtc = Now };
        context.AddRange(owner, project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    private static int FreeLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
