using BuildMind.Application.Features.Requirements.Integration;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class IsolatedAtomicRequirementMutationContractsTests
{
    [Fact]
    public void Scope_PreservesExactTrustedInputs()
    {
        var project = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var expected = Guid.NewGuid();
        var scope = new IsolatedRequirementMutationScope(project, actor, operation, expected);

        Assert.Equal(project, scope.ProjectId);
        Assert.Equal(actor, scope.TrustedActorId);
        Assert.Equal(operation, scope.OperationId);
        Assert.Equal(expected, scope.ExpectedRequirementRevisionId);
    }

    [Fact]
    public void Scope_AllowsNullExpectedRevisionForFirstSave()
    {
        var scope = new IsolatedRequirementMutationScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        Assert.Null(scope.ExpectedRequirementRevisionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Scope_RejectsEmptyRequiredIdentity(int which)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ids[which] = Guid.Empty;
        Assert.Throws<ArgumentException>(() =>
            new IsolatedRequirementMutationScope(ids[0], ids[1], ids[2], null));
    }

    [Fact]
    public void Scope_RejectsEmptyExpectedRevision()
    {
        Assert.Throws<ArgumentException>(() =>
            new IsolatedRequirementMutationScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void SavedOutcome_RequiresValidRevision()
    {
        Assert.Throws<ArgumentException>(() => new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.Saved));
        Assert.Throws<ArgumentException>(() => new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.Saved, Guid.Empty));
        var id = Guid.NewGuid();
        Assert.Equal(id, new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.Saved, id).SavedRequirementRevisionId);
    }

    [Theory]
    [InlineData(IsolatedRequirementMutationStatus.AccessDenied)]
    [InlineData(IsolatedRequirementMutationStatus.AuthorizationChanged)]
    [InlineData(IsolatedRequirementMutationStatus.RequirementRevisionConflict)]
    [InlineData(IsolatedRequirementMutationStatus.ProjectNotFound)]
    [InlineData(IsolatedRequirementMutationStatus.DependencyUnavailable)]
    [InlineData(IsolatedRequirementMutationStatus.DependencyTimeout)]
    [InlineData(IsolatedRequirementMutationStatus.CommitOutcomeUnknown)]
    public void NonSuccessOutcome_RejectsSavedRevision(IsolatedRequirementMutationStatus status)
    {
        Assert.Null(new IsolatedRequirementMutationOutcome(status).SavedRequirementRevisionId);
        Assert.Throws<ArgumentException>(() => new IsolatedRequirementMutationOutcome(status, Guid.NewGuid()));
    }

    [Fact]
    public void Outcome_RejectsUnknownStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IsolatedRequirementMutationOutcome((IsolatedRequirementMutationStatus)999));
    }
}
