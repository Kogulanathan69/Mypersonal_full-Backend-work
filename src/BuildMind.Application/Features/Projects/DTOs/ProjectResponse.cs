namespace BuildMind.Application.Features.Projects.DTOs;

public sealed class ProjectResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? ProjectType { get; init; }

    public string? LocationReference { get; init; }

    public required string Status { get; init; }

    public long Revision { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}