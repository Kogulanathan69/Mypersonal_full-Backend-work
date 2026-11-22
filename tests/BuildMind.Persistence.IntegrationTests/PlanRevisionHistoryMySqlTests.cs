using System.Data.Common;
using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Repositories.PlanEditor;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;

namespace BuildMind.Persistence.IntegrationTests;

public sealed class PlanRevisionHistoryMySqlTests
{
    [Fact]
    public async Task History_preserves_scope_payload_and_original_head_across_an_append()
    {
        await using var database = await DisposableMySqlDatabase.CreateAsync();
        await using var writer = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        await using var reader = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        await writer.Database.MigrateAsync();
        var (plan, owner) = await SeedPlanAsync(writer);
        var otherPlan = new PlanRecord { ProjectId = plan.ProjectId, PlanId = Guid.NewGuid() };
        writer.Add(otherPlan);
        await writer.SaveChangesAsync();
        var port = new MySqlPlanRevisionHistoryQuery(reader);
        var service = new PlanRevisionHistoryService(new CurrentUser(owner), new ProjectAuthorizationReader(reader), port);
        var query = new PlanRevisionHistoryQuery(plan.ProjectId, plan.PlanId, 2);

        Assert.Empty((await service.ListAsync(query)).Revisions!);
        Assert.Equal(PlanRevisionHistorySourceStatus.NotFound,
            (await port.ReadPageAsync(Guid.NewGuid(), plan.PlanId, null, 1, default)).Status);
        var first = await AppendAsync(writer, plan, owner, 1);
        var second = await AppendAsync(writer, plan, owner, 2);
        var third = await AppendAsync(writer, plan, owner, 3);
        await using var concurrentWriter = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        var writablePlan = await concurrentWriter.Set<PlanRecord>().SingleAsync(row => row.PlanId == plan.PlanId);
        PlanRevisionSnapshot? fourth = null;
        var options = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(options, database.ConnectionString, 60);
        options.AddInterceptors(new AdvanceAfterPlanRead(async () =>
        {
            fourth = await AppendAsync(concurrentWriter, writablePlan, owner, 4);
        }));
        await using var snapshotReader = new BuildMindDbContext(options.Options);
        var racingService = new PlanRevisionHistoryService(new CurrentUser(owner), new ProjectAuthorizationReader(snapshotReader),
            new MySqlPlanRevisionHistoryQuery(snapshotReader));
        var page = await racingService.ListAsync(query);
        Assert.Equal(PlanRevisionHistoryStatus.Succeeded, page.Status);
        Assert.Equal(new[] { third.RevisionId, second.RevisionId }, page.Revisions!.Select(row => row.RevisionId));
        var cursor = Assert.IsType<PlanRevisionHistoryCursor>(page.NextCursor);

        var continuation = await service.ListAsync(query with { Cursor = cursor });
        Assert.Equal(first.RevisionId, Assert.Single(continuation.Revisions!).RevisionId);
        Assert.Null(continuation.NextCursor);
        Assert.Equal(Assert.IsType<PlanRevisionSnapshot>(fourth).RevisionId, (await service.ListAsync(query)).Revisions![0].RevisionId);
        Assert.Equal(4, (await port.ReadPageAsync(plan.ProjectId, plan.PlanId, null, 50, default)).Revisions!.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => port.ReadPageAsync(plan.ProjectId, plan.PlanId, null, 51, default));

        foreach (var changed in new[]
                 {
                     cursor with { HeadRevisionId = Guid.NewGuid() },
                     cursor with { AfterRevisionId = Guid.NewGuid() },
                     cursor with { ExpectedNextRevisionId = Guid.NewGuid() },
                     cursor with { PlanId = otherPlan.PlanId }
                 })
        {
            var invalid = await port.ReadPageAsync(plan.ProjectId, plan.PlanId, changed, 2, default);
            Assert.Equal(PlanRevisionHistorySourceStatus.InvalidCursor, invalid.Status);
            Assert.Null(invalid.Revisions);
        }

        var exact = (await service.GetAsync(plan.ProjectId, plan.PlanId, first.RevisionId)).Revision!;
        Assert.Equal(first.GeometryJson, exact.GeometryJson);
        Assert.Equal(first.GeometryHash, exact.GeometryHash);
        Assert.Equal(first.Validation.ValidationId, exact.Validation.ValidationId);
        Assert.Null(exact.Validation.RequirementsMatch);
        Assert.Equal(first.CreatedAtUtc, exact.CreatedAtUtc);
        Assert.Equal(PlanRevisionHistorySourceStatus.NotFound,
            (await port.ReadRevisionAsync(plan.ProjectId, otherPlan.PlanId, first.RevisionId, default)).Status);
        Assert.Equal(PlanRevisionHistorySourceStatus.NotFound,
            (await port.ReadRevisionAsync(Guid.NewGuid(), plan.PlanId, first.RevisionId, default)).Status);

        var outsider = new PlanRevisionHistoryService(new CurrentUser(Guid.NewGuid()), new ProjectAuthorizationReader(reader), port);
        var denied = await outsider.ListAsync(query);
        Assert.Equal(PlanRevisionHistoryStatus.DeniedOrNotFound, denied.Status);
        Assert.Null(denied.Revisions);
    }

