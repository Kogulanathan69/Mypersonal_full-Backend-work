namespace BuildMind.Domain.Entities;

public sealed class Project
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public required Guid OwnerId { get; init; }

    public required string Name { get; init; }

    public string? ProjectType { get; set; }

    public string? LocationReference { get; set; }

    public string Status { get; private set; } = "Created";

    public long Revision { get; private set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; init; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static Project Restore(
        Guid id,
        Guid ownerId,
        string name,
        string? projectType,
        string? locationReference,
        string status,
        long revision,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? updatedAtUtc)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty)
            throw new ArgumentException("Invalid project identity.");

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        if (revision < 1)
            throw new ArgumentOutOfRangeException(nameof(revision));

        return new Project
        {
            Id = id,
            OwnerId = ownerId,
            Name = name,
            ProjectType = projectType,
            LocationReference = locationReference,
            Status = status,
            Revision = revision,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    public Project WithUpdatedDetails(
        string? name,
        string? projectType,
        string? locationReference,
        DateTimeOffset updatedAtUtc)
    {
        return Restore(
            Id,
            OwnerId,
            name?.Trim() ?? Name,
            projectType?.Trim() ?? ProjectType,
            locationReference ?? LocationReference,
            Status,
            checked(Revision + 1),
            CreatedAtUtc,
            updatedAtUtc);
    }
}