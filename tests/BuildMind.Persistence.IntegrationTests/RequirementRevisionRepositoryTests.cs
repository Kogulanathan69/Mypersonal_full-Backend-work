using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using BuildMind.Infrastructure.Persistence.Transactions;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;
using Xunit.Abstractions;
using BuildMind.Infrastructure.Persistence.Connections;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// BE-M4-D03 live tests of the production schema and <see cref="RequirementRevisionRepository"/> on a disposable
/// MySQL 8.4 database (synthetic data only). The schema is created only by applying the committed, reviewed
/// initial migration <c>20261118133137_InitialFeatureSchema</c> (generated in CI because local dotnet-ef is blocked
/// by Smart App Control; see the D03 register).
/// </summary>
public sealed class RequirementRevisionRepositoryTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 18, 10, 20, 30, TimeSpan.Zero).AddTicks(1_234_560);

    [Fact]
    public async Task R01_schema_has_the_expected_tables_keys_and_indexes()
    {
        await using var database = await DisposableMySqlDatabase.CreateAsync();
        await CreateSchemaAsync(database);
        await using (var probe = CreateContext(database.ConnectionString))
        {
            output.WriteLine("Schema path: migrations (" + string.Join(", ", await probe.Database.GetAppliedMigrationsAsync()) + ")");
        }

        var tables = await ListAsync(database, "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name <> '__EFMigrationsHistory' ORDER BY table_name;");
        var constraints = await ListAsync(database,
            "SELECT constraint_name FROM information_schema.table_constraints WHERE table_schema = DATABASE() AND constraint_type IN ('FOREIGN KEY','UNIQUE') ORDER BY constraint_name;");
        output.WriteLine("Tables: " + string.Join(", ", tables));
        output.WriteLine("FK/unique: " + string.Join(", ", constraints));

        // M4 baseline tables plus the additive Captain feature tables (see BuildMindMigrationTests.CaptainMigrations).
        Assert.Equal(new[] { "password_reset_tokens", "plan_revisions", "plans", "project_requirement_heads", "projects", "refresh_tokens", "requirement_revisions", "users" }
            .Concat(BuildMindMigrationTests.CaptainMigrations.SelectMany(m => m.Tables)).Order(StringComparer.Ordinal), tables);
        Assert.Contains("fk_refresh_tokens_users_user_id", constraints);
        Assert.Contains("ux_refresh_tokens_token_hash", constraints);
        Assert.Contains("fk_password_reset_tokens_users_user_id", constraints);
        Assert.Equal("bigint", await ColumnTypeAsync(database, "users", "token_version"));
        Assert.Equal("bigint", await ColumnTypeAsync(database, "projects", "authorization_version"));
        Assert.Contains("fk_projects_users_owner_id", constraints);
        Assert.Contains("fk_requirement_revisions_projects_project_id", constraints);
        Assert.Contains("fk_project_requirement_heads_requirement_revisions_current", constraints);
        Assert.Contains("ux_users_normalized_email", constraints);
        Assert.Contains("ux_requirement_revisions_project_id_revision_number", constraints);
        Assert.Equal("json", await ColumnTypeAsync(database, "requirement_revisions", "content_json"));
        Assert.Equal("datetime(6)", await ColumnTypeAsync(database, "requirement_revisions", "created_at_utc"));
    }

    [Fact]
    public async Task R02_first_save_creates_the_revision_and_head()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        var revision = Revision(projectId, 1);

        var result = await Save(database, null, revision);

        Assert.Equal(RequirementSaveStatus.Saved, result.Status);
        var current = await Read(database, r => r.GetCurrentAsync(projectId, CancellationToken.None));
        Assert.NotNull(current);
        Assert.Equal(revision.Id, current!.Id);
        Assert.Equal(Now, current.CreatedAtUtc);                     // microsecond UTC round trip
        Assert.Equal(revision.Content.GroundFloorRooms, current.Content.GroundFloorRooms); // JSON value objects
    }

    [Fact]
    public async Task R03_next_save_with_the_current_revision_moves_the_head_and_keeps_history()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        var first = Revision(projectId, 1);
        var second = Revision(projectId, 2);
        await Save(database, null, first);

        var result = await Save(database, first.Id, second);

        Assert.Equal(RequirementSaveStatus.Saved, result.Status);
        Assert.Equal(second.Id, (await Read(database, r => r.GetCurrentAsync(projectId, CancellationToken.None)))!.Id);
        Assert.NotNull(await Read(database, r => r.GetAsync(projectId, first.Id, CancellationToken.None))); // never overwritten
    }

    [Fact]
    public async Task R04_stale_or_unexpected_head_is_a_conflict_and_changes_nothing()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        var first = Revision(projectId, 1);
        await Save(database, null, first);
        await Save(database, first.Id, Revision(projectId, 2));

        var stale = await Save(database, first.Id, Revision(projectId, 2));      // head already moved
        var firstAgain = await Save(database, null, Revision(projectId, 1));      // a head already exists

        Assert.Equal(RequirementSaveStatus.RevisionConflict, stale.Status);
        Assert.Equal(RequirementSaveStatus.RevisionConflict, firstAgain.Status);
        Assert.Null(stale.SavedRevision);
        Assert.Equal(2L, await CountAsync(database, "requirement_revisions"));
    }

    [Fact]
    public async Task R05_missing_project_is_reported_and_nothing_is_written()
    {
        await using var database = await SeededDatabaseAsync();

        var result = await Save(database, null, Revision(Guid.NewGuid(), 1));

        Assert.Equal(RequirementSaveStatus.ProjectNotFound, result.Status);
        Assert.Equal(0L, await CountAsync(database, "requirement_revisions"));
    }

    [Fact]
    public async Task R06_reads_are_scoped_to_the_project()
    {
        await using var database = await SeededDatabaseAsync();
        var projectA = await SeedProjectAsync(database);
        var projectB = await SeedProjectAsync(database);
        var revisionA = Revision(projectA, 1);
        await Save(database, null, revisionA);

        Assert.Null(await Read(database, r => r.GetAsync(projectB, revisionA.Id, CancellationToken.None)));
        Assert.Null(await Read(database, r => r.GetCurrentAsync(projectB, CancellationToken.None)));
        Assert.NotNull(await Read(database, r => r.GetAsync(projectA, revisionA.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task R07_concurrent_first_saves_have_exactly_one_winner()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Outcome(() => Save(database, null, Revision(projectId, 1)))));

        AssertExactlyOneWinnerAndTruthfulLosers(outcomes);
        Assert.Equal(1L, await CountAsync(database, "requirement_revisions"));
    }

    [Fact]
    public async Task R11_reusing_an_existing_revision_identity_is_rejected_as_a_caller_error()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        var first = Revision(projectId, 1);
        await Save(database, null, first);
        var reused = new RequirementRevision(first.Id, projectId, 2, "requirements-v1", "catalogue-2026.11", first.Content, Now);

        await Assert.ThrowsAsync<ArgumentException>(() => Save(database, first.Id, reused));

        Assert.Equal(first.Id, (await Read(database, r => r.GetCurrentAsync(projectId, CancellationToken.None)))!.Id);
        Assert.Equal(1L, await CountAsync(database, "requirement_revisions"));
    }

    /// <summary>
    /// Losers must report the truth: either RevisionConflict (head verifiably moved) or the transient, fully
    /// rolled-back deadlock they hit before the winner committed. Never a silent overwrite or a second winner.
    /// </summary>
    private static void AssertExactlyOneWinnerAndTruthfulLosers((RequirementSaveResult? Result, Exception? Error)[] outcomes)
    {
        Assert.Equal(1, outcomes.Count(o => o.Result?.Status == RequirementSaveStatus.Saved));
        Assert.All(outcomes.Where(o => o.Result?.Status != RequirementSaveStatus.Saved), o =>
        {
            if (o.Error is null)
            {
                Assert.Equal(RequirementSaveStatus.RevisionConflict, o.Result!.Status);
                Assert.Null(o.Result.SavedRevision);
            }
            else
            {
                var failure = BuildMind.Infrastructure.Persistence.Errors.PersistenceErrorClassifier.Classify(o.Error);
                Assert.True(failure.ServerRolledBackTransaction, $"Unexpected loser failure: {failure.Kind}");
            }
        });
    }

    private static async Task<(RequirementSaveResult? Result, Exception? Error)> Outcome(Func<Task<RequirementSaveResult>> save)
    {
        try
        {
            return (await save(), null);
        }
        catch (Exception exception) when (exception is DbUpdateException or MySqlException)
        {
            return (null, exception);
        }
    }

    [Fact]
    public async Task R08_concurrent_next_saves_have_exactly_one_winner()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        var first = Revision(projectId, 1);
        await Save(database, null, first);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Outcome(() => Save(database, first.Id, Revision(projectId, 2)))));

        AssertExactlyOneWinnerAndTruthfulLosers(outcomes);
        Assert.Equal(2L, await CountAsync(database, "requirement_revisions"));
    }

    [Fact]
    public async Task R09_cancellation_before_saving_writes_nothing()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await using var context = CreateContext(database.ConnectionString);
        var repository = new RequirementRevisionRepository(context, new TransactionRunner(new DbContextTransactionSource(context)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.TrySaveAtomicallyAsync(null, Revision(projectId, 1), cancelled.Token));

        Assert.Equal(0L, await CountAsync(database, "requirement_revisions"));
    }

    [Fact]
    public async Task R10_constraints_reject_orphans_and_duplicate_emails_without_leaking_values()
    {
        await using var database = await SeededDatabaseAsync();
        await SeedProjectAsync(database, "dup@example.invalid");

        await using var context = CreateContext(database.ConnectionString);
        context.Add(new ProjectRecord { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Name = "orphan", Status = "Created", Revision = 1, CreatedAtUtc = Now });
        var orphan = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(BuildMind.Infrastructure.Persistence.Errors.PersistenceFailureKind.ForeignKeyViolation,
            BuildMind.Infrastructure.Persistence.Errors.PersistenceErrorClassifier.Classify(orphan).Kind);

        context.ChangeTracker.Clear();
        context.Add(User("dup@example.invalid"));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var failure = BuildMind.Infrastructure.Persistence.Errors.PersistenceErrorClassifier.Classify(duplicate);
        Assert.Equal("ux_users_normalized_email", failure.ConstraintName);
        Assert.DoesNotContain("dup@example.invalid", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protected_save_rechecks_transfer_and_version_without_writing_or_disclosing()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        await using var change = CreateContext(database.ConnectionString);
        var project = await change.Set<ProjectRecord>().AsNoTracking().SingleAsync(p => p.Id == projectId);
        var nextOwner = User("transfer-target@example.invalid");
        change.Add(nextOwner);
        await change.SaveChangesAsync();
        // Synthetic transfer evidence only. This fixture does not define a product lifecycle writer.
        await change.Set<ProjectRecord>().Where(p => p.Id == projectId).ExecuteUpdateAsync(update => update
            .SetProperty(p => p.OwnerId, nextOwner.Id)
            .SetProperty(p => p.AuthorizationVersion, p => p.AuthorizationVersion + 1));

        foreach (var (actor, version) in new[] { (project.OwnerId, 1L), (nextOwner.Id, 1L), (Guid.NewGuid(), 2L) })
        {
            var denied = await Read(database, repository => repository.TrySaveAuthorizedAtomicallyAsync(
                actor, version, null, Revision(projectId, 1), default));
            Assert.Equal(RequirementSaveStatus.ProjectNotFound, denied.Status);
            Assert.Null(denied.SavedRevision);
        }
        Assert.Equal(0, await CountAsync(database, "requirement_revisions"));
        Assert.Equal(0, await CountAsync(database, "project_requirement_heads"));

        var current = await Read(database, repository => repository.TrySaveAuthorizedAtomicallyAsync(
            nextOwner.Id, 2, null, Revision(projectId, 1), default));
        Assert.Equal(RequirementSaveStatus.Saved, current.Status);
    }

    [Fact]
    public async Task Competing_protected_writes_have_one_winner_and_no_losing_revision()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        await using var probe = CreateContext(database.ConnectionString);
        var actor = await probe.Set<ProjectRecord>().Where(p => p.Id == projectId).Select(p => p.OwnerId).SingleAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenders = Enumerable.Range(0, 2).Select(_ => Read(database, async repository =>
        {
            await start.Task;
            return await repository.TrySaveAuthorizedAtomicallyAsync(actor, 1, null, Revision(projectId, 1), default);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(contenders);
        Assert.Single(results, result => result.Status == RequirementSaveStatus.Saved);
        Assert.Null(Assert.Single(results, result => result.Status == RequirementSaveStatus.RevisionConflict).SavedRevision);
        Assert.Equal(1, await CountAsync(database, "requirement_revisions"));
        Assert.Equal(1, await CountAsync(database, "project_requirement_heads"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Protected_save_rolls_back_an_insert_when_failure_or_cancellation_follows(bool cancel)
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        using var cancellation = new CancellationTokenSource();
        await using var context = CreateContext(database.ConnectionString, new AbortAfterInsert(cancellation, cancel));
        var actor = await context.Set<ProjectRecord>().Where(p => p.Id == projectId).Select(p => p.OwnerId).SingleAsync();
        var repository = new RequirementRevisionRepository(context, new TransactionRunner(new DbContextTransactionSource(context)));
        Task<RequirementSaveResult> SaveProtected() => repository.TrySaveAuthorizedAtomicallyAsync(
            actor, 1, null, Revision(projectId, 1), cancellation.Token);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(SaveProtected);
        else
            await Assert.ThrowsAsync<DbUpdateException>(SaveProtected);

        Assert.Equal(0, await CountAsync(database, "requirement_revisions"));
        Assert.Equal(0, await CountAsync(database, "project_requirement_heads"));
    }

    [Fact]
    public async Task Protected_save_holds_the_project_lock_before_the_first_feature_write()
    {
        await using var database = await SeededDatabaseAsync();
        var projectId = await SeedProjectAsync(database);
        await using var probe = CreateContext(database.ConnectionString);
        var actor = await probe.Set<ProjectRecord>().Where(p => p.Id == projectId).Select(p => p.OwnerId).SingleAsync();
        var interceptor = new BeforeInsert(async token =>
        {
            await using var transaction = await probe.Database.BeginTransactionAsync(token);
            var failure = await Assert.ThrowsAsync<MySqlException>(() => probe.Database.SqlQuery<long>($"""
                SELECT authorization_version AS `Value` FROM projects WHERE id = {projectId} FOR UPDATE NOWAIT
                """).ToListAsync(token));
            Assert.Equal(3572, failure.Number);
        });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var repository = new RequirementRevisionRepository(context, new TransactionRunner(new DbContextTransactionSource(context)));
        var result = await repository.TrySaveAuthorizedAtomicallyAsync(actor, 1, null, Revision(projectId, 1), default);
        Assert.True(interceptor.Observed);
        Assert.Equal(RequirementSaveStatus.Saved, result.Status);
    }

    private sealed class BeforeInsert(Func<CancellationToken, Task> probe) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Observed)
            {
                await probe(cancellationToken);
                Observed = true;
            }
            return result;
        }
    }

    private sealed class AbortAfterInsert(CancellationTokenSource source, bool cancel) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!cancel)
                throw new DbUpdateException("Synthetic database failure after the revision insert.");

            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private static RequirementRevision Revision(Guid projectId, long number) => new(
        Guid.NewGuid(),
        projectId,
        number,
        "requirements-v1",
        "catalogue-2026.11",
        new RequirementContent(2, [new RoomCount("bedroom", 3), new RoomCount("kitchen", 1)],
            relationships: [new RoomRelationshipPreference("kitchen", "bedroom", "not-adjacent")]),
        Now);

    private static UserRecord User(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "synthetic-not-a-real-hash",
        IsActive = true,
        CreatedAtUtc = Now,
    };

    private static BuildMindDbContext CreateContext(string connectionString, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, connectionString, 30);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new BuildMindDbContext(builder.Options);
    }

    /// <summary>
    /// Creates the schema ONLY from the committed, reviewed migration (STEP 06-E3). The interim EnsureCreated
    /// fallback was removed: a missing migration now fails the tests instead of silently using another path.
    /// </summary>
    private static async Task CreateSchemaAsync(DisposableMySqlDatabase database)
    {
        await using var context = CreateContext(database.ConnectionString);
        Assert.Contains("20261118133137_InitialFeatureSchema", context.Database.GetMigrations());
        await context.Database.MigrateAsync();
    }

    private static async Task<DisposableMySqlDatabase> SeededDatabaseAsync()
    {
        var database = await DisposableMySqlDatabase.CreateAsync();
        try
        {
            await CreateSchemaAsync(database);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task<Guid> SeedProjectAsync(DisposableMySqlDatabase database, string? email = null)
    {
        await using var context = CreateContext(database.ConnectionString);
        var owner = User(email ?? $"{Guid.NewGuid():N}@example.invalid");
        var project = new ProjectRecord { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "synthetic project", Status = "Created", Revision = 1, CreatedAtUtc = Now };
        context.AddRange(owner, project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<RequirementSaveResult> Save(DisposableMySqlDatabase database, Guid? expected, RequirementRevision revision)
    {
        await using var context = CreateContext(database.ConnectionString);
        var repository = new RequirementRevisionRepository(context, new TransactionRunner(new DbContextTransactionSource(context)));
        return await repository.TrySaveAtomicallyAsync(expected, revision, CancellationToken.None);
    }

    private static async Task<T> Read<T>(DisposableMySqlDatabase database, Func<RequirementRevisionRepository, Task<T>> read)
    {
        await using var context = CreateContext(database.ConnectionString);
        return await read(new RequirementRevisionRepository(context, new TransactionRunner(new DbContextTransactionSource(context))));
    }

    private static async Task<long> CountAsync(DisposableMySqlDatabase database, string table) =>
        Convert.ToInt64((await ListAsync(database, $"SELECT COUNT(*) FROM `{table}`;")).Single());

    private static async Task<string?> ColumnTypeAsync(DisposableMySqlDatabase database, string table, string column) =>
        (await ListAsync(database,
            $"SELECT column_type FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{table}' AND column_name = '{column}';"))
        .SingleOrDefault();

    private static async Task<List<string>> ListAsync(DisposableMySqlDatabase database, string sql)
    {
        await using var connection = new MySqlConnection(database.ConnectionString);
        await MySqlConnectionOpenGate.OpenAsync(connection);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return values;
    }
}
