using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Infrastructure.Persistence.Repositories.Survey;

/// <summary>
/// MySQL adapter for <see cref="ISurveyRepository"/> (Captain integration).
/// <para>
/// Insert uses the ratified protected-write path. A fresh owner-scoped authorization read supplies the server
/// version for the uploader. Inside the insert transaction, the project row is locked <c>FOR UPDATE</c> and the
/// owner and version are re-checked. A transfer or revocation between the service's ownership check and this write
/// therefore denies the insert (null), and the service then removes the stored blob.
/// </para>
/// Reads are scoped by project and survey. State updates are a compare-and-set on <c>state_version</c>.
/// </summary>
internal sealed class SurveyRepository(
    BuildMindDbContext context,
    TransactionRunner transactions,
    IProjectAuthorizationPort authorization) : ISurveyRepository
{
    public async Task<SurveyEntity?> GetAsync(Guid projectId, Guid surveyId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || surveyId == Guid.Empty)
            return null;

        var record = await context.Set<SurveyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectId == projectId && s.Id == surveyId, cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : ToDomain(record);
    }

    public async Task<SurveyEntity?> AddAsync(SurveyEntity survey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(survey);
        if (survey.Status != SurveyStatus.Queued || survey.StateVersion != 1)
            throw new ArgumentException("Only a newly queued survey can be added.", nameof(survey));

        var access = await authorization.AuthorizeOwnerAsync(survey.ProjectId, survey.UploadedBy, cancellationToken)
            .ConfigureAwait(false);
        if (access.Status != ProjectAuthorizationStatus.Authorized || access.Snapshot is not { } snapshot
            || snapshot.ProjectId != survey.ProjectId || snapshot.ActorId != survey.UploadedBy)
            return null;

        var record = ToRecord(survey);
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var allowed = await new ProjectMutationAuthorization(context)
                    .LockAndCheckAsync(survey.ProjectId, survey.UploadedBy, snapshot.Version.Value, token)
                    .ConfigureAwait(false);
                if (!allowed)
                    throw new SurveyWriteRejected();

                context.Add(record);
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return survey;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SurveyWriteRejected)
        {
            return null;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public async Task<bool> UpdateAsync(SurveyEntity survey, long expectedStateVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(survey);
        if (expectedStateVersion <= 0 || survey.StateVersion != expectedStateVersion + 1)
            throw new ArgumentException("The survey must advance exactly one state version.", nameof(survey));

        var updated = await context.Set<SurveyRecord>()
            .Where(s => s.Id == survey.Id && s.ProjectId == survey.ProjectId && s.StateVersion == expectedStateVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, survey.Status.ToString())
                .SetProperty(s => s.StateVersion, survey.StateVersion)
                .SetProperty(s => s.CompletedAtUtc, survey.CompletedAtUtc)
                .SetProperty(s => s.FailureCode, survey.FailureCode)
                .SetProperty(s => s.FailedAtUtc, survey.FailedAtUtc), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1;
    }

    internal static SurveyRecord ToRecord(SurveyEntity survey) => new()
    {
        Id = survey.Id,
        ProjectId = survey.ProjectId,
        UploadedBy = survey.UploadedBy,
        StorageId = survey.StorageId,
        OriginalFileName = survey.OriginalFileName,
        MediaType = survey.MediaType,
        ByteLength = survey.ByteLength,
        Sha256Hash = survey.Sha256Hash,
        UploadedAtUtc = survey.UploadedAtUtc,
        Status = survey.Status.ToString(),
        StateVersion = survey.StateVersion,
        CompletedAtUtc = survey.CompletedAtUtc,
        FailureCode = survey.FailureCode,
        FailedAtUtc = survey.FailedAtUtc,
    };

    internal static SurveyEntity ToDomain(SurveyRecord record)
    {
        if (!Enum.TryParse<SurveyStatus>(record.Status, ignoreCase: false, out var status) || !Enum.IsDefined(status))
            throw new InvalidDataException("The stored survey status is invalid.");
        return SurveyEntity.Rehydrate(record.Id, record.ProjectId, record.UploadedBy, record.StorageId,
            record.OriginalFileName, record.MediaType, record.ByteLength, record.Sha256Hash, record.UploadedAtUtc,
            status, record.StateVersion, record.CompletedAtUtc, record.FailureCode, record.FailedAtUtc);
    }

    private sealed class SurveyWriteRejected : Exception;
}
