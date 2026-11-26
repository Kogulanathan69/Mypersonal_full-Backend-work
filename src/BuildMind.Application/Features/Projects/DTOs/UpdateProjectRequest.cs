namespace BuildMind.Application.Features.Projects.DTOs;

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }

    public string? ProjectType { get; init; }

    public string? LocationReference { get; init; }

    public required long Revision { get; init; }
}
