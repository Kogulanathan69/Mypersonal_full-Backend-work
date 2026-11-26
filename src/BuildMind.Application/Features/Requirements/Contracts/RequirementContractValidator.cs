using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Requirements.Contracts;

/// <summary>
/// Pure requirements validation against a supplied policy, not geometry, legal or readiness validation.
/// Matching is ordinal; room relationships reference room types present in the request.
/// </summary>
public static class RequirementContractValidator
{
    public static RequirementValidationResult Validate(SaveRequirementsRequest? request, RequirementCatalogue? catalogue)
    {
        if (catalogue is null)
            return new(RequirementValidationStatus.PolicyUnavailable,
                [new("POLICY_UNAVAILABLE", "catalogue", "Required approved requirements policy is unavailable.")]);

        if (!catalogue.DesignMethodPolicyAvailable && (catalogue.DesignMethodRequired || request?.DesignMethod is not null))
            return new(RequirementValidationStatus.PolicyUnavailable,
                [new("POLICY_UNAVAILABLE", "designMethod", "Approved design-method policy is unavailable.")]);

        var issues = new List<RequirementValidationIssue>();
        void Error(string code, string field, string message) => issues.Add(new(code, field, message));
        if (request is null)
            return new(RequirementValidationStatus.Invalid,
                [new("REQUIRED", "request", "Requirements input is required.")]);

        if (request.ExpectedRevisionId == Guid.Empty)
            Error("INVALID_REVISION", "expectedRevisionId", "Use a revision identity, or null for an initial save.");
        if (!string.Equals(request.CatalogueVersion, catalogue.Version, StringComparison.Ordinal))
            Error("CATALOGUE_VERSION_MISMATCH", "catalogueVersion", "Input must identify the supplied policy version.");
        if (request.FloorCount is not int floorCount)
            Error("REQUIRED", "floorCount", "Floor count is required.");
        else if (floorCount <= 0 || !Within(floorCount, catalogue.FloorCountBounds))
            Error("OUT_OF_BOUNDS", "floorCount", "Floor count is outside approved bounds.");

        var rooms = catalogue.Rooms.ToDictionary(item => item.RoomType, StringComparer.Ordinal);
        var selectedRooms = new HashSet<string>(StringComparer.Ordinal);
        if (request.GroundFloorRooms is null)
            Error("REQUIRED", "groundFloorRooms", "Ground-floor room counts are required.");
        else
        {
            for (var i = 0; i < request.GroundFloorRooms.Count; i++)
            {
                var item = request.GroundFloorRooms[i];
                var field = $"groundFloorRooms[{i}]";
                if (item is null || string.IsNullOrWhiteSpace(item.RoomType))
                {
                    Error("REQUIRED", field, "Room type and count are required.");
                    continue;
                }
                if (!selectedRooms.Add(item.RoomType)) Error("DUPLICATE", field, "Duplicate room type.");
                if (!rooms.TryGetValue(item.RoomType, out var policy))
                    Error("UNKNOWN_OPTION", field, "Room type is not in the approved catalogue.");
                if (item.Count is not int count)
                    Error("REQUIRED", field + ".count", "Room count is required.");
                else if (count < 0 || (policy is not null && !Within(count, policy.Bounds)))
                    Error("OUT_OF_BOUNDS", field + ".count", "Room count is outside approved bounds.");
            }
        }
        foreach (var policy in catalogue.Rooms.Where(item => item.Required))
            if (!selectedRooms.Contains(policy.RoomType))
                Error("REQUIRED", "groundFloorRooms", $"Required room type '{policy.RoomType}' is missing.");

        var spaces = catalogue.AdditionalSpaces.ToDictionary(item => item.Option, StringComparer.Ordinal);
        var selectedSpaces = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < request.AdditionalSpaces.Count; i++)
        {
            var item = request.AdditionalSpaces[i];
            var field = $"additionalSpaces[{i}]";
            if (item is null || string.IsNullOrWhiteSpace(item.Option))
            {
                Error("REQUIRED", field, "Space option is required.");
                continue;
            }
            if (!selectedSpaces.Add(item.Option)) Error("DUPLICATE", field, "Duplicate additional space.");
            if (!spaces.TryGetValue(item.Option, out var policy))
            {
                Error("UNKNOWN_OPTION", field, "Space option is not in the approved catalogue.");
                continue;
            }
            if (item.OtherText is not null)
            {
                if (!policy.AllowsOtherText)
                    Error("TEXT_NOT_ALLOWED", field + ".otherText", "This option does not allow other text.");
                else if (item.OtherText.Length > policy.OtherTextMaximumLength!.Value)
                    Error("TEXT_TOO_LONG", field + ".otherText", "Text exceeds the approved length limit.");
            }
        }

        var kinds = catalogue.Relationships.ToDictionary(item => item.Kind, StringComparer.Ordinal);
        var selectedRelationships = new HashSet<(string Kind, string Source, string Target)>();
        for (var i = 0; i < request.Relationships.Count; i++)
        {
            var item = request.Relationships[i];
            var field = $"relationships[{i}]";
            if (item is null || string.IsNullOrWhiteSpace(item.Kind)
                || string.IsNullOrWhiteSpace(item.SourceRoomType) || string.IsNullOrWhiteSpace(item.TargetRoomType))
            {
                Error("REQUIRED", field, "Relationship kind and both room references are required.");
                continue;
            }
            if (!kinds.TryGetValue(item.Kind, out var policy))
            {
                Error("UNKNOWN_OPTION", field, "Relationship kind is not in the approved catalogue.");
                continue;
            }
            if (!selectedRooms.Contains(item.SourceRoomType) || !selectedRooms.Contains(item.TargetRoomType))
                Error("INVALID_REFERENCE", field, "Relationships must reference selected room types.");
            if (!policy.AllowsSelfReference && item.SourceRoomType == item.TargetRoomType)
                Error("INVALID_REFERENCE", field, "Self-reference is not allowed by this policy.");
            var source = item.SourceRoomType;
            var target = item.TargetRoomType;
            if (!policy.Directed && StringComparer.Ordinal.Compare(source, target) > 0)
                (source, target) = (target, source);
            if (!selectedRelationships.Add((item.Kind, source, target)))
                Error("DUPLICATE", field, "Duplicate room relationship.");
        }

        if (request.DesignMethod is null)
        {
            if (catalogue.DesignMethodRequired) Error("REQUIRED", "designMethod", "Design method is required.");
        }
        else if (!catalogue.DesignMethods.Contains(request.DesignMethod, StringComparer.Ordinal))
            Error("UNKNOWN_OPTION", "designMethod", "Design method is not in the approved catalogue.");

        if (issues.Count > 0) return new(RequirementValidationStatus.Invalid, issues);
        var content = new RequirementContent(
            request.FloorCount!.Value,
            request.GroundFloorRooms!.Select(item => new RoomCount(item!.RoomType!, item.Count!.Value)),
            request.AdditionalSpaces.Select(item => new AdditionalSpaceSelection(item!.Option!, item.OtherText)),
            request.Relationships.Select(item => new RoomRelationshipPreference(
                item!.SourceRoomType!, item.TargetRoomType!, item.Kind!)),
            request.DesignMethod);
        return new(RequirementValidationStatus.Valid, issues, content, catalogue.SchemaVersion, catalogue.Version);
    }

    private static bool Within(int count, RequirementCountBounds bounds) => count >= bounds.Minimum && count <= bounds.Maximum;
}
