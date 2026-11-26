using System.Text.Json.Serialization;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.API.Endpoints.Requirements;

public sealed class PutDesignMethodHttpRequest : PutRequirementsSectionHttpRequest
{
    [JsonRequired] public string? DesignMethod { get; init; }
    public override Dictionary<string, string[]> GetStructuralErrors() => MetadataErrors();
    public SaveDesignMethodRequest ToApplicationRequest()
    {
        if (GetStructuralErrors().Count != 0) throw new InvalidOperationException("Structural validation is required.");
        return new(ExpectedRevisionId!.Value, CatalogueVersion!, DesignMethod);
    }
}
