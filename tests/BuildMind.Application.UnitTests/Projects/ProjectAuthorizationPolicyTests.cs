using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Application.UnitTests.Projects;

public sealed class ProjectAuthorizationPolicyTests
{
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    [Fact]
    public void InitialVersion_ShouldBeOne()
    {
        Assert.Equal(
            1L, ProjectAuthorizationPolicy.InitialVersion);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(100L)]
    [InlineData(long.MaxValue)]
    public void PositiveVersion_ShouldBeValid(long version)
    {
        Assert.True(
            ProjectAuthorizationPolicy.IsValid(version));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void InvalidVersion_ShouldBeRejected(long version)
    {
        Assert.False(
            ProjectAuthorizationPolicy.IsValid(version));
    }

    [Fact]
    public void Version_ShouldIncreaseMonotonically()
    {
        var version = ProjectAuthorizationPolicy.InitialVersion;

        for (var i = 0; i < 100; i++)
        {
            var next =
                ProjectAuthorizationPolicy.NextVersion(version);

            Assert.True(next > version);

            version = next;
        }

        Assert.Equal(101L, version);
    }

    [Fact]
    public void InvalidVersion_CannotIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProjectAuthorizationPolicy.NextVersion(0));
    }

    [Fact]
    public void MaximumVersion_ShouldNotWrapAround()
    {
        Assert.Throws<OverflowException>(
            () => ProjectAuthorizationPolicy.NextVersion(
                long.MaxValue));
    }

    [Fact]
    public void MatchingVersion_ShouldPass()
    {
        Assert.True(
            ProjectAuthorizationPolicy.Matches(5, 5));
    }

    [Fact]
    public void StaleAuthorization_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.Matches(6, 5));
    }

    [Fact]
    public void InvalidMatchingVersions_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.Matches(0, 0));
    }

    [Fact]
    public void CorrectOwner_ShouldPass()
    {
        Assert.True(
            ProjectAuthorizationPolicy.MatchesOwner(
                _projectId, _projectId,
                _ownerId, _ownerId));
    }

    [Fact]
    public void CrossAccountAccess_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.MatchesOwner(
                _projectId, _projectId,
                _ownerId, Guid.NewGuid()));
    }

    [Fact]
    public void WrongProject_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.MatchesOwner(
                _projectId, Guid.NewGuid(),
                _ownerId, _ownerId));
    }

    [Fact]
    public void EmptyActor_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.MatchesOwner(
                _projectId, _projectId,
                _ownerId, Guid.Empty));
    }

    [Fact]
    public void ValidMutationEvidence_ShouldPass()
    {
        Assert.True(
            ProjectAuthorizationPolicy.MatchesMutationEvidence(
                _projectId, _projectId,
                _ownerId, _ownerId,
                2, 2, 10, 10));
    }

    [Fact]
    public void StaleRevision_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.MatchesMutationEvidence(
                _projectId, _projectId,
                _ownerId, _ownerId,
                2, 2, 10, 9));
    }

    [Fact]
    public void RevokedAuthorization_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.MatchesMutationEvidence(
                _projectId, _projectId,
                _ownerId, _ownerId,
                3, 2, 10, 10));
    }

    [Fact]
    public void CrossAccountMutation_ShouldFail()
    {
        Assert.False(
            ProjectAuthorizationPolicy.MatchesMutationEvidence(
                _projectId, _projectId,
                _ownerId, Guid.NewGuid(),
                2, 2, 10, 10));
    }
}