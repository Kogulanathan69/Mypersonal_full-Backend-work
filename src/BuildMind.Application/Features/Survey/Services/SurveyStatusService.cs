using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Application.Features.Survey.Contracts;
using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Application.Features.Survey.Services;

public sealed class SurveyStatusService(
    ICurrentUser currentUser,
    IProjectOwnershipGate ownershipGate,
    ISurveyRepository repository)
{
    public async Task<SurveyStatusResponse?> GetAsync(
        Guid projectId,
        Guid surveyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedAccessException();

        Guid userId = currentUser.GetRequiredUserId();

        if (userId == Guid.Empty || currentUser.UserId != userId)
            throw new UnauthenticatedAccessException();

        if (projectId == Guid.Empty || surveyId == Guid.Empty)
            throw new ArgumentException("INVALID_SURVEY_ID");

        bool ownsProject = await ownershipGate.OwnsProjectAsync(
            projectId,
            userId,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!ownsProject)
            return null;

        SurveyEntity? survey = await repository.GetAsync(
            projectId,
            surveyId,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (survey is null)
            return null;

        return new SurveyStatusResponse(
            survey.Id,
            survey.Status.ToString(),
            survey.StateVersion,
            survey.UploadedAtUtc,
            survey.CompletedAtUtc,
            survey.FailureCode);
    }
}
