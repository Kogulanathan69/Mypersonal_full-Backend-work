using System.Security.Cryptography;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Application.Features.Survey.Contracts;
using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Application.Features.Survey.Services;

public sealed class SurveyUploadService(
    ICurrentUser currentUser,
    IProjectOwnershipGate ownershipGate,
    ISurveyStorage storage,
    ISurveyRepository repository,
    SurveyUploadValidator validator,
    TimeProvider timeProvider)
{
    public async Task<UploadSurveyResult> UploadAsync(
        UploadSurveyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedAccessException();

        Guid userId = currentUser.GetRequiredUserId();

        if (userId == Guid.Empty || currentUser.UserId != userId)
            throw new UnauthenticatedAccessException();

        if (request.ProjectId == Guid.Empty)
            throw new ArgumentException("INVALID_PROJECT_ID");

        bool ownsProject = await ownershipGate.OwnsProjectAsync(
            request.ProjectId,
            userId,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!ownsProject)
            throw new KeyNotFoundException("PROJECT_NOT_FOUND");

        if (request.ByteLength <= 0 ||
            request.ByteLength > SurveyUploadValidator.MaxFileSizeBytes)
            throw new InvalidDataException("INVALID_FILE_SIZE");

        if (request.Content is null || !request.Content.CanRead)
            throw new InvalidDataException("INVALID_FILE_CONTENT");

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            int allowedRead = (int)Math.Min(
                chunk.Length,
                SurveyUploadValidator.MaxFileSizeBytes + 1 - totalBytes);

            if (allowedRead <= 0)
                throw new InvalidDataException("INVALID_FILE_SIZE");

            int bytesRead = await request.Content.ReadAsync(
                chunk.AsMemory(0, allowedRead),
                cancellationToken);

            if (bytesRead == 0)
                break;

            totalBytes += bytesRead;

            if (totalBytes > SurveyUploadValidator.MaxFileSizeBytes)
                throw new InvalidDataException("INVALID_FILE_SIZE");

            await buffer.WriteAsync(
                chunk.AsMemory(0, bytesRead),
                cancellationToken);
        }

        if (totalBytes != request.ByteLength)
            throw new InvalidDataException("FILE_SIZE_MISMATCH");

        buffer.Position = 0;

        string? validationError = await validator.ValidateAsync(
            request.OriginalFileName,
            request.MediaType,
            totalBytes,
            buffer,
            cancellationToken);

        if (validationError is not null)
            throw new InvalidDataException(validationError);

        buffer.Position = 0;

        string normalizedMediaType = request.MediaType.Trim().ToLowerInvariant();

        byte[] fileBytes = buffer.ToArray();
        string sha256Hash = Convert.ToHexString(
            SHA256.HashData(fileBytes)).ToLowerInvariant();

        buffer.Position = 0;

        Guid storageId = await storage.StoreAsync(
            buffer,
            normalizedMediaType,
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var survey = SurveyEntity.CreateQueued(
                Guid.NewGuid(),
                request.ProjectId,
                userId,
                storageId,
                request.OriginalFileName,
                normalizedMediaType,
                totalBytes,
                sha256Hash,
                timeProvider.GetUtcNow());

            SurveyEntity? savedSurvey = await repository.AddAsync(
                survey,
                cancellationToken);

            if (savedSurvey is null)
                throw new InvalidOperationException(
                    "SURVEY_PERSISTENCE_CONFLICT");

            return new UploadSurveyResult(
                savedSurvey.Id,
                savedSurvey.Status.ToString(),
                savedSurvey.UploadedAtUtc);
        }
        catch
        {
            try
            {
                await storage.DeleteAsync(
                    storageId,
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original upload or persistence exception.
                // Storage cleanup failure requires operational monitoring.
            }

            throw;
        }
    }
}
