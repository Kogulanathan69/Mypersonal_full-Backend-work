using BuildMind.Application.Features.Requirements.Contracts;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class RequirementPolicyAvailabilityTests
{
    [Fact]
    public void Validate_ReturnsUnavailableForMissingCatalogue() =>
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(), null), "POLICY_UNAVAILABLE", "catalogue",
            RequirementValidationStatus.PolicyUnavailable);

    [Fact]
    public void Catalogue_DistinguishesNullAndEmptyMethods()
    {
        var unavailable = F.WithMethods(null);
        var empty = F.WithMethods([]);
        Assert.False(unavailable.DesignMethodPolicyAvailable);
        Assert.True(empty.DesignMethodPolicyAvailable);
        Assert.Empty(unavailable.DesignMethods);
        Assert.Empty(empty.DesignMethods);
    }

    [Fact]
    public void Validate_AllowsOmittedOptionalUnavailableMethod() =>
        F.AssertValid(RequirementContractValidator.Validate(F.Request(), F.WithMethods(null)));

    [Fact]
    public void Validate_ReturnsUnavailableForSelectedUnavailableMethod() =>
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(method: "METHOD_A"), F.WithMethods(null)),
            "POLICY_UNAVAILABLE", "designMethod", RequirementValidationStatus.PolicyUnavailable);

    [Theory]
    [InlineData(null)]
    [InlineData("METHOD_A")]
    public void Validate_ReturnsUnavailableForRequiredUnavailableMethod(string? method) =>
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(method: method), F.WithMethods(null, true)),
            "POLICY_UNAVAILABLE", "designMethod", RequirementValidationStatus.PolicyUnavailable);

    [Fact]
    public void Validate_AllowsOmittedMethodWithConfiguredEmptySet() =>
        F.AssertValid(RequirementContractValidator.Validate(F.Request(), F.WithMethods([])));

    [Fact]
    public void Validate_RejectsChoiceWithConfiguredEmptyMethodSet() =>
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(method: "METHOD_A"), F.WithMethods([])),
            "UNKNOWN_OPTION", "designMethod");

    [Fact]
    public void Validate_RequiresMethodWithRequiredEmptySet() =>
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(), F.WithMethods([], true)), "REQUIRED", "designMethod");

    [Fact]
    public void Validate_RejectsChoiceWithRequiredEmptySet() =>
        F.AssertIssue(RequirementContractValidator.Validate(F.Request(method: "METHOD_A"), F.WithMethods([], true)),
            "UNKNOWN_OPTION", "designMethod");

    [Fact]
    public void Validate_AllowsNoRoomsWithConfiguredEmptyRoomSet()
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), [], [], [], [], false);
        F.AssertValid(RequirementContractValidator.Validate(F.WithRooms(), catalogue));
    }

    [Fact]
    public void Validate_RejectsRoomWithConfiguredEmptyRoomSet()
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), [], [], [], [], false);
        F.AssertIssue(RequirementContractValidator.Validate(F.WithRooms(new RoomCountInput("ROOM_A", 1)), catalogue),
            "UNKNOWN_OPTION", "groundFloorRooms[0]");
    }

    [Fact]
    public void Validate_RejectsSelectionsWithEmptyOptionalPolicies()
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), F.Rooms(), [], [], [], false);
        F.AssertIssue(RequirementContractValidator.Validate(F.WithSpaces(new AdditionalSpaceInput("SPACE_A", null)), catalogue),
            "UNKNOWN_OPTION", "additionalSpaces[0]");
        F.AssertIssue(RequirementContractValidator.Validate(F.WithRelationships(new RoomRelationshipInput("ROOM_A", "ROOM_B", "NEAR")), catalogue),
            "UNKNOWN_OPTION", "relationships[0]");
    }
}
