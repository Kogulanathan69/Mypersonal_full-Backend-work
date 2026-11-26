using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Application.Features.Survey.Abstractions;

public interface ISurveyRepository
{
    Task<SurveyEntity?> GetAsync(
        Guid projectId,
        Guid surveyId,
        CancellationToken cancellationToken);

    Task<SurveyEntity?> AddAsync(
        SurveyEntity survey,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        SurveyEntity survey,
        long expectedStateVersion,
        CancellationToken cancellationToken);
}
