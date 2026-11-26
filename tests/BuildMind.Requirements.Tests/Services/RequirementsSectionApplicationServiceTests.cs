using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;
using Setup = BuildMind.Requirements.Tests.Services.RequirementsApplicationServiceTests.Setup;

namespace BuildMind.Requirements.Tests.Services;

public sealed class RequirementsSectionApplicationServiceTests
{
    [Theory]
    [InlineData("METHOD_A", RequirementsOperationStatus.Success)]
    [InlineData("UNKNOWN", RequirementsOperationStatus.ValidationFailed)]
    public async Task MethodReplacement_PreservesSnapshotAndUsesPolicy(string method, RequirementsOperationStatus expected)
    {
        var setup = new Setup(); var initial = await Seed(setup);
        var service = new RequirementsSectionApplicationService(setup.Service);
        var result = await service.SaveDesignMethodAsync(setup.Project.Id, new(initial.RevisionId, F.Version, method), default);
        Assert.Equal(expected, result.Status);
        Assert.Equal("METHOD_A", setup.Store.History[initial.RevisionId].Content.DesignMethod);
        if (result.Revision is { } saved)
        {
            Assert.NotEqual(initial.RevisionId, saved.RevisionId);
            Assert.Equal(initial.FloorCount, saved.FloorCount);
            Assert.Equal(initial.GroundFloorRooms, saved.GroundFloorRooms);
            Assert.Equal(initial.AdditionalSpaces, saved.AdditionalSpaces);
            Assert.Equal(initial.Relationships, saved.Relationships);
            Assert.Equal(method, saved.DesignMethod);
            var stale = await service.SaveDesignMethodAsync(setup.Project.Id, new(initial.RevisionId, F.Version, method), default);
            Assert.Equal(RequirementsOperationStatus.RevisionConflict, stale.Status);
        }
        else Assert.Equal(initial.RevisionId, setup.Store.Current!.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_PreservesCoreOtherSectionAndHistory(bool spaces)
    {
        var setup = new Setup(); var initial = await Seed(setup);
        var service = new RequirementsSectionApplicationService(setup.Service);
        var result = await Replace(service, setup.Project.Id, initial.RevisionId, spaces);
        Assert.Equal(RequirementsOperationStatus.Success, result.Status);
        var current = result.Revision!;
        Assert.Equal(2, current.FloorCount);
        Assert.Equal(initial.GroundFloorRooms, current.GroundFloorRooms);
        Assert.Equal("METHOD_A", current.DesignMethod);
        Assert.Equal(2L, current.RevisionNumber);
        Assert.Equal(F.Version, current.CatalogueVersion);
        Assert.Equal(F.Schema, current.SchemaVersion);
        if (spaces) { Assert.Empty(current.AdditionalSpaces); Assert.Equal(initial.Relationships, current.Relationships); }
        else { Assert.Empty(current.Relationships); Assert.Equal(initial.AdditionalSpaces, current.AdditionalSpaces); }
        Assert.Single(setup.Store.History[initial.RevisionId].Content.AdditionalSpaces);
        Assert.Single(setup.Store.History[initial.RevisionId].Content.Relationships);
        Assert.Equal(2, setup.Store.History.Count);
        Assert.Equal(current.RevisionId, (await service.GetAsync(setup.Project.Id, default)).Revision!.RevisionId);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("too-long")]
    [InlineData("text-forbidden")]
    public async Task InvalidSpaces_UseExistingPolicyAndDoNotWrite(string kind)
    {
        var setup = new Setup(); var initial = await Seed(setup);
        AdditionalSpaceInput?[] selections = kind switch
        {
            "unknown" => [new("SYNTHETIC_UNKNOWN", null)],
            "duplicate" => [new("SPACE_A", null), new("SPACE_A", null)],
            "too-long" => [new("SPACE_TEXT", "123456")],
            _ => [new("SPACE_A", "text")]
        };
        var result = await new RequirementsSectionApplicationService(setup.Service).SaveSpacesAsync(setup.Project.Id,
            new(initial.RevisionId, F.Version, selections), default);
        Assert.Equal(RequirementsOperationStatus.ValidationFailed, result.Status);
        Assert.Equal(1, setup.Store.Writes);
        Assert.Equal(initial.RevisionId, setup.Store.Current!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    public async Task AllowedOptionalText_RoundTripsAtSyntheticBoundary(string? text)
    {
        var setup = new Setup(); var initial = await Seed(setup);
        var result = await new RequirementsSectionApplicationService(setup.Service).SaveSpacesAsync(setup.Project.Id,
            new(initial.RevisionId, F.Version, [new("SPACE_TEXT", text)]), default);
        Assert.Equal(RequirementsOperationStatus.Success, result.Status);
        Assert.Equal(text, result.Revision!.AdditionalSpaces.Single().OtherText);
    }

    [Theory]
    [InlineData("unknown-room")]
    [InlineData("unknown-kind")]
    [InlineData("undirected-duplicate")]
    [InlineData("self-forbidden")]
    public async Task InvalidRelationships_UseExistingPolicyAndDoNotWrite(string kind)
    {
        var setup = new Setup(); var initial = await Seed(setup);
        RoomRelationshipInput?[] selections = kind switch
        {
            "unknown-room" => [new("UNKNOWN", "ROOM_B", "NEAR")],
            "unknown-kind" => [new("ROOM_A", "ROOM_B", "UNKNOWN")],
            "undirected-duplicate" => [new("ROOM_A", "ROOM_B", "NEAR"), new("ROOM_B", "ROOM_A", "NEAR")],
            _ => [new("ROOM_A", "ROOM_A", "NEAR")]
        };
        var result = await new RequirementsSectionApplicationService(setup.Service).SaveRelationshipsAsync(setup.Project.Id,
            new(initial.RevisionId, F.Version, selections), default);
        Assert.Equal(RequirementsOperationStatus.ValidationFailed, result.Status);
        Assert.Equal(1, setup.Store.Writes);
    }

    [Fact]
    public async Task ExistingDirectedSelfAndZeroCountSemantics_AreNotTightened()
    {
        var setup = new Setup(); var initial = await Seed(setup, zeroRoom: true);
        var result = await new RequirementsSectionApplicationService(setup.Service).SaveRelationshipsAsync(setup.Project.Id,
            new(initial.RevisionId, F.Version, [new("ROOM_A", "ROOM_B", "FLOWS_TO"), new("ROOM_B", "ROOM_A", "FLOWS_TO"),
                new("ROOM_A", "ROOM_A", "SELF_OK")]), default);
        Assert.Equal(RequirementsOperationStatus.Success, result.Status);
        Assert.Equal(3, result.Revision!.Relationships.Count);
        Assert.Equal(0, result.Revision.GroundFloorRooms.Single(room => room.RoomType == "ROOM_B").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingBase_ProvisionallyConflictsWithoutInventingCore(bool spaces)
    {
        var setup = new Setup(); var service = new RequirementsSectionApplicationService(setup.Service);
        Assert.Equal(RequirementsOperationStatus.NoSavedRevision, (await service.GetAsync(setup.Project.Id, default)).Status);
        Assert.Equal(RequirementsOperationStatus.RevisionConflict, (await Replace(service, setup.Project.Id, F.RevisionId, spaces)).Status);
        Assert.Equal(0, setup.Store.Writes);
        Assert.Equal(0, setup.Catalogues.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleBase_CannotOverwriteOrRetry(bool spaces)
    {
        var setup = new Setup(); var initial = await Seed(setup);
        var service = new RequirementsSectionApplicationService(setup.Service);
        Assert.Equal(RequirementsOperationStatus.Success, (await Replace(service, setup.Project.Id, initial.RevisionId, spaces)).Status);
        Assert.Equal(RequirementsOperationStatus.RevisionConflict, (await Replace(service, setup.Project.Id, initial.RevisionId, !spaces)).Status);
        Assert.Equal(2, setup.Store.History.Count);
        Assert.Equal(2, setup.Store.Writes);
    }

    [Fact]
    public async Task CompetingDifferentSections_KeepOriginalExpectedRevisionAndOneWinner()
    {
        var setup = new Setup(); var initial = await Seed(setup);
        var coordinated = new CoordinatedService(setup.Service);
        var service = new RequirementsSectionApplicationService(coordinated);
        var results = await Task.WhenAll(Replace(service, setup.Project.Id, initial.RevisionId, true),
            Replace(service, setup.Project.Id, initial.RevisionId, false)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(results, item => item.Status == RequirementsOperationStatus.Success);
        Assert.Single(results, item => item.Status == RequirementsOperationStatus.RevisionConflict);
        Assert.Equal(2, coordinated.Reads);
        Assert.Equal(2, coordinated.Saves);
        Assert.Equal(2, setup.Store.History.Count);
        Assert.Equal(2L, setup.Store.Current!.RevisionNumber);
        // Existing atomic test double, not MySQL concurrency evidence.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationAndOwnerRejection_PreventProtectedAccess(bool unauthenticated)
    {
        var setup = new Setup();
        if (unauthenticated) setup.User.Authenticated = false; else setup.User.Id = Guid.NewGuid();
        var result = await Replace(new(setup.Service), setup.Project.Id, F.RevisionId, true);
        Assert.Equal(unauthenticated ? RequirementsOperationStatus.Unauthenticated : RequirementsOperationStatus.NotFound, result.Status);
        Assert.Equal(0, setup.Catalogues.Calls);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Fact]
    public async Task PolicyChange_RevalidatesPreservedCoreWithoutPruningIt()
    {
        var setup = new Setup(); var initial = await Seed(setup);
        setup.Catalogues.Policy = new(F.Version, F.Schema, new(1, 3),
            [new("ROOM_A", new(1, 1), true), new("ROOM_B", new(0, 2), false)], F.Spaces(), F.Relationships(), ["METHOD_A"], false);
        var result = await Replace(new(setup.Service), setup.Project.Id, initial.RevisionId, true);
        Assert.Equal(RequirementsOperationStatus.ValidationFailed, result.Status);
        Assert.Equal(2, setup.Store.Current!.Content.GroundFloorRooms[0].Count);
        Assert.Single(setup.Store.Current.Content.AdditionalSpaces);
        Assert.Equal(1, setup.Store.Writes);
    }

    [Fact]
    public async Task NewAuthoritativeProvenance_IsSavedWithoutRewritingHistory()
    {
        var setup = new Setup(); var initial = await Seed(setup);
        setup.Catalogues.Policy = F.Catalogue(version: "synthetic-v2", schema: "synthetic-schema-v2");
        var result = await new RequirementsSectionApplicationService(setup.Service).SaveSpacesAsync(setup.Project.Id,
            new(initial.RevisionId, "synthetic-v2", []), default);
        Assert.Equal(RequirementsOperationStatus.Success, result.Status);
        Assert.Equal("synthetic-v2", result.Revision!.CatalogueVersion);
        Assert.Equal("synthetic-schema-v2", result.Revision.SchemaVersion);
        Assert.Equal(F.Version, setup.Store.History[initial.RevisionId].CatalogueVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_DoesNotBecomeEmptySuccess(bool duringRead)
    {
        var fake = new StubService(); using var source = new CancellationTokenSource();
        if (duringRead) fake.AfterRead = source.Cancel; else source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Replace(new(fake), F.ProjectId, F.RevisionId, true, source.Token));
        Assert.Equal(0, fake.Saves);
    }

    [Fact]
    public async Task ConfirmedSave_IsNotReclassifiedAfterCancellation()
    {
        var setup = new Setup(); var initial = await Seed(setup); using var source = new CancellationTokenSource();
        setup.Store.AfterCommit = source.Cancel;
        Assert.Equal(RequirementsOperationStatus.Success, (await Replace(new(setup.Service), setup.Project.Id, initial.RevisionId, true, source.Token)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpaqueFailures_PropagateWithoutRetry(bool save)
    {
        var fake = new StubService(); var failure = new SyntheticUnknownOutcome();
        if (save) fake.SaveFailure = failure; else fake.ReadFailure = failure;
        var observed = await Assert.ThrowsAsync<SyntheticUnknownOutcome>(() => Replace(new(fake), F.ProjectId, F.RevisionId, true));
        Assert.Same(failure, observed);
        Assert.Equal(save ? 1 : 0, fake.Saves);
        Assert.Equal(1, fake.Reads);
    }

    [Theory]
    [InlineData(RequirementsOperationStatus.Unauthenticated)]
    [InlineData(RequirementsOperationStatus.NotFound)]
    [InlineData(RequirementsOperationStatus.DependencyUnavailable)]
    [InlineData(RequirementsOperationStatus.DependencyTimeout)]
    [InlineData(RequirementsOperationStatus.InvalidDependencyResponse)]
    public async Task ReadFailureOutcomes_ArePreserved(RequirementsOperationStatus status)
    {
        var failure = new RequirementsOperationResult(status); var fake = new StubService { ReadResult = failure };
        Assert.Same(failure, await Replace(new(fake), F.ProjectId, F.RevisionId, false));
        Assert.Equal(0, fake.Saves);
    }

    [Fact]
    public async Task FaultyReadOrSaveResponses_AreNotExposed()
    {
        var other = new RequirementRevisionDto(new RequirementRevision(F.RevisionId, Guid.NewGuid(), 1,
            F.Schema, F.Version, F.Content(), F.Timestamp));
        var fake = new StubService { ReadResult = new(RequirementsOperationStatus.Success, other) };
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, (await new RequirementsSectionApplicationService(fake).GetAsync(F.ProjectId, default)).Status);
        fake.ReadResult = new(RequirementsOperationStatus.Success, new RequirementRevisionDto(F.Revision()));
        fake.SaveResult = new(RequirementsOperationStatus.Success, other);
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, (await Replace(new(fake), F.ProjectId, F.RevisionId, true)).Status);
        fake.SaveResult = new(RequirementsOperationStatus.NoSavedRevision);
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, (await Replace(new(fake), F.ProjectId, F.RevisionId, true)).Status);
    }

    [Theory]
    [InlineData(false, 2, RequirementsOperationStatus.Success)]
    [InlineData(true, 2, RequirementsOperationStatus.InvalidDependencyResponse)]
    [InlineData(false, 1, RequirementsOperationStatus.InvalidDependencyResponse)]
    [InlineData(false, 3, RequirementsOperationStatus.InvalidDependencyResponse)]
    public async Task SuccessfulReplacement_RequiresNewConsecutiveRevision(
        bool reuseBaseId, long savedNumber, RequirementsOperationStatus expected)
    {
        var revisionId = reuseBaseId ? F.RevisionId : Guid.NewGuid();
        var original = F.Content();
        var matchingContent = new RequirementContent(original.FloorCount, original.GroundFloorRooms,
            [], original.Relationships, original.DesignMethod);
        var revision = new RequirementRevision(revisionId, F.ProjectId, savedNumber,
            F.Schema, F.Version, matchingContent, F.Timestamp);
        var receipt = new RequirementsOperationResult(RequirementsOperationStatus.Success, new RequirementRevisionDto(revision));
        var fake = new StubService { SaveResult = receipt };

        var result = await Replace(new(fake), F.ProjectId, F.RevisionId, true);

        Assert.Equal(expected, result.Status);
        Assert.Equal(1, fake.Saves);
        if (expected == RequirementsOperationStatus.Success)
            Assert.Same(receipt, result);
        else
            Assert.Null(result.Revision);
    }

    [Theory]
    [InlineData("catalogue")]
    [InlineData("floor-count")]
    [InlineData("rooms")]
    [InlineData("spaces")]
    [InlineData("relationships")]
    [InlineData("design-method")]
    public async Task SuccessfulReceiptWithDifferentSubmittedContentFailsClosed(string mismatch)
    {
        var original = F.Content();
        var rooms = original.GroundFloorRooms.AsEnumerable();
        var spaces = Enumerable.Empty<BuildMind.Domain.ValueObjects.AdditionalSpaceSelection>();
        var relationships = original.Relationships.AsEnumerable();
        var method = original.DesignMethod;
        var floors = original.FloorCount;
        var catalogue = F.Version;

        switch (mismatch)
        {
            case "catalogue": catalogue = "wrong-catalogue"; break;
            case "floor-count": floors = 2; break;
            case "rooms": rooms = [new("ROOM_A", 2), new("ROOM_B", 1)]; break;
            case "spaces": spaces = original.AdditionalSpaces; break;
            case "relationships": relationships = []; break;
            case "design-method": method = null; break;
        }

        var content = new RequirementContent(floors, rooms, spaces, relationships, method);
        var revision = new RequirementRevision(Guid.NewGuid(), F.ProjectId, 2,
            F.Schema, catalogue, content, F.Timestamp);
        var fake = new StubService
        {
            SaveResult = new(RequirementsOperationStatus.Success, new RequirementRevisionDto(revision))
        };

        var outcome = await Replace(new(fake), F.ProjectId, F.RevisionId, true);

        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, outcome.Status);
        Assert.Null(outcome.Revision);
        Assert.Equal(1, fake.Saves);
    }

    [Theory]
    [InlineData("spaces")]
    [InlineData("relationships")]
    [InlineData("design-method")]
    public async Task EverySectionRejectsSuccessfulReceiptContainingUnchangedBase(string section)
    {
        var unchanged = new RequirementRevision(Guid.NewGuid(), F.ProjectId, 2,
            F.Schema, F.Version, F.Content(), F.Timestamp);
        var fake = new StubService
        {
            SaveResult = new(RequirementsOperationStatus.Success, new RequirementRevisionDto(unchanged))
        };
        var service = new RequirementsSectionApplicationService(fake);

        var result = section switch
        {
            "spaces" => await service.SaveSpacesAsync(
                F.ProjectId, new(F.RevisionId, F.Version, []), default),
            "relationships" => await service.SaveRelationshipsAsync(
                F.ProjectId, new(F.RevisionId, F.Version, []), default),
            _ => await service.SaveDesignMethodAsync(
                F.ProjectId, new(F.RevisionId, F.Version, null), default)
        };

        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, result.Status);
        Assert.Null(result.Revision);
        Assert.Equal(1, fake.Reads);
        Assert.Equal(1, fake.Saves);
    }

    private static Task<RequirementsOperationResult> Replace(RequirementsSectionApplicationService service, Guid project, Guid revision,
        bool spaces, CancellationToken token = default) => spaces
        ? service.SaveSpacesAsync(project, new(revision, F.Version, []), token)
        : service.SaveRelationshipsAsync(project, new(revision, F.Version, []), token);

    private static async Task<RequirementRevisionDto> Seed(Setup setup, bool zeroRoom = false)
    {
        var result = await setup.Service.SaveAsync(setup.Project.Id, new(null, F.Version, 2,
            [new("ROOM_A", 2), new("ROOM_B", zeroRoom ? 0 : 1)], [new("SPACE_TEXT", "text")],
            [new("ROOM_A", "ROOM_B", "NEAR")], "METHOD_A"), default);
        Assert.Equal(RequirementsOperationStatus.Success, result.Status);
        return result.Revision!;
    }
    private sealed class SyntheticUnknownOutcome : Exception { }
    private sealed class StubService : IRequirementsApplicationService
    {
        public RequirementsOperationResult ReadResult { get; set; } = new(RequirementsOperationStatus.Success, new RequirementRevisionDto(F.Revision()));
        public RequirementsOperationResult SaveResult { get; set; } = new(RequirementsOperationStatus.Success, new RequirementRevisionDto(F.Revision()));
        public Exception? ReadFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public Action? AfterRead { get; set; }
        public int Reads { get; private set; }
        public int Saves { get; private set; }
        public Task<RequirementsOperationResult> GetAsync(Guid projectId, CancellationToken token)
        {
            Reads++; AfterRead?.Invoke();
            return ReadFailure is null ? Task.FromResult(ReadResult) : Task.FromException<RequirementsOperationResult>(ReadFailure);
        }
        public Task<RequirementsOperationResult> SaveAsync(Guid projectId, SaveRequirementsRequest? request, CancellationToken token)
        {
            Saves++;
            return SaveFailure is null ? Task.FromResult(SaveResult) : Task.FromException<RequirementsOperationResult>(SaveFailure);
        }
    }
    private sealed class CoordinatedService(IRequirementsApplicationService source) : IRequirementsApplicationService
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        private int _saves;
        public int Reads => _reads;
        public int Saves => _saves;
        public async Task<RequirementsOperationResult> GetAsync(Guid projectId, CancellationToken token)
        {
            var result = await source.GetAsync(projectId, token);
            if (Interlocked.Increment(ref _reads) == 2) _bothRead.TrySetResult();
            await _bothRead.Task.WaitAsync(token);
            return result;
        }
        public Task<RequirementsOperationResult> SaveAsync(Guid projectId, SaveRequirementsRequest? request, CancellationToken token)
        {
            Interlocked.Increment(ref _saves);
            return source.SaveAsync(projectId, request, token);
        }
    }
}
