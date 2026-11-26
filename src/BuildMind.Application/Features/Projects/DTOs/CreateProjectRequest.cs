namespace BuildMind.Application.Features.Projects.DTOs;

public sealed class CreateProjectRequest
{
    public required string Name { get; init; }

    public required string ProjectType { get; init; }

    public string? LocationReference { get; init; }
}