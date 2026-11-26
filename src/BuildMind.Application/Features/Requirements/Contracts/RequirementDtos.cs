using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Requirements.Contracts;

// Transport names and room-type relationship references remain M1/M3/M6 review candidates.
// Nullable inputs preserve missing values for validation instead of defaulting a count to zero.
public sealed record RoomCountInput(string? RoomType, int? Count);
public sealed record AdditionalSpaceInput(string? Option, string? OtherText);
public sealed record RoomRelationshipInput(string? SourceRoomType, string? TargetRoomType, string? Kind);

public sealed class SaveRequirementsRequest
{
    /// <summary>Null means create only if no current revision exists; empty GUID is invalid.</summary>
    public Guid? ExpectedRevisionId { get; }
    public string? CatalogueVersion { get; }
    public int? FloorCount { get; }
    public IReadOnlyList<RoomCountInput?>? GroundFloorRooms { get; }
    public IReadOnlyList<AdditionalSpaceInput?> AdditionalSpaces { get; }
    public IReadOnlyList<RoomRelationshipInput?> Relationships { get; }
    public string? DesignMethod { get; }

    public SaveRequirementsRequest(
        Guid? expectedRevisionId,
        string? catalogueVersion,
        int? floorCount,
        IReadOnlyList<RoomCountInput?>? groundFloorRooms,
        IReadOnlyList<AdditionalSpaceInput?>? additionalSpaces = null,
        IReadOnlyList<RoomRelationshipInput?>? relationships = null,
        string? designMethod = null)
    {
        ExpectedRevisionId = expectedRevisionId;
        CatalogueVersion = catalogueVersion;
        FloorCount = floorCount;
        GroundFloorRooms = groundFloorRooms is null ? null : Array.AsReadOnly(groundFloorRooms.ToArray());
        AdditionalSpaces = Array.AsReadOnly((additionalSpaces ?? []).ToArray());
        Relationships = Array.AsReadOnly((relationships ?? []).ToArray());
        DesignMethod = designMethod;
    }
}

/// <summary>A separate read contract; no EF entity is exposed.</summary>
public sealed class RequirementRevisionDto
{
    public Guid RevisionId { get; }
    public Guid ProjectId { get; }
    public long RevisionNumber { get; }
    public string SchemaVersion { get; }
    public string CatalogueVersion { get; }
    public int FloorCount { get; }
    public IReadOnlyList<RoomCountInput> GroundFloorRooms { get; }
    public IReadOnlyList<AdditionalSpaceInput> AdditionalSpaces { get; }
    public IReadOnlyList<RoomRelationshipInput> Relationships { get; }
    public string? DesignMethod { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public RequirementRevisionDto(RequirementRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        RevisionId = revision.Id;
        ProjectId = revision.ProjectId;
        RevisionNumber = revision.RevisionNumber;
        SchemaVersion = revision.SchemaVersion;
        CatalogueVersion = revision.CatalogueVersion;
        FloorCount = revision.Content.FloorCount;
        GroundFloorRooms = Array.AsReadOnly(revision.Content.GroundFloorRooms
            .Select(room => new RoomCountInput(room.RoomType, room.Count)).ToArray());
        AdditionalSpaces = Array.AsReadOnly(revision.Content.AdditionalSpaces
            .Select(space => new AdditionalSpaceInput(space.Option, space.OtherText)).ToArray());
        Relationships = Array.AsReadOnly(revision.Content.Relationships
            .Select(item => new RoomRelationshipInput(item.SourceRoomType, item.TargetRoomType, item.Kind)).ToArray());
        DesignMethod = revision.Content.DesignMethod;
        CreatedAtUtc = revision.CreatedAtUtc;
    }
}

public enum RequirementValidationStatus { Valid, Invalid, PolicyUnavailable }

public sealed record RequirementValidationIssue(string Code, string Field, string Message);

public sealed class RequirementValidationResult
{
    public RequirementValidationStatus Status { get; }
    public IReadOnlyList<RequirementValidationIssue> Issues { get; }
    public RequirementContent? Content { get; }
    public string? SchemaVersion { get; }
    public string? CatalogueVersion { get; }

    internal RequirementValidationResult(
        RequirementValidationStatus status,
        IEnumerable<RequirementValidationIssue> issues,
        RequirementContent? content = null,
        string? schemaVersion = null,
        string? catalogueVersion = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(issues);
        var issueArray = issues.ToArray();
        if (issueArray.Any(issue => issue is null))
            throw new ArgumentException("Issues cannot contain null entries.", nameof(issues));
        if (status == RequirementValidationStatus.Valid)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
            ArgumentException.ThrowIfNullOrWhiteSpace(catalogueVersion);
            if (issueArray.Length != 0) throw new ArgumentException("Successful validation cannot contain issues.", nameof(issues));
        }
        else if (content is not null || schemaVersion is not null || catalogueVersion is not null || issueArray.Length == 0)
            throw new ArgumentException("Failed validation requires issues and cannot expose a validated snapshot.");
        Status = status;
        Issues = Array.AsReadOnly(issueArray);
        Content = content;
        SchemaVersion = schemaVersion;
        CatalogueVersion = catalogueVersion;
    }

    /// <summary>Creates a new revision only from this successful immutable policy validation.</summary>
    public RequirementRevision CreateRevision(Guid id, Guid projectId, long revisionNumber, DateTimeOffset createdAtUtc)
    {
        if (Status != RequirementValidationStatus.Valid)
            throw new InvalidOperationException("Successful policy validation is required before creating a revision.");
        return new RequirementRevision(id, projectId, revisionNumber, SchemaVersion!, CatalogueVersion!, Content!, createdAtUtc);
    }
}
