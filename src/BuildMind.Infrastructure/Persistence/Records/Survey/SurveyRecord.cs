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
}
