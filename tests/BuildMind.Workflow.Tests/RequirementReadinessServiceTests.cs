using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Integration;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Workflow.Tests;

public sealed class RequirementReadinessServiceTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Land = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
    private static RequirementCatalogue Policy(string version = "test-v1", string schema = "test-schema", bool available = true) =>
        new(version, schema, new(1, 2), [new("ROOM", new(1, 3), true)], [], [],
            available ? new[] { "METHOD" } : null, true);
    private static RequirementRevision Revision(string? method = "METHOD", Guid? project = null, string version = "test-v1") =>
        new(Guid.NewGuid(), project ?? Project, 1, "test-schema", version,
            new RequirementContent(1, [new RoomCount("ROOM", 1)], designMethod: method), Now);
    private static RequirementReadinessContext Valid() => new(Project, Actor, Land, Revision(), Policy(), true);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Provider(RequirementReadinessContext? context) : IRequirementReadinessContextProvider
    {
        public RequirementReadinessContext? Context { get; set; } = context;
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public Task<RequirementReadinessContext?> GetAsync(Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(Project, projectId);
            Assert.Equal(Actor, authenticatedUserId);
            OnRead?.Invoke();
            return Task.FromResult(Context);
        }
    }
    private static Task<RequirementReadinessResult> Evaluate(RequirementReadinessContext? context) =>
        new RequirementReadinessService(new Provider(context), new Clock()).GetAsync(Project, Actor, default);

    [Fact] public async Task ReadyPreservesExactInputs()
    {
        var context = Valid(); var result = await Evaluate(context);
        Assert.Equal(RequirementReadinessStatus.Ready, result.Status);
        Assert.Same(context.CurrentRequirements, result.Snapshot!.Requirements);
        Assert.Equal(Land, result.Snapshot.LandRevisionId);
        Assert.Equal(Now, result.Snapshot.CapturedAtUtc);
        Assert.Empty(result.DiagnosticCodes);
    }
    [Theory]
    [InlineData("absent", "READINESS_CONTEXT_UNAVAILABLE")]
    [InlineData("wrong-actor", "READINESS_CONTEXT_UNAVAILABLE")]
    [InlineData("wrong-project", "READINESS_CONTEXT_UNAVAILABLE")]
    [InlineData("foreign-revision", "INVALID_READINESS_CONTEXT")]
    [InlineData("empty-land", "INVALID_READINESS_CONTEXT")]
    [InlineData("no-policy", "READINESS_POLICY_UNAVAILABLE")]
    [InlineData("unknown-state", "READINESS_POLICY_UNAVAILABLE")]
    [InlineData("no-method-policy", "DESIGN_METHOD_POLICY_UNAVAILABLE")]
    public async Task UntrustedOrUnknownContextFailsClosed(string scenario, string code)
    {
        var c = Valid();
        RequirementReadinessContext? context = scenario switch
        {
            "absent" => null,
            "wrong-actor" => c with { AuthenticatedUserId = Guid.NewGuid() },
            "wrong-project" => c with { ProjectId = Guid.NewGuid() },
            "foreign-revision" => c with { CurrentRequirements = Revision(project: Guid.NewGuid()) },
            "empty-land" => c with { ConfirmedLandRevisionId = Guid.Empty },
            "no-policy" => c with { Catalogue = null },
            "unknown-state" => c with { IsGenerationEligible = null },
            "no-method-policy" => c with { Catalogue = Policy(available: false) },
            _ => throw new InvalidOperationException()
        };
        var result = await Evaluate(context);
        Assert.Equal(RequirementReadinessStatus.Unavailable, result.Status);
        Assert.Null(result.Snapshot); Assert.Equal(code, Assert.Single(result.DiagnosticCodes));
    }
    [Theory]
    [InlineData("land", "CONFIRMED_LAND_REQUIRED")]
    [InlineData("requirements", "REQUIREMENTS_REQUIRED")]
    [InlineData("state", "PROJECT_NOT_ELIGIBLE")]
    [InlineData("method", "REQUIRED:designMethod")]
    [InlineData("invalid-method", "UNKNOWN_OPTION:designMethod")]
    [InlineData("version", "CATALOGUE_VERSION_MISMATCH:catalogueVersion")]
    [InlineData("schema", "REQUIREMENTS_SCHEMA_MISMATCH")]
    public async Task IncompleteContextHasNoSnapshot(string scenario, string code)
    {
        var c = Valid();
        c = scenario switch
        {
            "land" => c with { ConfirmedLandRevisionId = null },
            "requirements" => c with { CurrentRequirements = null },
            "state" => c with { IsGenerationEligible = false },
            "method" => c with { CurrentRequirements = Revision(null) },
            "invalid-method" => c with { CurrentRequirements = Revision("UNKNOWN") },
            "version" => c with { Catalogue = Policy("test-v2") },
            "schema" => c with { Catalogue = Policy(schema: "test-schema-v2") },
            _ => throw new InvalidOperationException()
        };
        var result = await Evaluate(c);
        Assert.Equal(RequirementReadinessStatus.Incomplete, result.Status);
        Assert.Null(result.Snapshot); Assert.Contains(code, result.DiagnosticCodes);
    }
    [Fact] public async Task ReportsMultipleMissingInputs()
    {
        var result = await Evaluate(Valid() with { ConfirmedLandRevisionId = null, CurrentRequirements = null, IsGenerationEligible = false });
        Assert.Equal(3, result.DiagnosticCodes.Count); Assert.Null(result.Snapshot);
    }
    [Fact] public async Task RequeryAfterEditUsesNewRevisionWithoutMutatingOldSnapshot()
    {
        var provider = new Provider(Valid()); var service = new RequirementReadinessService(provider, new Clock());
        var first = await service.GetAsync(Project, Actor, default);
        var next = Revision(); provider.Context = provider.Context! with { CurrentRequirements = next };
        var second = await service.GetAsync(Project, Actor, default);
        Assert.NotEqual(first.Snapshot!.RequirementsRevisionId, second.Snapshot!.RequirementsRevisionId);
        Assert.Equal(next.Id, second.Snapshot.RequirementsRevisionId); Assert.Equal(2, provider.Calls);
    }
    [Fact] public async Task EmptyActorDoesNotReadProvider()
    {
        var p = new Provider(Valid()); var result = await new RequirementReadinessService(p, new Clock()).GetAsync(Project, Guid.Empty, default);
        Assert.Equal(RequirementReadinessStatus.Unavailable, result.Status); Assert.Equal(0, p.Calls);
    }
    [Fact] public async Task CancellationBeforeReadDoesNotCallProvider()
    {
        var p = new Provider(Valid()); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RequirementReadinessService(p, new Clock()).GetAsync(Project, Actor, cts.Token));
        Assert.Equal(0, p.Calls);
    }
    [Fact] public async Task CancellationDuringReadCannotReturnReady()
    {
        using var cts = new CancellationTokenSource(); var p = new Provider(Valid()) { OnRead = cts.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RequirementReadinessService(p, new Clock()).GetAsync(Project, Actor, cts.Token));
    }
    [Fact] public async Task CancellationDuringCaptureClockCannotReturnReady()
    {
        using var cts = new CancellationTokenSource();
        var provider = new Provider(Valid());
        var clock = new CancelingClock(cts.Cancel);
        var service = new RequirementReadinessService(provider, clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAsync(Project, Actor, cts.Token));

        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, clock.Calls);
    }

    private sealed class CancelingClock(Action cancel) : TimeProvider
    {
        public int Calls { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Calls++;
            cancel();
            return Now;
        }
    }

    [Fact] public async Task UnknownFailurePropagatesWithoutRetry()
    {
        var p = new Provider(Valid()) { OnRead = () => throw new InvalidOperationException("synthetic") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RequirementReadinessService(p, new Clock()).GetAsync(Project, Actor, default));
        Assert.Equal(1, p.Calls);
    }
}
