using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Services;

// Existing-port flow checks only: no HTTP decisions, real authentication or MySQL evidence.
public sealed class RequirementRevisionReadSaveFlowTests
{
    [Fact]
    public async Task InitialAndNextSave_ReadBackExactImmutableHistory()
    {
        var store = new SnapshotStore();
        var writer = new ValidatedRequirementRevisionWriter(store, TimeProvider.System);
        var reader = new RequirementRevisionReader(store);
        Assert.Null(await reader.GetCurrentAsync(F.ProjectId, default));
        var first = (await writer.SaveAsync(F.ProjectId, null, Validate(), default)).SavedRevision!;
        var nextValidation = F.Validate(F.Request(floors: 2));
        var second = (await writer.SaveAsync(F.ProjectId, first.Id, nextValidation, default)).SavedRevision!;

        Assert.Same(second, await reader.GetCurrentAsync(F.ProjectId, default));
        Assert.Same(first, await reader.GetAsync(F.ProjectId, first.Id, default));
        Assert.Same(second, await reader.GetAsync(F.ProjectId, second.Id, default));
        Assert.Equal(1, first.Content.FloorCount);
        Assert.Equal(2, second.Content.FloorCount);
        Assert.Equal(1L, first.RevisionNumber);
        Assert.Equal(2L, second.RevisionNumber);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.HistoryCount);
    }

    [Fact]
    public async Task StaleSave_PreservesCurrentAndHistory()
    {
        var store = new SnapshotStore();
        var writer = new ValidatedRequirementRevisionWriter(store, TimeProvider.System);
        var reader = new RequirementRevisionReader(store);
        var first = (await writer.SaveAsync(F.ProjectId, null, Validate(), default)).SavedRevision!;
        var second = (await writer.SaveAsync(F.ProjectId, first.Id, Validate(), default)).SavedRevision!;
        var result = await writer.SaveAsync(F.ProjectId, first.Id, Validate(), default);
        Assert.Equal(RequirementSaveStatus.RevisionConflict, result.Status);
        Assert.Null(result.SavedRevision);
        Assert.Same(second, await reader.GetCurrentAsync(F.ProjectId, default));
        Assert.Same(first, await reader.GetAsync(F.ProjectId, first.Id, default));
        Assert.Equal(2, store.HistoryCount);
        Assert.Equal(2, store.SaveCalls);
    }

    [Fact]
    public async Task ExplicitValidatedEmptySelections_DoNotRewriteEarlierSelections()
    {
        var store = new SnapshotStore();
        var writer = new ValidatedRequirementRevisionWriter(store, TimeProvider.System);
        var reader = new RequirementRevisionReader(store);
        var selected = F.Validate(new SaveRequirementsRequest(null, F.Version, 1,
            F.Request().GroundFloorRooms, [new("SPACE_TEXT", "text")],
            [new("ROOM_A", "ROOM_B", "NEAR")], "METHOD_A"));
        var first = (await writer.SaveAsync(F.ProjectId, null, selected, default)).SavedRevision!;
        var cleared = F.Validate(new SaveRequirementsRequest(first.Id, F.Version, 1,
            F.Request().GroundFloorRooms, [], [], null));
        var second = (await writer.SaveAsync(F.ProjectId, first.Id, cleared, default)).SavedRevision!;
        Assert.Empty(second.Content.AdditionalSpaces);
        Assert.Empty(second.Content.Relationships);
        Assert.Null(second.Content.DesignMethod);
        var historical = (await reader.GetAsync(F.ProjectId, first.Id, default))!;
        Assert.Single(historical.Content.AdditionalSpaces);
        Assert.Single(historical.Content.Relationships);
        Assert.Equal("METHOD_A", historical.Content.DesignMethod);
        // Explicit validated content is exercised here; omitted HTTP field semantics remain pending.
    }

    [Fact]
    public async Task FailedValidation_LeavesSavedSnapshotIntact()
    {
        var store = new SnapshotStore();
        var writer = new ValidatedRequirementRevisionWriter(store, TimeProvider.System);
        var first = (await writer.SaveAsync(F.ProjectId, null, Validate(), default)).SavedRevision!;
        var invalid = F.Validate(F.Request(version: "synthetic-unapproved-version", expected: first.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.SaveAsync(F.ProjectId, first.Id, invalid, default));
        Assert.Same(first, await new RequirementRevisionReader(store).GetCurrentAsync(F.ProjectId, default));
        Assert.Equal(1, store.HistoryCount);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task UnknownResponseAfterSyntheticCommit_RequiresRefreshAndDoesNotRetry()
    {
        var failure = new SyntheticOutcomeUnknownException();
        var store = new SnapshotStore { AfterSaveFailure = failure };
        var writer = new ValidatedRequirementRevisionWriter(store, TimeProvider.System);
        var observed = await Assert.ThrowsAsync<SyntheticOutcomeUnknownException>(() =>
            writer.SaveAsync(F.ProjectId, null, Validate(), default));
        Assert.Same(failure, observed);
        Assert.Equal(1, store.SaveCalls);
        var reader = new RequirementRevisionReader(store);
        var recovered = await reader.GetCurrentAsync(F.ProjectId, default);
        Assert.NotNull(recovered);
        Assert.Same(recovered, await reader.GetAsync(F.ProjectId, recovered.Id, default));
        Assert.Equal(1, store.HistoryCount);
        // A synthetic lost response can follow a commit. This does not test M4's internal exceptions.
    }

    private static RequirementValidationResult Validate() => F.Validate(F.Request());
    private sealed class SyntheticOutcomeUnknownException : Exception { }

    private sealed class SnapshotStore : IRequirementRevisionRepository
    {
        private readonly Dictionary<(Guid Project, Guid Revision), RequirementRevision> _history = [];
        private readonly Dictionary<Guid, RequirementRevision> _heads = [];
        public int HistoryCount => _history.Count;
        public int SaveCalls { get; private set; }
        public Exception? AfterSaveFailure { get; init; }

        public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_heads.GetValueOrDefault(projectId));
        }

        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_history.GetValueOrDefault((projectId, revisionId)));
        }

        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            var current = _heads.GetValueOrDefault(proposedRevision.ProjectId);
            if (current?.Id != expectedRevisionId)
                return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.RevisionConflict));
            _history.Add((proposedRevision.ProjectId, proposedRevision.Id), proposedRevision);
            _heads[proposedRevision.ProjectId] = proposedRevision;
            return AfterSaveFailure is null
                ? Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.Saved, proposedRevision))
                : Task.FromException<RequirementSaveResult>(AfterSaveFailure);
        }
    }
}
