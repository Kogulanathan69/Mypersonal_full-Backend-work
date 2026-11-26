using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;

namespace BuildMind.Application.Features.PlanEditor.Editing;

public interface ITrustedCandidateActionFacade
{
    Task<CandidateActionResult> ApplyAsync(
        ApplyCandidateCommand command,
        CancellationToken cancellationToken = default);

    Task<CandidateActionResult> RejectAsync(
        CandidateActionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Unregistered application boundary for authenticated candidate actions.
/// The actor is resolved exclusively from the trusted current-user context.
/// This facade does not define HTTP contracts, persistence, or production DI.
/// </summary>
public sealed class TrustedCandidateActionFacade(
    ICurrentUser currentUser,
    CandidatePreviewService candidatePreviews) : ITrustedCandidateActionFacade
{
    private readonly ICurrentUser currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly CandidatePreviewService candidatePreviews =
        candidatePreviews ?? throw new ArgumentNullException(nameof(candidatePreviews));

    public async Task<CandidateActionResult> ApplyAsync(
        ApplyCandidateCommand command,
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
            return new CandidateActionResult(CandidateActionStatus.Unauthorized);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await candidatePreviews
            .ApplyAsync(command, actorId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CandidateActionResult> RejectAsync(
        CandidateActionCommand command,
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
            return new CandidateActionResult(CandidateActionStatus.Unauthorized);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await candidatePreviews
            .RejectAsync(command, actorId, cancellationToken)
            .ConfigureAwait(false);
    }
}
