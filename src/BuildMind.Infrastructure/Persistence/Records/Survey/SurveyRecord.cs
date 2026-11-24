namespace BuildMind.Infrastructure.Persistence.Records.Survey;

/// <summary>Persistence shape of an uploaded survey (Captain integration, M2 domain type unchanged).</summary>
internal sealed class SurveyRecord
{
    public const int FileNameMaxLength = 255;
    public const int MediaTypeMaxLength = 32;
    public const int StatusMaxLength = 16;
    public const int FailureCodeMaxLength = 64;

    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid UploadedBy { get; init; }
    public Guid StorageId { get; init; }
    public required string OriginalFileName { get; init; }
    public required string MediaType { get; init; }
    public long ByteLength { get; init; }
    public required string Sha256Hash { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; }
    public required string Status { get; set; }
    public long StateVersion { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset? FailedAtUtc { get; set; }

    // Captain extraction worker lease (additive): a claimer owns Processing until the lease expires; a reclaim
    // increments the attempt counter and replaces the lease, so a stale claimer can no longer publish.
    public DateTimeOffset? ProcessingLeaseUntilUtc { get; set; }
    public int ProcessingAttempts { get; set; }
}

/// <summary>Extraction evidence for one survey (outcome is separate from processing state and from approval).</summary>
internal sealed class SurveyExtractionRecord
{
    public const int MaxResultBytes = 512 * 1024;

    public Guid ExtractionId { get; init; }
    public Guid SurveyId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Outcome { get; init; }
    public required string Method { get; init; }
    public required string ResultJson { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
