using System.Text;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit;

namespace BuildMind.Persistence.IntegrationTests;

public sealed class PlanRevisionSchemaTests
{
    [Fact]
    public async Task Schema_preserves_exact_geometry_and_rejects_invalid_scope_and_versions()
    {
        await using var database = await DisposableMySqlDatabase.CreateAsync();
        await using var context = BuildMindMigrationTests.CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();

        var projectId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, normalized_email, password_hash, is_active, created_at_utc)
            VALUES ({ownerId}, 'schema@example.invalid', 'SCHEMA@EXAMPLE.INVALID', 'synthetic', 1, UTC_TIMESTAMP(6));
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projects (id, owner_id, name, status, revision, created_at_utc)
            VALUES ({projectId}, {ownerId}, 'Synthetic', 'Created', 1, UTC_TIMESTAMP(6));
            """);

        var plan = new PlanRecord { ProjectId = projectId, PlanId = Guid.NewGuid() };
        var otherPlan = new PlanRecord { ProjectId = projectId, PlanId = Guid.NewGuid() };
        context.AddRange(plan, otherPlan);
        await context.SaveChangesAsync();

        const string geometry = " {\r\n  \"label\": \"ශ්‍රී ලංකාව 🏠\", \"x\": 1.00\r\n}\n";
        var instant = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
        var root = Revision(plan.PlanId, 1, null, geometry);
        context.Add(root);
        await context.SaveChangesAsync();
        plan.HeadRevisionId = root.RevisionId;
        await context.SaveChangesAsync();

        var revisions = context.Set<PlanRevisionRecord>();
        var stored = await revisions.AsNoTracking().SingleAsync();
        Assert.Equal(geometry, stored.GeometryJson);
        Assert.Equal(PlanRevisionService.ComputeGeometryHash(geometry), stored.GeometryHash);
        Assert.Equal(root.ValidationGeometryHash, stored.ValidationGeometryHash);
        Assert.Null(stored.RequirementsMatch);
        Assert.Equal(instant, stored.CreatedAtUtc);
        Assert.Equal(instant, stored.ValidatedAtUtc);
        Assert.Equal(TimeSpan.Zero, stored.ValidatedAtUtc.Offset);

        await AssertRejectedAsync(1062, Revision(plan.PlanId, 1, null, geometry));
        await AssertRejectedAsync(1452, Revision(otherPlan.PlanId, 2, root.RevisionId, geometry));
        await AssertRejectedAsync(3819, Revision(otherPlan.PlanId, 2, null, geometry));
        var oversized = "\"" + new string('é', PlanRevisionRecord.ProvisionalGeometryMaxBytes / 2) + "\"";
        await AssertRejectedAsync(3819, Revision(otherPlan.PlanId, 1, null, oversized));

        var atLimit = geometry + new string(' ', PlanRevisionRecord.ProvisionalGeometryMaxBytes - Encoding.UTF8.GetByteCount(geometry));
        var boundary = Revision(otherPlan.PlanId, 1, null, atLimit);
        context.Add(boundary);
        await context.SaveChangesAsync();
        var persistedBoundary = await revisions.AsNoTracking().SingleAsync(row => row.RevisionId == boundary.RevisionId);
        Assert.Equal(PlanRevisionRecord.ProvisionalGeometryMaxBytes, Encoding.UTF8.GetByteCount(persistedBoundary.GeometryJson));
        Assert.Equal(atLimit, persistedBoundary.GeometryJson);
        Assert.Equal(boundary.GeometryHash, persistedBoundary.GeometryHash);

        var invalidHead = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE plans SET head_revision_id = {root.RevisionId}
                WHERE project_id = {projectId} AND plan_id = {otherPlan.PlanId};
                """));
        Assert.Equal(1452, invalidHead.Number);

        PlanRevisionRecord Revision(Guid planId, long version, Guid? parentId, string json) => new()
        {
            ProjectId = projectId,
            PlanId = planId,
            RevisionId = Guid.NewGuid(),
            VersionNumber = version,
            ParentRevisionId = parentId,
            GeometrySchemaVersion = "synthetic",
            GeometryJson = json,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(json),
            LandRevisionId = Guid.NewGuid(),
            RequirementsRevisionId = Guid.NewGuid(),
            CreatedBy = ownerId,
            CreatedAtUtc = instant,
            ValidationId = Guid.NewGuid(),
            ValidatorVersion = "synthetic",
            ValidationGeometryHash = PlanRevisionService.ComputeGeometryHash(json),
            GeometryValid = true,
            RegulatoryEvidenceStatus = "synthetic",
            ValidatedAtUtc = instant
        };

        async Task AssertRejectedAsync(int errorNumber, PlanRevisionRecord revision)
        {
            await using var writeContext = BuildMindMigrationTests.CreateContext(database.ConnectionString);
            writeContext.Add(revision);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
            Assert.Equal(errorNumber, Assert.IsType<MySqlException>(error.InnerException).Number);
        }
    }
}
