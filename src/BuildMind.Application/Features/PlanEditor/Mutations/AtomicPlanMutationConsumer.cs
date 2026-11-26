using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Application.Features.PlanEditor.Mutations;

/// <summary>
/// Unregistered application-layer consumer for the database-free F02 atomic mutation port.
/// Preflight authorization is defense in depth only: the port remains responsible for authoritative
/// authorization-version validation inside its future transaction boundary.
/// </summary>
public sealed class AtomicPlanMutationConsumer(
    ICurrentUser currentUser,
    IProjectAuthorizationPort authorization,
    IAtomicPlanMutationPort mutations)
{
    private readonly ICurrentUser _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly IProjectAuthorizationPort _authorization =
        authorization ?? throw new ArgumentNullException(nameof(authorization));
    private readonly IAtomicPlanMutationPort _mutations =
        mutations ?? throw new ArgumentNullException(nameof(mutations));

    public async Task<CandidateCreateMutationResponse> CreateCandidateAsync(
        CandidateCreateMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = await PreflightAsync(
            request.Scope, request.CurrentAuthorization, cancellationToken).ConfigureAwait(false);
        if (failure is { } status)
            return CandidateCreateMutationResponse.Failed(new AtomicMutationResult(status));

        cancellationToken.ThrowIfCancellationRequested();
        var response = await _mutations.CreateCandidateAsync(request, cancellationToken).ConfigureAwait(false);
        return Normalize(request, response);
    }

    public async Task<CandidateApplyMutationResponse> ApplyCandidateAsync(
        CandidateApplyMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = await PreflightAsync(
            request.Scope, request.CurrentAuthorization, cancellationToken).ConfigureAwait(false);
        if (failure is { } status)
            return CandidateApplyMutationResponse.Failed(new AtomicMutationResult(status));

        cancellationToken.ThrowIfCancellationRequested();
        var response = await _mutations.ApplyCandidateAsync(request, cancellationToken).ConfigureAwait(false);
        return Normalize(request, response);
    }

    public async Task<CandidateStateMutationResponse> RejectCandidateAsync(
        CandidateRejectMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = await PreflightAsync(
            request.Scope, request.CurrentAuthorization, cancellationToken).ConfigureAwait(false);
        if (failure is { } status)
            return CandidateStateMutationResponse.Failed(new AtomicMutationResult(status));

        cancellationToken.ThrowIfCancellationRequested();
        var response = await _mutations.RejectCandidateAsync(request, cancellationToken).ConfigureAwait(false);
        return Normalize(request, response);
    }

    public async Task<CandidateStateMutationResponse> ExpireCandidateAsync(
        CandidateExpireMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = await PreflightAsync(
            request.Scope, request.CurrentAuthorization, cancellationToken).ConfigureAwait(false);
        if (failure is { } status)
            return CandidateStateMutationResponse.Failed(new AtomicMutationResult(status));

        cancellationToken.ThrowIfCancellationRequested();
        var response = await _mutations.ExpireCandidateAsUserAsync(request, cancellationToken).ConfigureAwait(false);
        return Normalize(request, response);
    }

    public async Task<PlanRevisionMutationResponse> AppendRevisionAsync(
        PlanRevisionAppendMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = await PreflightAsync(
            request.Scope, request.CurrentAuthorization, cancellationToken).ConfigureAwait(false);
        if (failure is { } status)
            return PlanRevisionMutationResponse.Failed(new AtomicMutationResult(status));

        cancellationToken.ThrowIfCancellationRequested();
        var response = await _mutations.AppendPlanRevisionAsync(request, cancellationToken).ConfigureAwait(false);
        return Normalize(request, response);
    }

    public async Task<FinalConfirmationMutationResponse> ConfirmFinalPlanAsync(
        FinalPlanConfirmationMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = await PreflightAsync(
            request.Scope, request.CurrentAuthorization, cancellationToken).ConfigureAwait(false);
        if (failure is { } status)
            return FinalConfirmationMutationResponse.Failed(new AtomicMutationResult(status));

        cancellationToken.ThrowIfCancellationRequested();
        var response = await _mutations.ConfirmFinalPlanAsync(request, cancellationToken).ConfigureAwait(false);
        return Normalize(request, response);
    }

    private async Task<AtomicMutationStatus?> PreflightAsync(
        AtomicMutationScope scope,
        AuthorizationBinding expectedAuthorization,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = _currentUser.GetRequiredUserId();

        if (actorId != scope.TrustedActorId ||
            actorId != expectedAuthorization.AuthenticatedActorId)
            return AtomicMutationStatus.AuthorizationDenied;

        var result = await _authorization.AuthorizeOwnerAsync(
            scope.ProjectId, actorId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null)
            return AtomicMutationStatus.InvalidDependencyResponse;
        if (result.Status == ProjectAuthorizationStatus.Denied)
            return result.Snapshot is null
                ? AtomicMutationStatus.AuthorizationDenied
                : AtomicMutationStatus.InvalidDependencyResponse;
        if (result.Status != ProjectAuthorizationStatus.Authorized || result.Snapshot is null)
            return AtomicMutationStatus.InvalidDependencyResponse;

        var snapshot = result.Snapshot;
        if (snapshot.ProjectId != scope.ProjectId || snapshot.ActorId != actorId)
            return AtomicMutationStatus.InvalidDependencyResponse;
        if (snapshot.Version != scope.AuthorizationVersion ||
            snapshot.Version != expectedAuthorization.Version)
            return AtomicMutationStatus.AuthorizationVersionMismatch;

        return AuthorizationBindingValidator.Validate(
                   expectedAuthorization, scope.ProjectId, actorId, snapshot.Version) ==
               AuthorizationBindingValidationStatus.Valid
            ? null
            : AtomicMutationStatus.InvalidDependencyResponse;
    }

    private static CandidateCreateMutationResponse Normalize(
        CandidateCreateMutationRequest request,
        CandidateCreateMutationResponse? response)
    {
        if (response is null)
            return InvalidCandidateCreateResponse();
        try
        {
            return response.Result.Status switch
            {
                AtomicMutationStatus.Succeeded when response.Candidate is not null =>
                    CandidateCreateMutationResponse.Succeeded(request, response.Result, response.Candidate),
                AtomicMutationStatus.MatchingDuplicate when response.Candidate is not null =>
                    CandidateCreateMutationResponse.MatchingDuplicate(request, response.Result, response.Candidate),
                AtomicMutationStatus.TransactionOutcomeUnknown =>
                    CandidateCreateMutationResponse.OutcomeUnknown(request, response.Result),
                AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate =>
                    InvalidCandidateCreateResponse(),
                _ => CandidateCreateMutationResponse.Failed(response.Result),
            };
        }
        catch (ArgumentException)
        {
            return InvalidCandidateCreateResponse();
        }
    }

    private static CandidateApplyMutationResponse Normalize(
        CandidateApplyMutationRequest request,
        CandidateApplyMutationResponse? response)
    {
        if (response is null)
            return InvalidCandidateApplyResponse();
        try
        {
            return response.Result.Status switch
            {
                AtomicMutationStatus.Succeeded when response.Candidate is not null && response.Revision is not null =>
                    CandidateApplyMutationResponse.Succeeded(
                        request, response.Result, response.Candidate, response.Revision),
                AtomicMutationStatus.MatchingDuplicate when response.Candidate is not null && response.Revision is not null =>
                    CandidateApplyMutationResponse.MatchingDuplicate(
                        request, response.Result, response.Candidate, response.Revision),
                AtomicMutationStatus.TransactionOutcomeUnknown =>
                    CandidateApplyMutationResponse.OutcomeUnknown(request, response.Result),
                AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate =>
                    InvalidCandidateApplyResponse(),
                _ => CandidateApplyMutationResponse.Failed(response.Result),
            };
        }
        catch (ArgumentException)
        {
            return InvalidCandidateApplyResponse();
        }
    }

    private static CandidateStateMutationResponse Normalize(
        CandidateRejectMutationRequest request,
        CandidateStateMutationResponse? response)
    {
        if (response is null)
            return InvalidCandidateStateResponse();
        try
        {
            return response.Result.Status switch
            {
                AtomicMutationStatus.Succeeded when response.Candidate is not null =>
                    CandidateStateMutationResponse.Succeeded(request, response.Result, response.Candidate),
                AtomicMutationStatus.MatchingDuplicate when response.Candidate is not null =>
                    CandidateStateMutationResponse.MatchingDuplicate(request, response.Result, response.Candidate),
                AtomicMutationStatus.TransactionOutcomeUnknown =>
                    CandidateStateMutationResponse.OutcomeUnknown(request, response.Result),
                AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate =>
                    InvalidCandidateStateResponse(),
                _ => CandidateStateMutationResponse.Failed(response.Result),
            };
        }
        catch (ArgumentException)
        {
            return InvalidCandidateStateResponse();
        }
    }

    private static CandidateStateMutationResponse Normalize(
        CandidateExpireMutationRequest request,
        CandidateStateMutationResponse? response)
    {
        if (response is null)
            return InvalidCandidateStateResponse();
        try
        {
            return response.Result.Status switch
            {
                AtomicMutationStatus.Succeeded when response.Candidate is not null =>
                    CandidateStateMutationResponse.Succeeded(request, response.Result, response.Candidate),
                AtomicMutationStatus.MatchingDuplicate when response.Candidate is not null =>
                    CandidateStateMutationResponse.MatchingDuplicate(request, response.Result, response.Candidate),
                AtomicMutationStatus.TransactionOutcomeUnknown =>
                    CandidateStateMutationResponse.OutcomeUnknown(request, response.Result),
                AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate =>
                    InvalidCandidateStateResponse(),
                _ => CandidateStateMutationResponse.Failed(response.Result),
            };
        }
        catch (ArgumentException)
        {
            return InvalidCandidateStateResponse();
        }
    }

    private static PlanRevisionMutationResponse Normalize(
        PlanRevisionAppendMutationRequest request,
        PlanRevisionMutationResponse? response)
    {
        if (response is null)
            return InvalidRevisionResponse();
        try
        {
            return response.Result.Status switch
            {
                AtomicMutationStatus.Succeeded when response.Revision is not null =>
                    PlanRevisionMutationResponse.Succeeded(request, response.Result, response.Revision),
                AtomicMutationStatus.MatchingDuplicate when response.Revision is not null =>
                    PlanRevisionMutationResponse.MatchingDuplicate(request, response.Result, response.Revision),
                AtomicMutationStatus.TransactionOutcomeUnknown =>
                    PlanRevisionMutationResponse.OutcomeUnknown(request, response.Result),
                AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate =>
                    InvalidRevisionResponse(),
                _ => PlanRevisionMutationResponse.Failed(response.Result),
            };
        }
        catch (ArgumentException)
        {
            return InvalidRevisionResponse();
        }
    }

    private static FinalConfirmationMutationResponse Normalize(
        FinalPlanConfirmationMutationRequest request,
        FinalConfirmationMutationResponse? response)
    {
        if (response is null)
            return InvalidConfirmationResponse();
        try
        {
            return response.Result.Status switch
            {
                AtomicMutationStatus.Succeeded when response.Confirmation is not null =>
                    FinalConfirmationMutationResponse.Succeeded(
                        request, response.Result, response.Confirmation),
                AtomicMutationStatus.MatchingDuplicate when response.Confirmation is not null =>
                    FinalConfirmationMutationResponse.MatchingDuplicate(
                        request, response.Result, response.Confirmation),
                AtomicMutationStatus.TransactionOutcomeUnknown =>
                    FinalConfirmationMutationResponse.OutcomeUnknown(request, response.Result),
                AtomicMutationStatus.Succeeded or AtomicMutationStatus.MatchingDuplicate =>
                    InvalidConfirmationResponse(),
                _ => FinalConfirmationMutationResponse.Failed(response.Result),
            };
        }
        catch (ArgumentException)
        {
            return InvalidConfirmationResponse();
        }
    }

    private static CandidateCreateMutationResponse InvalidCandidateCreateResponse() =>
        CandidateCreateMutationResponse.Failed(
            new AtomicMutationResult(AtomicMutationStatus.InvalidDependencyResponse));

    private static CandidateApplyMutationResponse InvalidCandidateApplyResponse() =>
        CandidateApplyMutationResponse.Failed(
            new AtomicMutationResult(AtomicMutationStatus.InvalidDependencyResponse));

    private static CandidateStateMutationResponse InvalidCandidateStateResponse() =>
        CandidateStateMutationResponse.Failed(
            new AtomicMutationResult(AtomicMutationStatus.InvalidDependencyResponse));

    private static PlanRevisionMutationResponse InvalidRevisionResponse() =>
        PlanRevisionMutationResponse.Failed(
            new AtomicMutationResult(AtomicMutationStatus.InvalidDependencyResponse));

    private static FinalConfirmationMutationResponse InvalidConfirmationResponse() =>
        FinalConfirmationMutationResponse.Failed(
            new AtomicMutationResult(AtomicMutationStatus.InvalidDependencyResponse));
}
