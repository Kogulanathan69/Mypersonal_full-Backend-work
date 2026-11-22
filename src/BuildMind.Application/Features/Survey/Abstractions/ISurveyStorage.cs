namespace BuildMind.Application.Features.Survey.Abstractions;

/// <summary>
/// Private file storage boundary. Infrastructure implementation belongs to M4.
/// Storage identifiers must never expose public file URLs or physical paths.
/// </summary>
public interface ISurveyStorage
{
    Task<Guid> StoreAsync(
        Stream content,
        string mediaType,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid storageId,
        CancellationToken cancellationToken);
}
