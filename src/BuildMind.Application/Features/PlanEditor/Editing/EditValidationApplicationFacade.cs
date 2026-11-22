using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;

namespace BuildMind.Application.Features.PlanEditor.Editing;

public interface IEditValidationApplicationFacade
{
    Task<CandidatePreviewResult> ValidateAsync(
        CreateCandidatePreviewCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Unregistered application boundary that supplies the trusted current actor to the existing candidate-preview
/// orchestration. It deliberately defines no HTTP contract and does not change whether that orchestration persists
/// a candidate; those production integration decisions remain outside this facade.
/// </summary>
public sealed class EditValidationApplicationFacade(
    ICurrentUser currentUser,
    CandidatePreviewService candidatePreviews) : IEditValidationApplicationFacade
{
    private readonly ICurrentUser currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly CandidatePreviewService candidatePreviews =
        candidatePreviews ?? throw new ArgumentNullException(nameof(candidatePreviews));

    public async Task<CandidatePreviewResult> ValidateAsync(
        CreateCandidatePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);

        Guid actorId;
        try
        {
            actorId = currentUser.GetRequiredUserId();
        }
        catch (UnauthenticatedAccessException)
        {
            return new CandidatePreviewResult(CandidatePreviewResultStatus.Unauthorized);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await candidatePreviews.PreviewAsync(command, actorId, cancellationToken).ConfigureAwait(false);
    }
}
