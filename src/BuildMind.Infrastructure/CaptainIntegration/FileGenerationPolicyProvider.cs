using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.Planning;
using Microsoft.Extensions.Configuration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Generation policy loaded from the operator file <see cref="FileKey"/>. No compiled-in values. A missing or invalid
/// file means unavailable (generation answers "policy unavailable"), never a permissive default. The policy label is
/// carried for traceability; synthetic dimensions/setbacks are development-only data, never legal minimums.
/// </summary>
internal sealed class FileGenerationPolicyProvider(IConfiguration configuration) : IGenerationPolicyProvider
{
    public const string FileKey = "CaptainIntegration:GenerationPolicyFile";
    private const long MaximumFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
    };

    public GenerationPolicy? Get()
    {
        var path = configuration[FileKey];
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaximumFileBytes)
                return null;
            using var stream = info.OpenRead();
            var document = JsonSerializer.Deserialize<PolicyDocument>(stream, Json);
            return document is null ? null : Validate(document);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                              or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static GenerationPolicy? Validate(PolicyDocument d)
    {
        if (string.IsNullOrWhiteSpace(d.Version) || string.IsNullOrWhiteSpace(d.Label) || d.RoomDimensions is null
            || d.RoomDimensions.Count is 0 or > 4 || string.IsNullOrWhiteSpace(d.EvidenceMode))
            return null;
        var supported = new[] { GenerationRoomTypes.Bedroom, GenerationRoomTypes.LivingRoom, GenerationRoomTypes.Kitchen, GenerationRoomTypes.Bathroom };
        foreach (var (type, size) in d.RoomDimensions)
        {
            if (!supported.Contains(type, StringComparer.Ordinal) || size is null
                || !(size.MinWidthM is > 0 and < 100) || !(size.MinDepthM is > 0 and < 100)
                || !double.IsFinite(size.MinWidthM) || !double.IsFinite(size.MinDepthM))
                return null;
        }
        if (d.EvidenceMode is not (PlanningSchemas.EvidenceModeNone or PlanningSchemas.EvidenceModeSynthetic or PlanningSchemas.EvidenceModeRag))
            return null;
        if (d.EvidenceMode == PlanningSchemas.EvidenceModeRag && (string.IsNullOrWhiteSpace(d.Jurisdiction) || d.Jurisdiction.Length > 200))
            return null;
        if (d.EvidenceMode == PlanningSchemas.EvidenceModeSynthetic
            && (d.SyntheticSetbackM is not (>= 0 and < 100) || string.IsNullOrWhiteSpace(d.SyntheticRuleSetVersion)))
            return null;
        return new GenerationPolicy(d.Version, d.Label, d.RoomDimensions.ToDictionary(p => p.Key,
                p => new RoomDimensionsDto { MinWidthM = p.Value!.MinWidthM, MinDepthM = p.Value.MinDepthM }, StringComparer.Ordinal),
            d.EvidenceMode, d.SyntheticSetbackM, d.SyntheticRuleSetVersion, d.Jurisdiction);
    }

    private sealed record PolicyDocument(
        string Version, string Label, Dictionary<string, Dimensions?>? RoomDimensions, string EvidenceMode,
        double? SyntheticSetbackM, string? SyntheticRuleSetVersion, string? Jurisdiction = null);

    private sealed record Dimensions(double MinWidthM, double MinDepthM);
}
