using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Services;

public sealed class ValidatedRequirementRevisionWriterTests
{
    [Fact]
    public async Task InitialSave_PreservesValidatedSnapshotAndUsesServerMetadata()
    {
        var repository = new RecordingRepository();
        var validation = FullValidation();
        var result = await Writer(repository).SaveAsync(F.ProjectId, null, validation, default);
        Assert.Equal(RequirementSaveStatus.Saved, result.Status);
        var saved = Assert.IsType<RequirementRevision>(result.SavedRevision);
        Assert.Same(repository.Proposed, saved);
        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(F.ProjectId, saved.ProjectId);
        Assert.Equal(1L, saved.RevisionNumber);
        Assert.Equal(F.Schema, saved.SchemaVersion);
        Assert.Equal(F.Version, saved.CatalogueVersion);
        Assert.Same(validation.Content, saved.Content);
        Assert.Single(saved.Content.AdditionalSpaces);
        Assert.Single(saved.Content.Relationships);
        Assert.Equal("METHOD_A", saved.Content.DesignMethod);
        Assert.Equal(F.Timestamp.ToUniversalTime(), saved.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, saved.CreatedAtUtc.Offset);
        Assert.Equal(F.ProjectId, repository.ReadProject);
        Assert.Null(repository.Expected);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task SubsequentSave_IncrementsWithoutMutatingPreviousRevision()
    {
        var previous = F.Revision();
        var repository = new RecordingRepository { Current = previous };
        var result = await Writer(repository).SaveAsync(F.ProjectId, previous.Id, FullValidation(), default);
        Assert.Equal(2L, result.SavedRevision!.RevisionNumber);
        Assert.NotEqual(previous.Id, result.SavedRevision.Id);
        Assert.Equal(previous.Id, repository.Expected);
        Assert.Equal(1L, previous.RevisionNumber);
        Assert.Same(previous, repository.Current);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StaleExpectedRevision_DoesNotAttemptWrite(bool hasCurrent, bool hasExpected)
    {
        var repository = new RecordingRepository { Current = hasCurrent ? F.Revision() : null };
        var result = await Writer(repository).SaveAsync(F.ProjectId,
            hasExpected ? Guid.Parse("30000000-0000-0000-0000-000000000003") : null, FullValidation(), default);
        Assert.Equal(RequirementSaveStatus.RevisionConflict, result.Status);
        Assert.Null(result.SavedRevision);
        Assert.Equal(0, repository.Writes);
    }

    [Theory]
    [InlineData(RequirementSaveStatus.RevisionConflict)]
    [InlineData(RequirementSaveStatus.ProjectNotFound)]
    public async Task AtomicRepositoryRejection_IsPreservedWithoutRetry(RequirementSaveStatus status)
    {
        var expectedResult = new RequirementSaveResult(status);
        var repository = new RecordingRepository { Save = (_, _) => Task.FromResult(expectedResult) };
        var result = await Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default);
        Assert.Same(expectedResult, result);
        Assert.Null(result.SavedRevision);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(1, repository.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedValidation_IsRejectedBeforeRepositoryAccess(bool unavailable)
    {
        var repository = new RecordingRepository();
        var validation = RequirementContractValidator.Validate(F.Request(floors: null), unavailable ? null : F.Catalogue());
        await Assert.ThrowsAsync<ArgumentException>(() => Writer(repository).SaveAsync(F.ProjectId, null, validation, default));
        Assert.Equal(0, repository.Reads);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task NullValidation_IsRejectedBeforeRepositoryAccess()
    {
        var repository = new RecordingRepository();
        await Assert.ThrowsAsync<ArgumentNullException>(() => Writer(repository).SaveAsync(F.ProjectId, null, null!, default));
        Assert.Equal(0, repository.Reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyIdentity_IsRejectedBeforeRepositoryAccess(bool project)
    {
        var repository = new RecordingRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => Writer(repository).SaveAsync(
            project ? Guid.Empty : F.ProjectId, project ? null : Guid.Empty, FullValidation(), default));
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task WrongProjectFromRepository_IsRejectedWithoutWrite()
    {
        var repository = new RecordingRepository { Current = F.Revision() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(repository).SaveAsync(
            Guid.Parse("40000000-0000-0000-0000-000000000004"), F.RevisionId, FullValidation(), default));
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task RevisionOverflow_IsRejectedWithoutWrite()
    {
        var repository = new RecordingRepository { Current = new(F.RevisionId, F.ProjectId, long.MaxValue,
            F.Schema, F.Version, F.Content(), F.Timestamp) };
        await Assert.ThrowsAsync<OverflowException>(() => Writer(repository).SaveAsync(F.ProjectId, F.RevisionId, FullValidation(), default));
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task CancellationBeforeRead_DoesNotAccessRepository()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var repository = new RecordingRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), source.Token));
        Assert.Equal(0, repository.Reads);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task CancellationDuringRead_PreventsWriteEvenIfDependencyReturnsNormally()
    {
        using var source = new CancellationTokenSource();
        var repository = new RecordingRepository { AfterRead = source.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), source.Token));
        Assert.Equal(source.Token, repository.ReadToken);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task CancellationDuringSave_IsForwardedWithoutRetry()
    {
        using var source = new CancellationTokenSource();
        var repository = new RecordingRepository { Save = (_, token) =>
        {
            source.Cancel();
            return Task.FromCanceled<RequirementSaveResult>(token);
        } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), source.Token));
        Assert.Equal(source.Token, repository.WriteToken);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task ConfirmedSave_IsNotReclassifiedWhenCancellationArrivesAfterCommit()
    {
        using var source = new CancellationTokenSource();
        var repository = new RecordingRepository { Save = (revision, _) =>
        {
            source.Cancel();
            return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.Saved, revision));
        } };
        var result = await Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), source.Token);
        Assert.Equal(RequirementSaveStatus.Saved, result.Status);
        Assert.Equal(source.Token, repository.ReadToken);
        Assert.Equal(source.Token, repository.WriteToken);
        Assert.Equal(1, repository.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyFailure_PropagatesUnchangedWithoutRetry(bool duringSave)
    {
        var failure = new InvalidOperationException("Synthetic dependency failure.");
        var repository = new RecordingRepository();
        if (duringSave) repository.Save = (_, _) => Task.FromException<RequirementSaveResult>(failure);
        else repository.ReadFailure = failure;
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default));
        Assert.Same(failure, observed);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(duringSave ? 1 : 0, repository.Writes);
    }

    [Fact]
    public async Task NullSaveResult_IsNotReportedAsSuccess()
    {
        var repository = new RecordingRepository { Save = (_, _) => Task.FromResult<RequirementSaveResult>(null!) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default));
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new ValidatedRequirementRevisionWriter(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ValidatedRequirementRevisionWriter(new RecordingRepository(), null!));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("project")]
    [InlineData("number")]
    [InlineData("schema")]
    [InlineData("catalogue")]
    [InlineData("timestamp")]
    [InlineData("floors")]
    [InlineData("rooms")]
    [InlineData("spaces")]
    [InlineData("relationships")]
    [InlineData("method")]
    public async Task InconsistentSavedSnapshot_IsNotExposedOrRetried(string changed)
    {
        var repository = new RecordingRepository { Save = (proposed, _) => Task.FromResult(
            new RequirementSaveResult(RequirementSaveStatus.Saved, Copy(proposed, changed))) };
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default));
        Assert.Equal("The repository returned an inconsistent saved revision.", observed.Message);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(1, repository.Writes);
        // The dependency may already have committed. This assertion makes no rollback claim.
    }

    [Fact]
    public async Task EquivalentRehydratedSavedSnapshot_IsAccepted()
    {
        RequirementSaveResult? returned = null;
        var repository = new RecordingRepository { Save = (proposed, _) =>
        {
            returned = new RequirementSaveResult(RequirementSaveStatus.Saved, Copy(proposed));
            return Task.FromResult(returned);
        } };
        var result = await Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default);
        Assert.Same(returned, result);
        Assert.NotSame(repository.Proposed, result.SavedRevision);
        Assert.NotSame(repository.Proposed!.Content, result.SavedRevision!.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainTransactionFailure_PropagatesWithoutRetryOrOutcomeGuess(bool rollbackUncertain)
    {
        // Infrastructure's concrete exceptions are internal. Do not bypass that boundary; these
        // synthetic exceptions verify opaque propagation, not a real transaction outcome.
        Exception failure = rollbackUncertain ? new SyntheticRollbackUncertainException() : new SyntheticCommitUnknownException();
        var repository = new RecordingRepository { Save = (_, _) => Task.FromException<RequirementSaveResult>(failure) };
        var observed = await Assert.ThrowsAnyAsync<Exception>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default));
        Assert.Same(failure, observed);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task UnrelatedDependencyCancellation_IsNotRewrittenAsCallerCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var failure = new OperationCanceledException(source.Token);
        var repository = new RecordingRepository { Save = (_, _) => Task.FromException<RequirementSaveResult>(failure) };
        var observed = await Assert.ThrowsAsync<OperationCanceledException>(() => Writer(repository).SaveAsync(F.ProjectId, null, FullValidation(), default));
        Assert.Same(failure, observed);
        Assert.Equal(source.Token, observed.CancellationToken);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task LastRepresentableRevision_CanBePrepared()
    {
        var repository = new RecordingRepository { Current = new(F.RevisionId, F.ProjectId, long.MaxValue - 1,
            F.Schema, F.Version, F.Content(), F.Timestamp) };
        var result = await Writer(repository).SaveAsync(F.ProjectId, F.RevisionId, FullValidation(), default);
        Assert.Equal(long.MaxValue, result.SavedRevision!.RevisionNumber);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task CancellationWhilePreparingMetadata_PreventsSave()
    {
        using var source = new CancellationTokenSource();
        var repository = new RecordingRepository();
        var writer = new ValidatedRequirementRevisionWriter(repository, new CallbackClock(() =>
        {
            source.Cancel();
            return F.Timestamp;
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.SaveAsync(F.ProjectId, null, FullValidation(), source.Token));
        Assert.Equal(1, repository.Reads);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task MetadataDependencyFailure_PreventsSave()
    {
        var failure = new InvalidOperationException("Synthetic clock failure.");
        var repository = new RecordingRepository();
        var writer = new ValidatedRequirementRevisionWriter(repository, new CallbackClock(() => throw failure));
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync(F.ProjectId, null, FullValidation(), default));
        Assert.Same(failure, observed);
        Assert.Equal(0, repository.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingSaves_KeepSameExpectedHeadAndPreserveAtomicConflict(bool subsequent)
    {
        var repository = new CoordinatedRepository(subsequent ? F.Revision() : null);
        var writer = new ValidatedRequirementRevisionWriter(repository, new FixedTimeProvider());
        var expected = subsequent ? F.RevisionId : (Guid?)null;
        var results = await Task.WhenAll(
            writer.SaveAsync(F.ProjectId, expected, FullValidation(), default),
            writer.SaveAsync(F.ProjectId, expected, FullValidation(), default)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(results, item => item.Status == RequirementSaveStatus.Saved);
        Assert.Single(results, item => item.Status == RequirementSaveStatus.RevisionConflict);
        Assert.Equal(2, repository.Reads);
        Assert.Equal(2, repository.Writes);
        Assert.Equal(subsequent ? 2L : 1L, repository.Head!.RevisionNumber);
        // This is a coordinated atomic test double, not MySQL concurrency evidence.
    }

    private sealed class SyntheticCommitUnknownException : Exception { }
    private sealed class SyntheticRollbackUncertainException : Exception { }

    private static RequirementRevision Copy(RequirementRevision source, string? changed = null)
    {
        var content = source.Content;
        return new RequirementRevision(
            changed == "id" ? Guid.NewGuid() : source.Id,
            changed == "project" ? Guid.NewGuid() : source.ProjectId,
            changed == "number" ? source.RevisionNumber + 1 : source.RevisionNumber,
            changed == "schema" ? "synthetic-other-schema" : source.SchemaVersion,
            changed == "catalogue" ? "synthetic-other-catalogue" : source.CatalogueVersion,
            new RequirementContent(changed == "floors" ? content.FloorCount + 1 : content.FloorCount,
                changed == "rooms" ? content.GroundFloorRooms.Select(room => room with { Count = room.Count + 1 }) : content.GroundFloorRooms,
                changed == "spaces" ? [] : content.AdditionalSpaces,
                changed == "relationships" ? [] : content.Relationships,
                changed == "method" ? null : content.DesignMethod),
            changed == "timestamp" ? source.CreatedAtUtc.AddSeconds(1) : source.CreatedAtUtc);
    }

    private sealed class CallbackClock(Func<DateTimeOffset> getTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => getTime();
    }

    private sealed class CoordinatedRepository : IRequirementRevisionRepository
    {
        private readonly RequirementRevision? _initial;
        private readonly object _gate = new();
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        public int Reads => _reads;
        public int Writes { get; private set; }
        public RequirementRevision? Head { get; private set; }

        public CoordinatedRepository(RequirementRevision? initial)
        {
            _initial = initial;
            Head = initial;
        }

        public async Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
        {
            // Both reads observe the same initial head before either write may begin.
            if (Interlocked.Increment(ref _reads) == 2) _bothRead.TrySetResult();
            await _bothRead.Task.WaitAsync(cancellationToken);
            return _initial;
        }

        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Writes++;
                if (Head?.Id != expectedRevisionId)
                    return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.RevisionConflict));
                Head = proposedRevision;
                return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.Saved, proposedRevision));
            }
        }
    }

    private static RequirementValidationResult FullValidation() => F.Validate(new SaveRequirementsRequest(
        null, F.Version, 1, F.Request().GroundFloorRooms, [new("SPACE_TEXT", "text")],
        [new("ROOM_A", "ROOM_B", "NEAR")], "METHOD_A"));

    private static ValidatedRequirementRevisionWriter Writer(RecordingRepository repository) => new(repository, new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => F.Timestamp.ToUniversalTime();
    }

    private sealed class RecordingRepository : IRequirementRevisionRepository
    {
        public RequirementRevision? Current { get; init; }
        public RequirementRevision? Proposed { get; private set; }
        public Guid ReadProject { get; private set; }
        public Guid? Expected { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public CancellationToken WriteToken { get; private set; }
        public Action? AfterRead { get; init; }
        public Exception? ReadFailure { get; set; }
        public Func<RequirementRevision, CancellationToken, Task<RequirementSaveResult>>? Save { get; set; }

        public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
        {
            Reads++;
            ReadProject = projectId;
            ReadToken = cancellationToken;
            AfterRead?.Invoke();
            return ReadFailure is null ? Task.FromResult(Current) : Task.FromException<RequirementRevision?>(ReadFailure);
        }

        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This writer reads only the current head.");

        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision, CancellationToken cancellationToken)
        {
            Writes++;
            Expected = expectedRevisionId;
            Proposed = proposedRevision;
            WriteToken = cancellationToken;
            return Save?.Invoke(proposedRevision, cancellationToken)
                ?? Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.Saved, proposedRevision));
        }
    }
}
