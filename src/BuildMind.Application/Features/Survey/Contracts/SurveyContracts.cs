namespace BuildMind.Application.Features.Survey.Contracts;

/// <summary>
/// Incoming survey upload data. The authenticated user ID must come
/// from the current-user context, never from the request body.
/// </summary>
public sealed record UploadSurveyRequest(
    Guid ProjectId,
    string OriginalFileName,
    string MediaType,
    long ByteLength,
    Stream Content);

/// <summary>
/// Result returned after a survey upload has been accepted.
/// </summary>
public sealed record UploadSurveyResult(
    Guid SurveyId,
    string Status,
    DateTimeOffset UploadedAtUtc);

/// <summary>
/// Survey processing status returned to the project owner.
/// </summary>
public sealed record SurveyStatusResponse(
    Guid SurveyId,
    string Status,
    long StateVersion,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode);
