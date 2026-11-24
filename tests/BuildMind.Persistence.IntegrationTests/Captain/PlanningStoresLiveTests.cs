using System.Text.Json;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using Microsoft.EntityFrameworkCore;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;

namespace BuildMind.Persistence.IntegrationTests.Captain;

/// <summary>Captain planning persistence on disposable MySQL 8.4 (protected writes, CAS, idempotency).</summary>
public sealed class PlanningStoresLiveTests
{
    [Fact]
    public async Task L01_land_revisions_chain_with_cas_and_deny_non_owners()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        var (intruder, _) = await SeedAsync(db);
        await using var context = Context(db);
        var store = new MySqlLandRevisionStore(context, Transactions(context));

        var first = Land(project, owner, 1);
        Assert.Equal(ProtectedWriteStatus.Written, await store.TryAppendAsync(first, null, 1, default));
        Assert.Equal(ProtectedWriteStatus.Conflict, await store.TryAppendAsync(Land(project, owner, 1), null, 1, default));
        Assert.Equal(ProtectedWriteStatus.Written, await store.TryAppendAsync(Land(project, owner, 2), first.Id, 1, default));
        Assert.Equal(ProtectedWriteStatus.Denied, await store.TryAppendAsync(Land(project, intruder, 3), null, 1, default));