    [Fact]
    public async Task Corruption_and_version_changes_withhold_protected_data()
    {
        await using var database = await DisposableMySqlDatabase.CreateAsync();
        await using var writer = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        await using var reader = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        await writer.Database.MigrateAsync();
        var (plan, owner) = await SeedPlanAsync(writer);
        var first = await AppendAsync(writer, plan, owner, 1);
        var second = await AppendAsync(writer, plan, owner, 2);
        var third = await AppendAsync(writer, plan, owner, 3);
        var port = new MySqlPlanRevisionHistoryQuery(reader);
        var authorization = new ProjectAuthorizationReader(reader);
        var service = new PlanRevisionHistoryService(new CurrentUser(owner), authorization, port);
        var query = new PlanRevisionHistoryQuery(plan.ProjectId, plan.PlanId, 2);

        // Inject changed evidence between real reads to exercise M3's comparison. This is a test-only SQL
        // change and makes no claim about which M1 write is required to advance the version.
        var before = await authorization.AuthorizeOwnerAsync(plan.ProjectId, owner, default);
        var changedPort = new AfterPageRead(port, async () =>
        {
            await writer.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE projects SET authorization_version = authorization_version + 1 WHERE id = {plan.ProjectId};
                """);
        });
        var changedService = new PlanRevisionHistoryService(new CurrentUser(owner), authorization, changedPort);
        var changed = await changedService.ListAsync(query);
        Assert.Equal(PlanRevisionHistoryStatus.DeniedOrNotFound, changed.Status);
        Assert.Null(changed.Revisions);
        Assert.NotEqual(before.Snapshot!.Version, (await authorization.AuthorizeOwnerAsync(plan.ProjectId, owner, default)).Snapshot!.Version);

        var emptyId = Guid.Empty;
        await writer.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE plan_revisions SET validation_id = {emptyId}
            WHERE project_id = {plan.ProjectId} AND plan_id = {plan.PlanId} AND revision_id = {third.RevisionId};
            """);
        Assert.Null((await service.ListAsync(query)).Revisions);
        await Assert.ThrowsAsync<InvalidDataException>(() => port.ReadRevisionAsync(plan.ProjectId, plan.PlanId, third.RevisionId, default));

        await writer.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE plan_revisions SET geometry_json = CONCAT(geometry_json, ' ')
            WHERE project_id = {plan.ProjectId} AND plan_id = {plan.PlanId} AND revision_id = {second.RevisionId};
            """);
        await Assert.ThrowsAsync<InvalidDataException>(() => port.ReadRevisionAsync(plan.ProjectId, plan.PlanId, second.RevisionId, default));

        await writer.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE plan_revisions SET parent_revision_id = {first.RevisionId}
            WHERE project_id = {plan.ProjectId} AND plan_id = {plan.PlanId} AND revision_id = {third.RevisionId};
            """);
        var forged = new PlanRevisionHistoryCursor(plan.ProjectId, plan.PlanId, 3, third.RevisionId, 2, second.RevisionId, first.RevisionId);
        Assert.Equal(PlanRevisionHistorySourceStatus.InvalidCursor,
            (await port.ReadPageAsync(plan.ProjectId, plan.PlanId, forged, 1, default)).Status);
        await Assert.ThrowsAsync<InvalidDataException>(() => port.ReadRevisionAsync(plan.ProjectId, plan.PlanId, first.RevisionId, default));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.ReadPageAsync(plan.ProjectId, plan.PlanId, null, 1, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authorization.AuthorizeOwnerAsync(plan.ProjectId, owner, cancellation.Token));

        var unavailable = new MySqlConnectionStringBuilder(database.ConnectionString) { Port = 1, ConnectionTimeout = 1, Pooling = false };
        await using var unavailableContext = BuildMindMigrationTests.CreateContext(unavailable.ConnectionString);
        var unavailableService = new PlanRevisionHistoryService(new CurrentUser(owner), authorization,
            new MySqlPlanRevisionHistoryQuery(unavailableContext));
        var failure = await unavailableService.ListAsync(query);
        Assert.Equal(PlanRevisionHistoryStatus.DependencyUnavailable, failure.Status);
        Assert.Null(failure.Revisions);
    }

