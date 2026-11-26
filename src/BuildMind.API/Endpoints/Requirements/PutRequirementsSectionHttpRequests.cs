using System.Text.Json.Serialization;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.API.Endpoints.Requirements;

/// <summary>Presence-aware section metadata; no client owner or core fields are mapped.</summary>
public abstract class PutRequirementsSectionHttpRequest
{
    [JsonRequired] public Guid? ExpectedRevisionId { get; init; }
    [JsonRequired] public string? CatalogueVersion { get; init; }

    public abstract Dictionary<string, string[]> GetStructuralErrors();

    protected Dictionary<string, string[]> MetadataErrors()
    {
        var errors = new Dictionary<string, string[]>();
        if (ExpectedRevisionId is null || ExpectedRevisionId == Guid.Empty)
            errors["expectedRevisionId"] = ["A nonempty existing revision identity is required."];
        if (string.IsNullOrWhiteSpace(CatalogueVersion))
            errors["catalogueVersion"] = ["An explicitly supplied catalogue version is required."];
        return errors;
    }
}

public sealed class PutAdditionalSpacesHttpRequest : PutRequirementsSectionHttpRequest
{
    [JsonRequired] public SpaceHttpInput?[]? AdditionalSpaces { get; init; }

    public override Dictionary<string, string[]> GetStructuralErrors()
    {
        var errors = MetadataErrors();
        if (AdditionalSpaces is null) errors["additionalSpaces"] = ["An explicit collection is required."];
        else for (var i = 0; i < AdditionalSpaces.Length; i++)
            if (AdditionalSpaces[i]?.IsStructurallyValid() != true)
                errors[$"additionalSpaces[{i}]"] = ["A structurally valid space selection is required."];
        return errors;
    }

    public SaveAdditionalSpacesRequest ToApplicationRequest()
    {
        if (GetStructuralErrors().Count != 0) throw new InvalidOperationException("Structural validation is required.");
        return new(ExpectedRevisionId!.Value, CatalogueVersion!,
            AdditionalSpaces!.Select(item => (AdditionalSpaceInput?)new(item!.Option, item.OtherText)));
    }
}

public sealed class PutRoomRelationshipsHttpRequest : PutRequirementsSectionHttpRequest
{
    [JsonRequired] public RelationshipHttpInput?[]? Relationships { get; init; }

    public override Dictionary<string, string[]> GetStructuralErrors()
    {
        var errors = MetadataErrors();
        if (Relationships is null) errors["relationships"] = ["An explicit collection is required."];
        else for (var i = 0; i < Relationships.Length; i++)
            if (Relationships[i]?.IsStructurallyValid() != true)
                errors[$"relationships[{i}]"] = ["A structurally valid relationship is required."];
        return errors;
    }

    public SaveRoomRelationshipsRequest ToApplicationRequest()
    {
        if (GetStructuralErrors().Count != 0) throw new InvalidOperationException("Structural validation is required.");
        return new(ExpectedRevisionId!.Value, CatalogueVersion!, Relationships!.Select(item =>
            (RoomRelationshipInput?)new(item!.SourceRoomType, item.TargetRoomType, item.Kind)));
    }
}
