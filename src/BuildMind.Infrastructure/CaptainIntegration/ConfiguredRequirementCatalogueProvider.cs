using System.Text.Json;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using Microsoft.Extensions.Configuration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Captain local-integration provider for <see cref="IRequirementCatalogueProvider"/>. It loads one requirement
/// policy snapshot from the operator-supplied JSON file named by <see cref="FileKey"/>.
/// </summary>
/// <remarks>
/// There are no compiled-in business values. A missing, unreadable or invalid file means "unavailable" (null), which
/// the existing service maps to a dependency failure. It is never an empty, permissive policy. The file's
/// <c>version</c> is recorded on every saved revision, so a synthetic local catalogue stays identifiable as such.
/// </remarks>
internal sealed class ConfiguredRequirementCatalogueProvider(IConfiguration configuration) : IRequirementCatalogueProvider
{
    public const string FileKey = "CaptainIntegration:RequirementCatalogueFile";
    private const long MaximumFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<RequirementCatalogue?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty)
            return null;

        var path = configuration[FileKey];
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaximumFileBytes)
                return null;

            await using var stream = info.OpenRead();
            var document = await JsonSerializer
                .DeserializeAsync<CatalogueDocument>(stream, Json, cancellationToken)
                .ConfigureAwait(false);
            return document?.ToCatalogue();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal sealed record CatalogueDocument(
        string Version,
        string SchemaVersion,
        RequirementCountBounds FloorCountBounds,
        RoomCountPolicy[] Rooms,
        AdditionalSpacePolicy[] AdditionalSpaces,
        RoomRelationshipPolicy[] Relationships,
        string[]? DesignMethods,
        bool DesignMethodRequired)
    {
        public RequirementCatalogue ToCatalogue() => new(
            Version, SchemaVersion, FloorCountBounds,
            Rooms ?? throw new JsonException("rooms is required."),
            AdditionalSpaces ?? throw new JsonException("additionalSpaces is required."),
            Relationships ?? throw new JsonException("relationships is required."),
            DesignMethods, DesignMethodRequired);
    }
}