    private static async Task<(PlanRecord Plan, Guid Owner)> SeedPlanAsync(BuildMindDbContext context)
    {
        var plan = new PlanRecord { ProjectId = Guid.NewGuid(), PlanId = Guid.NewGuid() };
        var owner = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, normalized_email, password_hash, is_active, created_at_utc)
            VALUES ({owner}, 'history@example.invalid', 'HISTORY@EXAMPLE.INVALID', 'synthetic', 1, UTC_TIMESTAMP(6));
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projects (id, owner_id, name, status, revision, created_at_utc)
            VALUES ({plan.ProjectId}, {owner}, 'Synthetic', 'Created', 1, UTC_TIMESTAMP(6));
            """);
        context.Add(plan);
        await context.SaveChangesAsync();
        return (plan, owner);
    }

    private static async Task<PlanRevisionSnapshot> AppendAsync(BuildMindDbContext context, PlanRecord plan, Guid owner, long version)
    {
        var geometry = new PlanGeometryDto
        {
            SchemaVersion = GeometrySchemaInfo.DraftVersion, ProjectId = plan.ProjectId, PlanId = plan.PlanId,
            LandRevisionId = Guid.NewGuid(), RequirementsRevisionId = Guid.NewGuid(),
            CoordinateSystem = new CoordinateSystemDto
            {
                Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 },
                XDirection = "right", YDirection = "up"
            },
            Floors = [new FloorDto { Id = Guid.NewGuid(), Level = 0, Rooms = [], Walls = [], Openings = [] }]
        };
        var json = " \r\n" + JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var instant = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
        var revision = new PlanRevisionSnapshot(Guid.NewGuid(), plan.PlanId, plan.ProjectId, plan.HeadRevisionId,
            version, geometry.SchemaVersion, json, hash, geometry.LandRevisionId, geometry.RequirementsRevisionId,
            new PlanValidationReferenceDto
            {
                ValidationId = Guid.NewGuid(), ValidatorVersion = "test-only/1", GeometryHash = hash,
                GeometryValid = true, RequirementsMatch = null, RegulatoryEvidenceStatus = "SYNTHETIC",
                HasBlockingIssues = false, ValidatedAtUtc = instant
            }, Guid.NewGuid(), owner, instant, $"Synthetic revision {version}");
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Add(PlanRevisionMapper.ToRecord(revision));
        await context.SaveChangesAsync();
        plan.HeadRevisionId = revision.RevisionId;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return revision;
    }

    private sealed class CurrentUser(Guid actor) : ICurrentUser
    {
        public Guid? UserId => actor;
        public bool IsAuthenticated => true;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => actor;
    }

    private sealed class AdvanceAfterPlanRead(Func<Task> advance) : DbCommandInterceptor
    {
        private bool advanced;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!advanced && command.CommandText.Contains("FROM `plans`", StringComparison.OrdinalIgnoreCase))
            {
                advanced = true;
                await advance();
            }

            return result;
        }
    }

    private sealed class AfterPageRead(IPlanRevisionHistoryQueryPort inner, Func<Task> afterRead) : IPlanRevisionHistoryQueryPort
    {
        public async Task<PlanRevisionHistorySourcePage> ReadPageAsync(Guid projectId, Guid planId,
            PlanRevisionHistoryCursor? cursor, int pageSize, CancellationToken cancellationToken)
        {
            var page = await inner.ReadPageAsync(projectId, planId, cursor, pageSize, cancellationToken);
            await afterRead();
            return page;
        }

        public Task<PlanRevisionHistorySourceRevision> ReadRevisionAsync(Guid projectId, Guid planId, Guid revisionId,
            CancellationToken cancellationToken) => inner.ReadRevisionAsync(projectId, planId, revisionId, cancellationToken);
    }
}
