using BuildMind.Application.Features.Requirements.Contracts;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

// Direct internal-constructor guards (including undefined validation statuses) are deliberately deferred.
public sealed class RequirementValidationResultTests
{
    [Fact]
    public void ValidResult_PreservesExactPolicyProvenance()
    {
        var result = F.Validate(F.Request());
        F.AssertValid(result);
        Assert.Equal(F.Schema, result.SchemaVersion);
        Assert.Equal(F.Version, result.CatalogueVersion);
        Assert.Equal(2, result.Content!.GroundFloorRooms.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedResult_ExposesNoValidatedSnapshot(bool unavailable)
    {
        var result = RequirementContractValidator.Validate(F.Request(floors: null), unavailable ? null : F.Catalogue());
        Assert.Equal(unavailable ? RequirementValidationStatus.PolicyUnavailable : RequirementValidationStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Issues);
        Assert.Null(result.Content);
        Assert.Null(result.SchemaVersion);
        Assert.Null(result.CatalogueVersion);
    }

    [Fact]
    public void CreateRevision_UsesValidatedContentAndVersions()
    {
        var result = F.Validate(F.Request());
        F.AssertValid(result);
        var revision = result.CreateRevision(F.RevisionId, F.ProjectId, 1, F.Timestamp);
        Assert.Equal(F.RevisionId, revision.Id);
        Assert.Equal(F.ProjectId, revision.ProjectId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Same(result.Content, revision.Content);
        Assert.Equal(result.SchemaVersion, revision.SchemaVersion);
        Assert.Equal(result.CatalogueVersion, revision.CatalogueVersion);
        Assert.Equal(F.Timestamp.UtcDateTime, revision.CreatedAtUtc.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, revision.CreatedAtUtc.Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateRevision_RejectsFailedValidation(bool unavailable)
    {
        var result = RequirementContractValidator.Validate(F.Request(floors: null), unavailable ? null : F.Catalogue());
        Assert.Throws<InvalidOperationException>(() => result.CreateRevision(F.RevisionId, F.ProjectId, 1, F.Timestamp));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateRevision_RejectsEmptyIdentity(bool revisionId)
    {
        var result = F.Validate(F.Request());
        F.AssertValid(result);
        Assert.Throws<ArgumentException>(revisionId ? "id" : "projectId", () => result.CreateRevision(
            revisionId ? Guid.Empty : F.RevisionId, revisionId ? F.ProjectId : Guid.Empty, 1, F.Timestamp));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void CreateRevision_RejectsNonPositiveRevisionNumber(long number)
    {
        var result = F.Validate(F.Request());
        F.AssertValid(result);
        Assert.Throws<ArgumentOutOfRangeException>("revisionNumber", () => result.CreateRevision(F.RevisionId, F.ProjectId, number, F.Timestamp));
    }

    [Fact]
    public void CreateRevision_DoesNotMutateValidationResult()
    {
        var result = F.Validate(F.Request());
        F.AssertValid(result);
        var content = result.Content;
        var first = result.CreateRevision(F.RevisionId, F.ProjectId, 1, F.Timestamp);
        var next = result.CreateRevision(Guid.Parse("10000000-0000-0000-0000-000000000003"), F.ProjectId, 2, F.Timestamp.AddMinutes(1));
        F.AssertValid(result);
        Assert.Same(content, result.Content);
        Assert.Same(content, first.Content);
        Assert.Same(content, next.Content);
        Assert.Equal(1, first.RevisionNumber);
        Assert.Equal(2, next.RevisionNumber);
    }

    [Fact]
    public void Validation_AgainstLaterCatalogue_DoesNotRewriteEarlierResult()
    {
        var first = F.Validate(F.Request());
        var second = RequirementContractValidator.Validate(F.Request(version: "test-catalogue-v2"),
            F.Catalogue(version: "test-catalogue-v2", schema: "test-schema-v2"));
        F.AssertValid(first);
        Assert.Equal(RequirementValidationStatus.Valid, second.Status);
        Assert.Empty(second.Issues);
        Assert.NotNull(second.Content);
        Assert.Equal("test-schema-v2", second.SchemaVersion);
        Assert.Equal("test-catalogue-v2", second.CatalogueVersion);
        var revision = second.CreateRevision(F.RevisionId, F.ProjectId, 1, F.Timestamp);
        Assert.Equal("test-schema-v2", revision.SchemaVersion);
        Assert.Equal("test-catalogue-v2", revision.CatalogueVersion);
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(), F.Catalogue(version: "test-catalogue-v2")),
            "CATALOGUE_VERSION_MISMATCH", "catalogueVersion");
        F.AssertValid(first);
    }
}
