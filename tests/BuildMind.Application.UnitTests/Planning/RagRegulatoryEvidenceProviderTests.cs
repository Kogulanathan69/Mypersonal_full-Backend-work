using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.Planning;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.UnitTests.Planning;

/// <summary>Phase 5: fail-closed consumption of the existing M7 constraint contract by generation.</summary>
public sealed class RagRegulatoryEvidenceProviderTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RequirementsId = Guid.NewGuid();

    [Fact]
    public async Task Synthetic_packs_never_count_as_reviewed()
    {
        var (selection, source) = await RunAsync(Pack(synthetic: true, ConstraintKindStatus.Verified));
        Assert.Equal(RegulatoryEvidenceStatuses.Unverified, selection.Evidence.Status);
        Assert.Contains(selection.Evidence.Gaps, g => g.Code == "FRONT_SETBACK_MIN_SYNTHETIC");
        Assert.Empty(selection.Evidence.Constraints);
        Assert.Equal("pack-1", selection.EvidencePackId);
        Assert.Equal(RagRegulatoryEvidenceProvider.MandatoryKinds, source.LastQuery!.Kinds);
        Assert.Equal(new ConstraintBinding(ProjectId, 4, source.LastQuery.Binding.LandRevisionId, RequirementsId), source.LastQuery.Binding);
    }

    [Fact]
    public async Task Verified_kinds_stay_unverified_until_the_parcel_edge_mapping_is_approved()
    {
        var (selection, _) = await RunAsync(Pack(synthetic: false, ConstraintKindStatus.Verified));
        Assert.Equal(RegulatoryEvidenceStatuses.Unverified, selection.Evidence.Status);
        Assert.Equal(["PARCEL_EDGE_ROLE_MAPPING_UNAPPROVED"], selection.Evidence.Gaps.Select(g => g.Code));
        Assert.Empty(selection.Evidence.Constraints); // no setback value is ever placed on an edge
    }

    [Fact]
    public async Task Missing_kinds_yield_missing_evidence_with_explicit_gaps()
    {
        var (selection, _) = await RunAsync(Pack(synthetic: false, ConstraintKindStatus.Missing));
        Assert.Equal(RegulatoryEvidenceStatuses.Missing, selection.Evidence.Status);
        Assert.Null(selection.Evidence.RuleSetVersion);
        Assert.Contains(selection.Evidence.Gaps, g => g.Code == "REAR_SPACE_MIN_MISSING");
    }

    [Fact]
    public async Task Missing_jurisdiction_never_queries_the_service()
    {
        var source = new FakeSource(Pack(synthetic: false, ConstraintKindStatus.Verified));
        var provider = new RagRegulatoryEvidenceProvider(source, new FakeProjects(), new PolicyRegulatoryEvidenceProvider());
        var selection = await provider.GetAsync(ProjectId, Land(), RequirementsId, Policy() with { Jurisdiction = null }, default);
        Assert.Equal(RegulatoryEvidenceStatuses.Missing, selection.Evidence.Status);
        Assert.Equal("JURISDICTION_NOT_CONFIGURED", selection.Evidence.Gaps.Single().Code);
        Assert.Null(source.LastQuery);
    }

    [Fact]
    public async Task Rag_unavailability_propagates_so_generation_fails_closed()
    {
        var source = new FakeSource(null) { Failure = new DependencyFailureException("rag", DependencyFailureKind.Unavailable) };
        var provider = new RagRegulatoryEvidenceProvider(source, new FakeProjects(), new PolicyRegulatoryEvidenceProvider());
        await Assert.ThrowsAsync<DependencyFailureException>(() => provider.GetAsync(ProjectId, Land(), RequirementsId, Policy(), default));
    }

    [Fact]
    public async Task Other_modes_delegate_to_the_policy_provider()
    {
        var source = new FakeSource(null);
        var provider = new RagRegulatoryEvidenceProvider(source, new FakeProjects(), new PolicyRegulatoryEvidenceProvider());
        var policy = Policy() with { EvidenceMode = PlanningSchemas.EvidenceModeNone };
        Assert.Equal(PlanningSchemas.EvidenceModeNone, provider.Mode(policy));
        Assert.Equal(RegulatoryEvidenceStatuses.Missing, (await provider.GetAsync(ProjectId, Land(), RequirementsId, policy, default)).Evidence.Status);
        Assert.Null(source.LastQuery);
    }

    private static async Task<(RegulatoryEvidenceSelection, FakeSource)> RunAsync(RegulatoryConstraintPack pack)
    {
        var source = new FakeSource(pack);
        var provider = new RagRegulatoryEvidenceProvider(source, new FakeProjects(), new PolicyRegulatoryEvidenceProvider());
        Assert.Equal(PlanningSchemas.EvidenceModeRag, provider.Mode(Policy()));
        return (await provider.GetAsync(ProjectId, Land(), RequirementsId, Policy(), default), source);
    }

    private static GenerationPolicy Policy() => new("policy", "DEVELOPMENT ONLY",
        new Dictionary<string, RoomDimensionsDto> { ["bedroom"] = new() { MinWidthM = 3, MinDepthM = 3 } },
        PlanningSchemas.EvidenceModeRag, null, null, "Synthetic council A");

    private static RegulatoryConstraintPack Pack(bool synthetic, ConstraintKindStatus status) => new("pack-1", "table-v0", new string('a', 64),
        "reviewers-v0", new string('b', 64), synthetic, "Synthetic council A", new ConstraintBinding(ProjectId, 4, Guid.NewGuid(), RequirementsId),
        RagRegulatoryEvidenceProvider.MandatoryKinds.Select(k => new ConstraintKindResult(k, status, [])).ToArray(),
        status == ConstraintKindStatus.Verified, []);

    private static LandRevision Land() => new(Guid.NewGuid(), ProjectId, 1, new MetricLandDto
    {
        SchemaVersion = PlanningSchemas.MetricLand,
        CoordinateSystem = new CoordinateSystemDto { Type = "localCartesian", Unit = "m", Origin = new() { X = 0, Y = 0 }, XDirection = "right", YDirection = "up" },
        Boundary = new PolygonBoundaryDto { Exterior = [] },
    }, 120, 4, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private sealed class FakeSource(RegulatoryConstraintPack? pack) : IRegulatoryConstraintSource
    {
        public RegulatoryConstraintQuery? LastQuery { get; private set; }
        public Exception? Failure { get; init; }

        public Task<RegulatoryConstraintPack> GetConstraintsAsync(RegulatoryConstraintQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            if (Failure is not null) throw Failure;
            return Task.FromResult(pack!);
        }
    }

    private sealed class FakeProjects : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Project?>(Project.Restore(id, Guid.NewGuid(), "p", null, null, "Created", 4, DateTimeOffset.UtcNow, null));
        public Task<Project?> GetByIdAndOwnerAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Project project, Guid ownerId, long expectedRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
