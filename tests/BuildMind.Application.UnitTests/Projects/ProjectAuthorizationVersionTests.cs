using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Application.UnitTests.Projects;

/// <summary>STEP 05 decision 4: domain rules for the project authorization version.</summary>
public sealed class ProjectAuthorizationVersionTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_and_restored_projects_carry_a_positive_version()
    {
        Assert.Equal(1, new Project { OwnerId = Guid.NewGuid(), Name = "p" }.AuthorizationVersion);
        Assert.Equal(7, Restore(7).AuthorizationVersion);
        Assert.Equal(1, Project.Restore(Guid.NewGuid(), Guid.NewGuid(), "p", null, null, "Created", 1, Created, null).AuthorizationVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_versions_are_rejected(long version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(version));

    [Fact]
    public void Metadata_edits_change_the_revision_but_never_the_authorization_version()
    {
        var project = Restore(5);

        var edited = project.WithUpdatedDetails("renamed", "Residential", "Kandy", Created.AddHours(1));

        Assert.Equal(project.Revision + 1, edited.Revision);
        Assert.Equal(5, edited.AuthorizationVersion);
        Assert.Equal(project.OwnerId, edited.OwnerId);
    }

    private static Project Restore(long authorizationVersion) =>
        Project.Restore(Guid.NewGuid(), Guid.NewGuid(), "p", null, null, "Created", 3, Created, null, authorizationVersion);
}