        var current = await store.GetCurrentAsync(project, default);
        Assert.Equal(2, current!.RevisionNumber);
        Assert.Equal(120, current.AreaM2);
        Assert.Equal(5, current.Land.Boundary.Exterior.Count);
    }

    [Fact]
    public async Task G01_jobs_are_idempotent_per_actor_key_and_complete_once_with_candidates()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var context = Context(db);
        var land = Land(project, owner, 1);
        await new MySqlLandRevisionStore(context, Transactions(context)).TryAppendAsync(land, null, 1, default);
        var jobs = new MySqlGenerationJobStore(context, Transactions(context));
        var running = Job(project, owner, land.Id, "key-00000001");

        Assert.Equal(ProtectedWriteStatus.Written, await jobs.TryStartAsync(running, 1, default));
        Assert.Equal(ProtectedWriteStatus.Conflict, await jobs.TryStartAsync(running with { Id = Guid.NewGuid() }, 1, default));

        var candidates = Enumerable.Range(0, 3).Select(i => Candidate(project, running, land.Id, i)).ToArray();
        var done = running with { Status = GenerationJobStatus.Succeeded, Outcome = "SUCCESS", AlgorithmVersion = "test", CompletedAtUtc = Now };
        Assert.Equal(ProtectedWriteStatus.Written, await jobs.TryCompleteAsync(done, candidates, 1, default));
        Assert.Equal(ProtectedWriteStatus.Conflict, await jobs.TryCompleteAsync(done, [], 1, default));

        var stored = await jobs.FindByKeyAsync(project, owner, "key-00000001", default);
        Assert.Equal(GenerationJobStatus.Succeeded, stored!.Status);
        var read = await jobs.GetCandidatesAsync(project, running.Id, default);
        Assert.Equal([0, 1, 2], read.Select(c => c.Ordinal));
        Assert.Equal(candidates[1].GeometryHash, read[1].GeometryHash);
        Assert.Empty(await jobs.GetCandidatesAsync(Guid.NewGuid(), running.Id, default));
    }

    [Fact]
    public async Task G02_stale_authorization_version_denies_job_writes()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var context = Context(db);
        var land = Land(project, owner, 1);
        await new MySqlLandRevisionStore(context, Transactions(context)).TryAppendAsync(land, null, 1, default);
        await BumpAuthorizationVersionAsync(db, project, 1);

        var jobs = new MySqlGenerationJobStore(context, Transactions(context));
        Assert.Equal(ProtectedWriteStatus.Denied, await jobs.TryStartAsync(Job(project, owner, land.Id, "key-00000002"), 1, default));
        Assert.Null(await jobs.FindByKeyAsync(project, owner, "key-00000002", default));
    }

    [Fact]
    public async Task P01_plan_creation_writes_plan_revision_and_evidence_once()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var context = Context(db);
        var land = Land(project, owner, 1);
        var store = new MySqlPlanCreationStore(context, Transactions(context));
        var planId = Guid.NewGuid();
        var geometry = Geometry(project, planId, land.Id, Guid.NewGuid());
        var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var reference = new PlanValidationReferenceDto
        {
            ValidationId = Guid.NewGuid(), ValidatorVersion = "captain-plan-validation/1", GeometryHash = hash, GeometryValid = true,
            RequirementsMatch = true, RegulatoryEvidenceStatus = PlanningSchemas.SyntheticEvidenceStatus, HasBlockingIssues = false,
            ValidatedAtUtc = Now,
        };
        var revision = new PlanRevisionSnapshot(Guid.NewGuid(), planId, project, null, 1, geometry.SchemaVersion, json, hash, land.Id,
            geometry.RequirementsRevisionId, reference, null, owner, Now, "first");
        var evidence = new IssuedValidationEvidence(project, planId, reference, []);

        Assert.Equal(ProtectedWriteStatus.Written, await store.TryCreateAsync(revision, evidence, 1, default));
        Assert.Equal(ProtectedWriteStatus.Conflict, await store.TryCreateAsync(revision with { RevisionId = Guid.NewGuid() }, evidence, 1, default));

        await using var check = Context(db);
        var plan = await check.Set<PlanRecord>().AsNoTracking().SingleAsync(p => p.PlanId == planId);
        Assert.Equal(revision.RevisionId, plan.HeadRevisionId);
        Assert.Equal(1, await check.Set<PlanRevisionRecord>().CountAsync(r => r.PlanId == planId));
        Assert.Equal(PlanningSchemas.SyntheticEvidenceStatus,
            (await check.Set<PlanValidationEvidenceRecord>().SingleAsync(e => e.ValidationId == reference.ValidationId)).RegulatoryEvidenceStatus);
    }

    internal static LandRevision Land(Guid project, Guid actor, long number) => new(Guid.NewGuid(), project, number, new MetricLandDto
    {
        SchemaVersion = PlanningSchemas.MetricLand,
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 }, XDirection = "right", YDirection = "up",
        },
        Boundary = new PolygonBoundaryDto
        {
            Exterior = new[] { (0d, 0d), (12d, 0d), (12d, 10d), (0d, 10d), (0d, 0d) }
                .Select(p => new CoordinatePointDto { X = p.Item1, Y = p.Item2 }).ToArray(),
        },
    }, 120, 4, null, actor, Now);

    private static GenerationJob Job(Guid project, Guid actor, Guid land, string key) => new(Guid.NewGuid(), project, actor, key, land,
        Guid.NewGuid(), GenerationJobStatus.Running, null, null, null, PlanningSchemas.EvidenceModeSynthetic, 1, Now, null, []);

    internal static PlanGeometryDto Geometry(Guid project, Guid plan, Guid land, Guid requirements)
    {
        var walls = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var ring = new[] { (1d, 1d), (4d, 1d), (4d, 4d), (1d, 4d), (1d, 1d) };
        return new PlanGeometryDto
        {
            SchemaVersion = GeometrySchemaInfo.DraftVersion, ProjectId = project, PlanId = plan, LandRevisionId = land,
            RequirementsRevisionId = requirements,
            CoordinateSystem = new CoordinateSystemDto
            {
                Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 }, XDirection = "right", YDirection = "up",
            },
            Floors =
            [
                new FloorDto
                {
                    Id = Guid.NewGuid(), Level = 0, Openings = [],
                    Rooms = [new RoomDto
                    {
                        Id = Guid.NewGuid(), Name = "Bedroom 1", Type = "bedroom", WallIds = walls,
                        Polygon = ring.Select(p => new CoordinatePointDto { X = p.Item1, Y = p.Item2 }).ToArray(),
                    }],
                    Walls = walls.Select((id, i) => new WallDto
                    {
                        Id = id, Thickness = 0.1,
                        Start = new CoordinatePointDto { X = ring[i].Item1, Y = ring[i].Item2 },
                        End = new CoordinatePointDto { X = ring[i + 1].Item1, Y = ring[i + 1].Item2 },
                    }).ToArray(),
                },
            ],
        };
    }

    private static GenerationCandidate Candidate(Guid project, GenerationJob job, Guid land, int ordinal)
    {
        var geometry = Geometry(project, Guid.NewGuid(), land, job.RequirementsRevisionId);
        var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
        return new GenerationCandidate(geometry.PlanId, job.Id, project, ordinal, geometry, json, PlanRevisionService.ComputeGeometryHash(json),
            new AlternativeMetricsDto { FloorAreaM2 = 9, FootprintAreaM2 = 9, RoomCount = 1 },
            new ValidationResultDto
            {
                ProjectId = project, LandRevisionId = land, RequirementsRevisionId = job.RequirementsRevisionId, ValidationId = Guid.NewGuid(),
                CandidateId = geometry.PlanId, GeometryHash = new string('a', 64), GeometryValid = true, RequirementsMatch = true,
                RegulatoryEvidenceStatus = "REVIEWED", HasBlockingIssues = false, ValidatorVersion = "test", RuleSetVersion = "synthetic",
                ValidatedAtUtc = Now, Diagnostics = [],
            }, $"sig-{ordinal}", Now);
    }
}
