namespace BuildMind.Application.Features.Dashboard.Contracts;

/// <summary>Provisional projection of an actual owner-scoped M1 project query, not a project entity.
/// Stage codes are supplied by the agreed M1 contract; no stage catalogue is defined by M5.</summary>
public sealed class DashboardProjectDto
{
    public Guid ProjectId { get; }
    public string Name { get; }
    public string StageCode { get; }

    public DashboardProjectDto(Guid projectId, string name, string stageCode)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageCode);
        ProjectId = projectId;
        Name = name;
        StageCode = stageCode;
    }
}

public sealed record DashboardStageCountDto(string StageCode, int Count);

/// <summary>Derives counts without inventing progress or duplicating project queries.
/// Caller must obtain the complete, authorized projection from M1; an unavailable query is not empty.</summary>
public sealed class DashboardSummaryDto
{
    public int TotalProjects => Projects.Count;
    public IReadOnlyList<DashboardProjectDto> Projects { get; }
    public IReadOnlyList<DashboardStageCountDto> StageCounts { get; }

    public DashboardSummaryDto(IEnumerable<DashboardProjectDto> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var rows = projects.ToArray();
        if (rows.Any(x => x is null) || rows.Select(x => x.ProjectId).Distinct().Count() != rows.Length)
            throw new ArgumentException("Project projection must contain unique, nonnull rows.", nameof(projects));
        Projects = Array.AsReadOnly(rows);
        StageCounts = Array.AsReadOnly(rows.GroupBy(x => x.StageCode, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new DashboardStageCountDto(x.Key, x.Count())).ToArray());
    }
}
