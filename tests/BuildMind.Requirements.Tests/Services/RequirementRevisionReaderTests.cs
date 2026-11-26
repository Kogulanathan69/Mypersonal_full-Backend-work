using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Services;

public sealed class RequirementRevisionReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingSnapshot_IsReturnedWithoutMutation(bool exact)
    {
        var snapshot = F.Revision();
        var repository = new ReadRepository { Read = (_, _, _) => Task.FromResult<RequirementRevision?>(snapshot) };
        using var source = new CancellationTokenSource();
        var result = await Read(repository, exact, source.Token);
        Assert.Same(snapshot, result);
        Assert.Equal(F.ProjectId, repository.Project);
        Assert.Equal(exact ? F.RevisionId : (Guid?)null, repository.Revision);
        Assert.Equal(source.Token, repository.Token);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(F.Schema, result!.SchemaVersion);
        Assert.Equal(F.Version, result.CatalogueVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSnapshot_RemainsNull(bool exact)
    {
        var repository = new ReadRepository();
        Assert.Null(await Read(repository, exact));
        Assert.Equal(1, repository.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyProject_IsRejectedBeforeRepositoryAccess(bool exact)
    {
        var repository = new ReadRepository();
        var reader = new RequirementRevisionReader(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => exact
            ? reader.GetAsync(Guid.Empty, F.RevisionId, default) : reader.GetCurrentAsync(Guid.Empty, default));
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task EmptyRevision_IsRejectedBeforeRepositoryAccess()
    {
        var repository = new ReadRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => new RequirementRevisionReader(repository).GetAsync(F.ProjectId, Guid.Empty, default));
        Assert.Equal(0, repository.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongProjectSnapshot_IsNeverReturned(bool exact)
    {
        var wrong = new RequirementRevision(F.RevisionId, Guid.Parse("90000000-0000-0000-0000-000000000009"),
            1, F.Schema, F.Version, F.Content(), F.Timestamp);
        var repository = new ReadRepository { Read = (_, _, _) => Task.FromResult<RequirementRevision?>(wrong) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Read(repository, exact));
        Assert.Equal(1, repository.Reads);
    }

    [Fact]
    public async Task WrongExactRevision_IsNeverReturned()
    {
        var wrong = new RequirementRevision(Guid.Parse("80000000-0000-0000-0000-000000000008"), F.ProjectId,
            1, F.Schema, F.Version, F.Content(), F.Timestamp);
        var repository = new ReadRepository { Read = (_, _, _) => Task.FromResult<RequirementRevision?>(wrong) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Read(repository, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeRead_DoesNotAccessRepository(bool exact)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var repository = new ReadRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(repository, exact, source.Token));
        Assert.Equal(0, repository.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterDependencyReturns_DoesNotExposeSnapshot(bool exact)
    {
        using var source = new CancellationTokenSource();
        var repository = new ReadRepository { Read = (_, _, _) =>
        {
            source.Cancel();
            return Task.FromResult<RequirementRevision?>(F.Revision());
        } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(repository, exact, source.Token));
        Assert.Equal(source.Token, repository.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyFailure_IsNotConvertedToAbsenceOrRetried(bool exact)
    {
        var failure = new TimeoutException("Synthetic dependency failure.");
        var repository = new ReadRepository { Read = (_, _, _) => Task.FromException<RequirementRevision?>(failure) };
        var observed = await Assert.ThrowsAsync<TimeoutException>(() => Read(repository, exact));
        Assert.Same(failure, observed);
        Assert.Equal(1, repository.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyCancellation_PreservesOriginalToken(bool exact)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var failure = new OperationCanceledException(source.Token);
        var repository = new ReadRepository { Read = (_, _, _) => Task.FromException<RequirementRevision?>(failure) };
        var observed = await Assert.ThrowsAsync<OperationCanceledException>(() => Read(repository, exact));
        Assert.Same(failure, observed);
        Assert.Equal(source.Token, observed.CancellationToken);
        Assert.Equal(1, repository.Reads);
    }

    [Fact]
    public void Constructor_RejectsMissingRepository() =>
        Assert.Throws<ArgumentNullException>(() => new RequirementRevisionReader(null!));

    private static Task<RequirementRevision?> Read(ReadRepository repository, bool exact, CancellationToken token = default)
    {
        var reader = new RequirementRevisionReader(repository);
        return exact ? reader.GetAsync(F.ProjectId, F.RevisionId, token) : reader.GetCurrentAsync(F.ProjectId, token);
    }

    private sealed class ReadRepository : IRequirementRevisionRepository
    {
        public Func<Guid, Guid?, CancellationToken, Task<RequirementRevision?>> Read { get; init; } =
            (_, _, _) => Task.FromResult<RequirementRevision?>(null);
        public int Reads { get; private set; }
        public Guid Project { get; private set; }
        public Guid? Revision { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken) =>
            Invoke(projectId, null, cancellationToken);
        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
            Invoke(projectId, revisionId, cancellationToken);

        private Task<RequirementRevision?> Invoke(Guid projectId, Guid? revisionId, CancellationToken cancellationToken)
        {
            Reads++;
            Project = projectId;
            Revision = revisionId;
            Token = cancellationToken;
            return Read(projectId, revisionId, cancellationToken);
        }

        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A reader must never invoke a save.");
    }
}
