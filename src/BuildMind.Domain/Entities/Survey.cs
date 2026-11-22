using BuildMind.Domain.ValueObjects;

namespace BuildMind.Domain.Entities;

/// <summary>
/// Private survey upload metadata. File bytes are stored outside the domain model.
/// </summary>
public sealed class Survey
{
    public const long MaxFileSizeBytes = 6 * 1024 * 1024;

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid UploadedBy { get; }
    public Guid StorageId { get; }
    public string OriginalFileName { get; }
    public string MediaType { get; }
    public long ByteLength { get; }
    public string Sha256Hash { get; }
    public DateTimeOffset UploadedAtUtc { get; }
    public SurveyStatus Status { get; }
    public long StateVersion { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public string? FailureCode { get; }
    public DateTimeOffset? FailedAtUtc { get; }

    private Survey(
        Guid id,
        Guid projectId,
        Guid uploadedBy,
        Guid storageId,
        string originalFileName,
        string mediaType,
        long byteLength,
        string sha256Hash,
        DateTimeOffset uploadedAtUtc,
        SurveyStatus status,
        long stateVersion,
        DateTimeOffset? completedAtUtc,
        string? failureCode,
        DateTimeOffset? failedAtUtc)
    {
        ReportStructure.RequireIdentity(id, nameof(id));
        ReportStructure.RequireIdentity(projectId, nameof(projectId));
        ReportStructure.RequireIdentity(uploadedBy, nameof(uploadedBy));
        ReportStructure.RequireIdentity(storageId, nameof(storageId));

        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        if (originalFileName.Length > 255)
            throw new ArgumentException("Filename is too long.", nameof(originalFileName));

        if (mediaType is not ("application/pdf" or "image/jpeg" or "image/png"))
            throw new ArgumentException("Unsupported survey media type.", nameof(mediaType));

        if (byteLength <= 0 || byteLength > MaxFileSizeBytes)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hash);
        if (sha256Hash.Length != 64 ||
            sha256Hash.AsSpan().ContainsAnyExcept("0123456789abcdef"))
            throw new ArgumentException("SHA-256 must contain 64 lowercase hexadecimal characters.", nameof(sha256Hash));

        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        if (stateVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(stateVersion));

        if (status == SurveyStatus.Queued &&
            (stateVersion != 1 || completedAtUtc is not null ||
             failureCode is not null || failedAtUtc is not null))
            throw new ArgumentException("Queued surveys require initial state.");

        if (status == SurveyStatus.Processing &&
            (stateVersion != 2 || completedAtUtc is not null ||
             failureCode is not null || failedAtUtc is not null))
            throw new ArgumentException("Processing surveys require processing state.");

        if (status == SurveyStatus.Completed &&
            (stateVersion != 3 || completedAtUtc is null ||
             failureCode is not null || failedAtUtc is not null))
            throw new ArgumentException("Completed surveys require a completion time.");

        if (status == SurveyStatus.Failed &&
            (stateVersion < 2 || failureCode is null ||
             !IsValidFailureCode(failureCode) || failedAtUtc is null ||
             completedAtUtc is not null))
            throw new ArgumentException("Failed surveys require a safe failure code and failure time.");

        var terminalTime = completedAtUtc ?? failedAtUtc;
        if (terminalTime is not null && terminalTime.Value < uploadedAtUtc)
            throw new ArgumentException("Terminal time cannot precede upload time.");

        Id = id;
        ProjectId = projectId;
        UploadedBy = uploadedBy;
        StorageId = storageId;
        OriginalFileName = originalFileName;
        MediaType = mediaType;
        ByteLength = byteLength;
        Sha256Hash = sha256Hash;
        UploadedAtUtc = uploadedAtUtc.ToUniversalTime();
        Status = status;
        StateVersion = stateVersion;
        CompletedAtUtc = completedAtUtc?.ToUniversalTime();
        FailureCode = failureCode;
        FailedAtUtc = failedAtUtc?.ToUniversalTime();
    }

    public static Survey CreateQueued(
        Guid id,
        Guid projectId,
        Guid uploadedBy,
        Guid storageId,
        string originalFileName,
        string mediaType,
        long byteLength,
        string sha256Hash,
        DateTimeOffset uploadedAtUtc) =>
        new(id, projectId, uploadedBy, storageId, originalFileName,
            mediaType, byteLength, sha256Hash, uploadedAtUtc,
            SurveyStatus.Queued, 1, null, null, null);

    public static Survey Rehydrate(
        Guid id,
        Guid projectId,
        Guid uploadedBy,
        Guid storageId,
        string originalFileName,
        string mediaType,
        long byteLength,
        string sha256Hash,
        DateTimeOffset uploadedAtUtc,
        SurveyStatus status,
        long stateVersion,
        DateTimeOffset? completedAtUtc = null,
        string? failureCode = null,
        DateTimeOffset? failedAtUtc = null) =>
        new(id, projectId, uploadedBy, storageId, originalFileName,
            mediaType, byteLength, sha256Hash, uploadedAtUtc,
            status, stateVersion, completedAtUtc, failureCode, failedAtUtc);

    public Survey BeginProcessing(long expectedStateVersion)
    {
        RequireQueued(expectedStateVersion);
        return Copy(SurveyStatus.Processing, StateVersion + 1, null, null, null);
    }

    public Survey Complete(long expectedStateVersion, DateTimeOffset completedAtUtc)
    {
        RequireProcessing(expectedStateVersion);
        return Copy(SurveyStatus.Completed, StateVersion + 1, completedAtUtc, null, null);
    }

    public Survey Fail(long expectedStateVersion, string safeFailureCode, DateTimeOffset failedAtUtc)
    {
        if (expectedStateVersion != StateVersion)
            throw new InvalidOperationException("Survey state version conflict.");

        if (Status is not (SurveyStatus.Queued or SurveyStatus.Processing))
            throw new InvalidOperationException("Survey cannot transition from its current state.");

        if (!IsValidFailureCode(safeFailureCode))
            throw new ArgumentException("Invalid safe failure code.", nameof(safeFailureCode));

        return Copy(SurveyStatus.Failed, StateVersion + 1, null, safeFailureCode, failedAtUtc);
    }

    private Survey Copy(
        SurveyStatus status,
        long stateVersion,
        DateTimeOffset? completedAtUtc,
        string? failureCode,
        DateTimeOffset? failedAtUtc) =>
        new(Id, ProjectId, UploadedBy, StorageId, OriginalFileName,
            MediaType, ByteLength, Sha256Hash, UploadedAtUtc,
            status, stateVersion, completedAtUtc, failureCode, failedAtUtc);

    private void RequireQueued(long expectedStateVersion)
    {
        if (expectedStateVersion != StateVersion || Status != SurveyStatus.Queued)
            throw new InvalidOperationException("Survey is not in the expected queued state.");
    }

    private void RequireProcessing(long expectedStateVersion)
    {
        if (expectedStateVersion != StateVersion || Status != SurveyStatus.Processing)
            throw new InvalidOperationException("Survey is not in the expected processing state.");
    }

    private static bool IsValidFailureCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 64)
            return false;

        return code.AsSpan().IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_") < 0;
    }
}
