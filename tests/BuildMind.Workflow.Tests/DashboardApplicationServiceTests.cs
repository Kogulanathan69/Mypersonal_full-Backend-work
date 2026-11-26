using BuildMind.Application.Features.Dashboard.Abstractions;
using BuildMind.Application.Features.Dashboard.Contracts;
using BuildMind.Application.Features.Dashboard.Queries;
using BuildMind.Application.Features.Dashboard.Services;

namespace BuildMind.Workflow.Tests;

/// <summary>Synthetic consumer-port tests only: no production authentication or database isolation is exercised.</summary>
public sealed class DashboardApplicationServiceTests
{
    private static readonly Guid Owner = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOwner = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private static DashboardOwnedProject Row(int id, string stage = "TEST_STAGE", Guid? owner = null) =>
        new(owner ?? Owner, new DashboardProjectDto(new Guid(id, 0, 0, new byte[8]), $"Synthetic project {id}", stage));

    private static DashboardProjectReadResult Loaded(params DashboardOwnedProject[] rows) =>
        new(DashboardProjectReadStatus.Loaded, Owner, rows);

    private static DashboardSummaryQueryHandler Handler(Source source) =>
        new(new DashboardApplicationService(source));

    private static void AssertFailure(DashboardSummaryResult result, DashboardSummaryStatus expected)
    {
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task EmptyOwnerProjectSet_ReturnsSuccessfulZeroSummary()
    {
        var source = new Source(Loaded());
        var result = await Handler(source).HandleAsync(new(), CancellationToken.None);
        Assert.Equal(DashboardSummaryStatus.Available, result.Status);
        Assert.NotNull(result.Summary);
        Assert.Equal(0, result.Summary.TotalProjects);
        Assert.Empty(result.Summary.Projects);
        Assert.Empty(result.Summary.StageCounts);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task SingleProject_PreservesSafeProjectionAndStage()
    {
        var row = Row(1);
        var result = await Handler(new Source(Loaded(row))).HandleAsync(new(), CancellationToken.None);
        Assert.Equal(DashboardSummaryStatus.Available, result.Status);
        var summary = Assert.IsType<DashboardSummaryDto>(result.Summary);
        Assert.Equal(1, summary.TotalProjects);
        Assert.Same(row.Project, Assert.Single(summary.Projects));
        var count = Assert.Single(summary.StageCounts);
        Assert.Equal("TEST_STAGE", count.StageCode);
        Assert.Equal(1, count.Count);
    }

    [Fact]
    public async Task MultipleProjects_AggregateExactStagesWithoutInventingValues()
    {
        var result = await Handler(new Source(Loaded(Row(1, "TEST_B"), Row(2, "TEST_A"), Row(3, "TEST_B"))))
            .HandleAsync(new(), CancellationToken.None);
        Assert.Equal(DashboardSummaryStatus.Available, result.Status);
        var summary = Assert.IsType<DashboardSummaryDto>(result.Summary);
        Assert.Equal(3, summary.TotalProjects);
        Assert.Collection(summary.StageCounts,
            stage => { Assert.Equal("TEST_A", stage.StageCode); Assert.Equal(1, stage.Count); },
            stage => { Assert.Equal("TEST_B", stage.StageCode); Assert.Equal(2, stage.Count); });
        Assert.Equal(summary.TotalProjects, summary.StageCounts.Sum(stage => stage.Count));
    }

    [Fact]
    public async Task StageComparison_PreservesExistingOrdinalContract()
    {
        var result = await Handler(new Source(Loaded(Row(1, "TEST"), Row(2, "test"))))
            .HandleAsync(new(), CancellationToken.None);
        Assert.Equal(2, result.Summary!.StageCounts.Count);
        Assert.All(result.Summary.StageCounts, stage => Assert.Equal(1, stage.Count));
    }

    [Fact]
    public async Task DuplicateProjectIds_FailWithoutPartialSummary()
    {
        var result = await Handler(new Source(Loaded(Row(1, "TEST_A"), Row(1, "TEST_B"))))
            .HandleAsync(new(), CancellationToken.None);
        AssertFailure(result, DashboardSummaryStatus.InvalidDependencyResponse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOwnerRows_RejectEntireResponse(bool includesOwnedRow)
    {
        var rows = includesOwnedRow ? new[] { Row(1), Row(2, owner: OtherOwner) } : [Row(2, owner: OtherOwner)];
        var result = await Handler(new Source(Loaded(rows))).HandleAsync(new(), CancellationToken.None);
        AssertFailure(result, DashboardSummaryStatus.InvalidDependencyResponse);
    }

    [Fact]
    public async Task UnauthenticatedSource_ReturnsNoProjectData()
    {
        var result = await Handler(new Source(new DashboardProjectReadResult(DashboardProjectReadStatus.Unauthenticated)))
            .HandleAsync(new(), CancellationToken.None);
        AssertFailure(result, DashboardSummaryStatus.Unauthenticated);
    }

    [Fact]
    public async Task UnavailableSource_IsNotSuccessfulEmptyDashboard()
    {
        var result = await Handler(new Source(new DashboardProjectReadResult(DashboardProjectReadStatus.Unavailable)))
            .HandleAsync(new(), CancellationToken.None);
        AssertFailure(result, DashboardSummaryStatus.DependencyUnavailable);
    }

    [Fact]
    public async Task NullSourceResponse_FailsClosed()
    {
        var source = new Source(_ => Task.FromResult<DashboardProjectReadResult>(null!));
        AssertFailure(await Handler(source).HandleAsync(new(), CancellationToken.None), DashboardSummaryStatus.InvalidDependencyResponse);
    }

    [Fact]
    public async Task DependencyException_ReturnsFailureWithoutExceptionDetails()
    {
        var source = new Source(_ => Task.FromException<DashboardProjectReadResult>(new InvalidOperationException("SYNTHETIC_PRIVATE_DETAIL")));
        AssertFailure(await Handler(source).HandleAsync(new(), CancellationToken.None), DashboardSummaryStatus.DependencyUnavailable);
    }

    [Fact]
    public async Task DependencyTimeout_IsDistinctFromEmptyOrUnavailable()
    {
        var source = new Source(_ => Task.FromException<DashboardProjectReadResult>(new TimeoutException("SYNTHETIC_PRIVATE_DETAIL")));
        AssertFailure(await Handler(source).HandleAsync(new(), CancellationToken.None), DashboardSummaryStatus.DependencyTimeout);
    }

    [Fact]
    public async Task InvalidSourceConstruction_ReturnsInvalidDependencyResponse()
    {
        var source = new Source(_ => Task.FromResult(new DashboardProjectReadResult(DashboardProjectReadStatus.Loaded, Guid.Empty, [])));
        AssertFailure(await Handler(source).HandleAsync(new(), CancellationToken.None), DashboardSummaryStatus.InvalidDependencyResponse);
    }

    [Fact]
    public async Task DependencyOwnedCancellation_DoesNotPretendRequestWasCancelled()
    {
        using var dependencyCancellation = new CancellationTokenSource();
        dependencyCancellation.Cancel();
        var source = new Source(_ => Task.FromCanceled<DashboardProjectReadResult>(dependencyCancellation.Token));
        AssertFailure(await Handler(source).HandleAsync(new(), CancellationToken.None), DashboardSummaryStatus.DependencyUnavailable);
    }

    [Fact]
    public async Task AlreadyCancelledRequest_DoesNotCallSource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new Source(Loaded());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(source).HandleAsync(new(), cancellation.Token));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task ServiceDirectly_RejectsAlreadyCancelledRequest()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new Source(Loaded());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DashboardApplicationService(source).GetSummaryAsync(cancellation.Token));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task InFlightCancellation_IsForwardedAndPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Loaded();
        });
        var pending = Handler(source).HandleAsync(new(), cancellation.Token);
        Assert.False(pending.IsCompleted);
        Assert.Equal(cancellation.Token, source.LastToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task SourceIgnoringCancellation_CannotReturnSuccessfulSummaryAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source(_ => { cancellation.Cancel(); return Task.FromResult(Loaded(Row(1))); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(source).HandleAsync(new(), cancellation.Token));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("invalid")]
    [InlineData("unexpected")]
    public async Task CancellationTakesPrecedenceOverDependencyFailure(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source(_ =>
        {
            cancellation.Cancel();
            Exception exception = kind switch
            {
                "timeout" => new TimeoutException(),
                "invalid" => new ArgumentException(),
                _ => new InvalidOperationException()
            };
            return Task.FromException<DashboardProjectReadResult>(exception);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(source).HandleAsync(new(), cancellation.Token));
    }

    [Fact]
    public async Task AsyncSource_IsAwaitedWithoutBlocking()
    {
        var completion = new TaskCompletionSource<DashboardProjectReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var source = new Source(_ => completion.Task);
        var pending = Handler(source).HandleAsync(new(), cancellation.Token);
        Assert.False(pending.IsCompleted);
        Assert.Equal(cancellation.Token, source.LastToken);
        completion.SetResult(Loaded(Row(1)));
        var result = await pending;
        Assert.Equal(DashboardSummaryStatus.Available, result.Status);
        Assert.Equal(1, result.Summary!.TotalProjects);
    }

    [Fact]
    public void NullQuery_IsRejectedBeforeSourceAccess()
    {
        var source = new Source(Loaded());
        Assert.Throws<ArgumentNullException>("query", () => { _ = Handler(source).HandleAsync(null!, CancellationToken.None); });
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void NullDependencies_AreRejected()
    {
        Assert.Throws<ArgumentNullException>("source", () => new DashboardApplicationService(null!));
        Assert.Throws<ArgumentNullException>("service", () => new DashboardSummaryQueryHandler(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedSource_RejectsMissingOrEmptyOwnerIdentity(bool missing) =>
        Assert.Throws<ArgumentException>("authenticatedOwnerId", () => new DashboardProjectReadResult(
            DashboardProjectReadStatus.Loaded, missing ? null : Guid.Empty, []));

    [Fact]
    public void OwnerBinding_RejectsEmptyOwnerAndNullProject()
    {
        Assert.Throws<ArgumentException>("ownerId", () => new DashboardOwnedProject(Guid.Empty, Row(1).Project));
        Assert.Throws<ArgumentNullException>("project", () => new DashboardOwnedProject(Owner, null!));
    }

    [Fact]
    public void LoadedSource_RejectsMissingCollectionAndNullRows()
    {
        Assert.Throws<ArgumentNullException>("projects", () => new DashboardProjectReadResult(DashboardProjectReadStatus.Loaded, Owner));
        Assert.Throws<ArgumentException>("projects", () => new DashboardProjectReadResult(DashboardProjectReadStatus.Loaded, Owner, [null!]));
    }

    [Theory]
    [InlineData(DashboardProjectReadStatus.Unauthenticated)]
    [InlineData(DashboardProjectReadStatus.Unavailable)]
    public void FailedSource_CannotCarryOwnerOrProjectData(DashboardProjectReadStatus status)
    {
        Assert.Throws<ArgumentException>(() => new DashboardProjectReadResult(status, Owner));
        Assert.Throws<ArgumentException>(() => new DashboardProjectReadResult(status, projects: []));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedSourceStatus_IsRejected(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>("status", () => new DashboardProjectReadResult((DashboardProjectReadStatus)value));

    [Fact]
    public async Task SourceCopiesRowsAndSummaryCollectionsRemainImmutable()
    {
        var rows = new[] { Row(1) };
        var loaded = Loaded(rows);
        rows[0] = Row(2, owner: OtherOwner);
        Assert.Throws<NotSupportedException>(() => ((IList<DashboardOwnedProject>)loaded.Projects!).Clear());
        var result = await Handler(new Source(loaded)).HandleAsync(new(), CancellationToken.None);
        Assert.Equal(DashboardSummaryStatus.Available, result.Status);
        Assert.Equal(Row(1).Project.ProjectId, Assert.Single(result.Summary!.Projects).ProjectId);
        Assert.Throws<NotSupportedException>(() => ((IList<DashboardProjectDto>)result.Summary.Projects).Clear());
    }

    [Theory]
    [InlineData(DashboardSummaryStatus.Unauthenticated)]
    [InlineData(DashboardSummaryStatus.DependencyUnavailable)]
    [InlineData(DashboardSummaryStatus.DependencyTimeout)]
    [InlineData(DashboardSummaryStatus.InvalidDependencyResponse)]
    public void FailedSummary_NeverCarriesPayload(DashboardSummaryStatus status) =>
        AssertFailure(DashboardSummaryResult.Failure(status), status);

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedSummaryStatus_IsRejected(int status) =>
        Assert.Throws<ArgumentOutOfRangeException>("status", () => DashboardSummaryResult.Failure((DashboardSummaryStatus)status));

    [Fact]
    public void SummaryFactories_RequireConsistentPayload()
    {
        Assert.Throws<ArgumentException>("status", () => DashboardSummaryResult.Failure(DashboardSummaryStatus.Available));
        Assert.Throws<ArgumentNullException>("summary", () => DashboardSummaryResult.Available(null!));
    }

    private sealed class Source : IDashboardProjectSource
    {
        private readonly Func<CancellationToken, Task<DashboardProjectReadResult>> _read;
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Source(DashboardProjectReadResult result) : this(_ => Task.FromResult(result)) { }
        public Source(Func<CancellationToken, Task<DashboardProjectReadResult>> read) => _read = read;

        public Task<DashboardProjectReadResult> GetForCurrentUserAsync(CancellationToken cancellationToken)
        {
            Calls++;
            LastToken = cancellationToken;
            return _read(cancellationToken);
        }
    }
}
