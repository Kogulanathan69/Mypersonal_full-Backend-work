using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.PlanEditor.Contracts;

public static class PlanEditorJson
{
    /// <summary>Strict options required for provisional M3 contract boundaries.</summary>
    public static JsonSerializerOptions StrictOptions { get; } = new()
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
