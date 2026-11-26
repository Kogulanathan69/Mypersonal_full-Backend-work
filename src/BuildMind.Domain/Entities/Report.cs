using BuildMind.Domain.ValueObjects;

namespace BuildMind.Domain.Entities;

public enum ReportStatus { Pending, Ready, Failed }

/// <summary>Immutable report state. Lifecycle permission is not authenticated user authorization.</summary>
public sealed class Report
{
    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid RequestedBy { get; }
    public DateTimeOffset RequestedAtUtc { get; }
    public ReportSnapshot InputSnapshot { get; }
    public long StateVersion { get; }
    public ReportStatus Status { get; }
    public ReportArtifact? Artifact { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public string? FailureCode { get; }
    public DateTimeOffset? FailedAtUtc { get; }

    private Report(Guid id, Guid projectId, Guid requestedBy, DateTimeOffset requestedAtUtc,
        ReportSnapshot inputSnapshot, long stateVersion, ReportStatus status, ReportArtifact? artifact,
        DateTimeOffset? completedAtUtc, string? failureCode, DateTimeOffset? failedAtUtc)
    {
        ReportStructure.RequireIdentity(id, nameof(id));
        ReportStructure.RequireIdentity(projectId, nameof(projectId));
        ReportStructure.RequireIdentity(requestedBy, nameof(requestedBy));
        ArgumentNullException.ThrowIfNull(inputSnapshot);
        if (inputSnapshot.ProjectId != projectId)
            throw new ArgumentException("Report project must match its snapshot.", nameof(projectId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (stateVersion <= 0) throw new ArgumentOutOfRangeException(nameof(stateVersion));
        switch (status)
        {
            case ReportStatus.Pending:
                if (stateVersion != 1 || artifact is not null || completedAtUtc is not null
                    || failureCode is not null || failedAtUtc is not null)
                    throw new ArgumentException("Pending reports require initial state and no terminal metadata.");
                break;
            case ReportStatus.Ready:
                if (stateVersion != 2 || artifact is null || completedAtUtc is null
                    || failureCode is not null || failedAtUtc is not null)
                    throw new ArgumentException("Ready reports require an artifact and completion time only.");
                if (artifact.CreatedAtUtc > completedAtUtc.Value)
                    throw new ArgumentException("Artifact creation cannot follow report completion.");
                break;
            case ReportStatus.Failed:
                if (stateVersion != 2 || artifact is not null || completedAtUtc is not null
                    || !IsValidFailureCode(failureCode) || failedAtUtc is null)
                    throw new ArgumentException("Failed reports require a safe failure code and failure time only.");
                break;
        }
        var terminalTime = completedAtUtc ?? failedAtUtc;
        if (terminalTime is not null && (terminalTime.Value < requestedAtUtc || terminalTime.Value < inputSnapshot.CapturedAtUtc))
            throw new ArgumentException("Terminal time cannot precede the request or snapshot capture.");
        Id = id;
        ProjectId = projectId;
        RequestedBy = requestedBy;
        RequestedAtUtc = requestedAtUtc.ToUniversalTime();
        InputSnapshot = inputSnapshot;
        StateVersion = stateVersion;
        Status = status;
        Artifact = artifact;
        CompletedAtUtc = completedAtUtc?.ToUniversalTime();
        FailureCode = failureCode;
        FailedAtUtc = failedAtUtc?.ToUniversalTime();
    }

    public static Report CreatePending(Guid id, Guid projectId, Guid requestedBy,
        DateTimeOffset requestedAtUtc, ReportSnapshot inputSnapshot) =>
        new(id, projectId, requestedBy, requestedAtUtc, inputSnapshot, 1, ReportStatus.Pending, null, null, null, null);

    /// <summary>Validated structural rehydration, not evidence of a legitimate persisted lifecycle history.</summary>
    public static Report Rehydrate(Guid id, Guid projectId, Guid requestedBy, DateTimeOffset requestedAtUtc,
        ReportSnapshot inputSnapshot, long stateVersion, ReportStatus status, ReportArtifact? artifact = null,
        DateTimeOffset? completedAtUtc = null, string? failureCode = null, DateTimeOffset? failedAtUtc = null) =>
        new(id, projectId, requestedBy, requestedAtUtc, inputSnapshot, stateVersion, status,
            artifact, completedAtUtc, failureCode, failedAtUtc);

    public Report Complete(long expectedStateVersion, ReportArtifact artifact, DateTimeOffset completedAtUtc)
    {
        RequirePending(expectedStateVersion);
        ArgumentNullException.ThrowIfNull(artifact);
        return new(Id, ProjectId, RequestedBy, RequestedAtUtc, InputSnapshot, checked(StateVersion + 1),
            ReportStatus.Ready, artifact, completedAtUtc, null, null);
    }

    /// <param name="safeFailureCode">A non-sensitive Application code, never raw exception text.</param>
    public Report Fail(long expectedStateVersion, string safeFailureCode, DateTimeOffset failedAtUtc)
    {
        RequirePending(expectedStateVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureCode);
        return new(Id, ProjectId, RequestedBy, RequestedAtUtc, InputSnapshot, checked(StateVersion + 1),
            ReportStatus.Failed, null, null, safeFailureCode, failedAtUtc);
    }

    private static bool IsValidFailureCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 64) return false;
        foreach (var character in code)
        {
            if (!(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                return false;
        }
        return true;
    }

    private void RequirePending(long expectedStateVersion)
    {
        if (expectedStateVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedStateVersion));
        if (expectedStateVersion != StateVersion) throw new InvalidOperationException("Report state version conflict.");
        if (Status != ReportStatus.Pending) throw new InvalidOperationException("Terminal report transitions are not permitted.");
    }
}
