using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.ValueObjects;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class RequirementContractValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidRequirements()
    {
        var result = F.Validate(F.Request());
        F.AssertValid(result);
        Assert.Equal(1, result.Content!.FloorCount);
        Assert.Equal(new RoomCount[] { new("ROOM_A", 1), new("ROOM_B", 1) }, result.Content.GroundFloorRooms);
        Assert.Empty(result.Content.AdditionalSpaces);
        Assert.Empty(result.Content.Relationships);
        Assert.Null(result.Content.DesignMethod);
    }

    [Fact]
    public void Validate_AcceptsAllSupportedOptionalSections()
    {
        var request = new SaveRequirementsRequest(null, F.Version, 2, F.Request().GroundFloorRooms,
            [new("SPACE_A", null), new("SPACE_TEXT", "hello")], [new("ROOM_A", "ROOM_B", "NEAR")], "METHOD_A");
        var result = F.Validate(request);
        F.AssertValid(result);
        Assert.Equal(2, result.Content!.FloorCount);
        Assert.Equal(new AdditionalSpaceSelection[] { new("SPACE_A", null), new("SPACE_TEXT", "hello") }, result.Content.AdditionalSpaces);
        Assert.Equal(new RoomRelationshipPreference("ROOM_A", "ROOM_B", "NEAR"), Assert.Single(result.Content.Relationships));
        Assert.Equal("METHOD_A", result.Content.DesignMethod);
    }

    [Fact]
    public void Validate_RejectsMissingRequest() => F.AssertIssue(F.Validate(null), "REQUIRED", "request");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_AcceptsNullOrNonemptyExpectedRevision(bool supplied) =>
        F.AssertValid(F.Validate(F.Request(expected: supplied ? F.RevisionId : null)));

    [Fact]
    public void Validate_RejectsEmptyExpectedRevision() =>
        F.AssertIssue(F.Validate(F.Request(expected: Guid.Empty)), "INVALID_REVISION", "expectedRevisionId");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("other-version")]
    [InlineData("TEST-CATALOGUE-V1")]
    public void Validate_RejectsCatalogueVersionMismatch(string? version) =>
        F.AssertIssue(F.Validate(F.Request(version: version)), "CATALOGUE_VERSION_MISMATCH", "catalogueVersion");

    [Fact]
    public void Validate_RejectsMissingFloorCount() =>
        F.AssertIssue(F.Validate(F.Request(floors: null)), "REQUIRED", "floorCount");

    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(int.MaxValue, false)]
    public void Validate_AppliesInclusiveFloorBounds(int count, bool valid)
    {
        var result = F.Validate(F.Request(floors: count));
        if (valid) F.AssertValid(result);
        else F.AssertIssue(result, "OUT_OF_BOUNDS", "floorCount");
    }

    [Fact]
    public void Validate_RejectsMissingRoomCollection() =>
        F.AssertIssue(F.Validate(new(null, F.Version, 1, null)), "REQUIRED", "groundFloorRooms");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_RejectsMissingRequiredRoom(bool optionalPresent) =>
        F.AssertIssue(F.Validate(F.WithRooms(optionalPresent ? [new("ROOM_B", 1)] : [])), "REQUIRED", "groundFloorRooms");

    [Fact]
    public void Validate_AllowsOmittedOptionalRoom() => F.AssertValid(F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1))));

    [Fact]
    public void Validate_RejectsNullRoomEntry() =>
        F.AssertIssue(F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1), null)), "REQUIRED", "groundFloorRooms[1]");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_RejectsBlankRoomType(string? code) =>
        F.AssertIssue(F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1), new(code, 1))), "REQUIRED", "groundFloorRooms[1]");

    [Theory]
    [InlineData("ROOM_UNKNOWN")]
    [InlineData("room_a")]
    public void Validate_RejectsUnknownRoomType(string code) =>
        F.AssertIssue(F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1), new(code, 1))), "UNKNOWN_OPTION", "groundFloorRooms[1]");

    [Fact]
    public void Validate_RejectsMissingRoomCount() =>
        F.AssertIssue(F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", null))), "REQUIRED", "groundFloorRooms[0].count");

    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(int.MaxValue, false)]
    public void Validate_AppliesInclusiveRequiredRoomBounds(int count, bool valid)
    {
        var result = F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", count)));
        if (valid) F.AssertValid(result);
        else F.AssertIssue(result, "OUT_OF_BOUNDS", "groundFloorRooms[0].count");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Validate_AppliesOptionalRoomZeroBoundary(int count, bool valid)
    {
        var result = F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1), new("ROOM_B", count)));
        if (valid) F.AssertValid(result);
        else F.AssertIssue(result, "OUT_OF_BOUNDS", "groundFloorRooms[1].count");
    }

    [Fact]
    public void Validate_RejectsDuplicateRoomTypes() =>
        F.AssertIssue(F.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1), new("ROOM_A", 2))), "DUPLICATE", "groundFloorRooms[1]");

    [Fact]
    public void Validate_AcceptsEmptyOptionalSelections() => F.AssertValid(F.Validate(F.Request()));

    [Fact]
    public void Validate_RejectsNullSpaceEntry() =>
        F.AssertIssue(F.Validate(F.WithSpaces([null])), "REQUIRED", "additionalSpaces[0]");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_RejectsBlankSpaceOption(string? code) =>
        F.AssertIssue(F.Validate(F.WithSpaces(new AdditionalSpaceInput(code, null))), "REQUIRED", "additionalSpaces[0]");

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("space_a")]
    public void Validate_RejectsUnknownSpace(string code) =>
        F.AssertIssue(F.Validate(F.WithSpaces(new AdditionalSpaceInput(code, null))), "UNKNOWN_OPTION", "additionalSpaces[0]");

    [Fact]
    public void Validate_RejectsDuplicateSpaces() =>
        F.AssertIssue(F.Validate(F.WithSpaces(new AdditionalSpaceInput("SPACE_A", null), new("SPACE_A", null))), "DUPLICATE", "additionalSpaces[1]");

    [Theory]
    [InlineData("")]
    [InlineData("text")]
    public void Validate_RejectsTextForNonTextOption(string text) =>
        F.AssertIssue(F.Validate(F.WithSpaces(new AdditionalSpaceInput("SPACE_A", text))), "TEXT_NOT_ALLOWED", "additionalSpaces[0].otherText");

    [Fact]
    public void Validate_AllowsMissingOptionalOtherText() =>
        F.AssertValid(F.Validate(F.WithSpaces(new AdditionalSpaceInput("SPACE_TEXT", null))));

    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Validate_AppliesInclusiveTextLimit(int length, bool valid)
    {
        var result = F.Validate(F.WithSpaces(new AdditionalSpaceInput("SPACE_TEXT", new string('x', length))));
        if (valid) F.AssertValid(result);
        else F.AssertIssue(result, "TEXT_TOO_LONG", "additionalSpaces[0].otherText");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("x", false)]
    public void Validate_AppliesZeroTextLimit(string text, bool valid)
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), F.Rooms(),
            [new("SPACE_TEXT", true, 0)], [], [], false);
        var result = RequirementContractValidator.Validate(F.WithSpaces(new AdditionalSpaceInput("SPACE_TEXT", text)), catalogue);
        if (valid) F.AssertValid(result);
        else F.AssertIssue(result, "TEXT_TOO_LONG", "additionalSpaces[0].otherText");
    }

    [Fact]
    public void Validate_RejectsNullRelationshipEntry() =>
        F.AssertIssue(F.Validate(F.WithRelationships([null])), "REQUIRED", "relationships[0]");

    [Theory]
    [InlineData(null, "ROOM_B", "NEAR")]
    [InlineData("", "ROOM_B", "NEAR")]
    [InlineData(" ", "ROOM_B", "NEAR")]
    [InlineData("ROOM_A", null, "NEAR")]
    [InlineData("ROOM_A", "", "NEAR")]
    [InlineData("ROOM_A", " ", "NEAR")]
    [InlineData("ROOM_A", "ROOM_B", null)]
    [InlineData("ROOM_A", "ROOM_B", "")]
    [InlineData("ROOM_A", "ROOM_B", " ")]
    public void Validate_RejectsIncompleteRelationship(string? source, string? target, string? kind) =>
        F.AssertIssue(F.Validate(F.WithRelationships(new RoomRelationshipInput(source, target, kind))), "REQUIRED", "relationships[0]");

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("near")]
    public void Validate_RejectsUnknownRelationshipKind(string kind) =>
        F.AssertIssue(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_B", kind))), "UNKNOWN_OPTION", "relationships[0]");

    [Theory]
    [InlineData("ROOM_A", "ROOM_B")]
    [InlineData("ROOM_B", "ROOM_A")]
    [InlineData("UNKNOWN", "ROOM_A")]
    [InlineData("ROOM_A", "UNKNOWN")]
    public void Validate_RejectsUnselectedRelationshipReference(string source, string target) =>
        F.AssertIssue(F.Validate(new(null, F.Version, 1, [new("ROOM_A", 1)],
            relationships: [new(source, target, "NEAR")])), "INVALID_REFERENCE", "relationships[0]");

    [Fact]
    public void Validate_RejectsForbiddenSelfReference() =>
        F.AssertIssue(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_A", "NEAR"))), "INVALID_REFERENCE", "relationships[0]");

    [Fact]
    public void Validate_AcceptsPermittedSelfReference() =>
        F.AssertValid(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_A", "SELF_OK"))));

    [Theory]
    [InlineData("NEAR")]
    [InlineData("FLOWS_TO")]
    public void Validate_RejectsExactDuplicateRelationships(string kind) =>
        F.AssertIssue(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_B", kind), new("ROOM_A", "ROOM_B", kind))),
            "DUPLICATE", "relationships[1]");

    [Fact]
    public void Validate_RejectsReversedUndirectedDuplicate() =>
        F.AssertIssue(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_B", "NEAR"), new("ROOM_B", "ROOM_A", "NEAR"))),
            "DUPLICATE", "relationships[1]");

    [Fact]
    public void Validate_AllowsOppositeDirectedRelationships() =>
        F.AssertValid(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_B", "FLOWS_TO"), new("ROOM_B", "ROOM_A", "FLOWS_TO"))));

    [Fact]
    public void Validate_AllowsDistinctRelationshipKinds() =>
        F.AssertValid(F.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_B", "NEAR"), new("ROOM_A", "ROOM_B", "FLOWS_TO"))));

    [Fact]
    public void Validate_AcceptsAllowedDesignMethod() => F.AssertValid(F.Validate(F.Request(method: "METHOD_A")));

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("method_a")]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_RejectsUnknownOrBlankDesignMethod(string method) =>
        F.AssertIssue(F.Validate(F.Request(method: method)), "UNKNOWN_OPTION", "designMethod");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_RequiresMethodOnlyWhenConfiguredRequired(bool required)
    {
        var result = RequirementContractValidator.Validate(F.Request(), F.Catalogue(required: required));
        if (required) F.AssertIssue(result, "REQUIRED", "designMethod");
        else F.AssertValid(result);
    }

    [Fact]
    public void Validate_CollectsIndependentInputErrors()
    {
        var request = new SaveRequirementsRequest(null, "wrong", null, F.Request().GroundFloorRooms, [new("UNKNOWN", null)]);
        var result = F.Validate(request);
        F.AssertIssue(result, "CATALOGUE_VERSION_MISMATCH", "catalogueVersion");
        F.AssertIssue(result, "REQUIRED", "floorCount");
        F.AssertIssue(result, "UNKNOWN_OPTION", "additionalSpaces[0]");
        Assert.Equal(3, result.Issues.Count);
    }
}
