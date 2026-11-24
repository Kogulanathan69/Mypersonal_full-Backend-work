using System.Security.Cryptography;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Planning;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Repositories.Survey;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Read-only access to the private survey blob written by <c>PrivateSurveyFileStorage</c> (<c>{storageId:N}.blob</c>).
/// The path is built only from the server GUID; the root and the file must not be reparse points (symlinks); the file
/// is read with a hard size cap and must match the recorded length and SHA-256.
/// </summary>
internal sealed class SurveyBlobReader(IConfiguration configuration)
{
    public sealed record BlobRead(byte[]? Content, string? FailureCode);

    public async Task<BlobRead> ReadAsync(Guid storageId, long expectedLength, string expectedSha256, CancellationToken token)
    {
        var rootPath = configuration[CaptainIntegrationInfrastructure.SurveyStorageDirectoryKey];
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            return new(null, "SURVEY_STORAGE_UNAVAILABLE");
        var root = new DirectoryInfo(rootPath);
        if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            return new(null, "SURVEY_STORAGE_UNAVAILABLE");
        var file = new FileInfo(Path.Combine(root.FullName, storageId.ToString("N") + ".blob"));
        if (!file.Exists)
            return new(null, "SURVEY_FILE_MISSING");
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null)
            return new(null, "SURVEY_FILE_REJECTED");
        if (expectedLength is <= 0 or > SurveyEntity.MaxFileSizeBytes || file.Length != expectedLength)
            return new(null, "SURVEY_FILE_SIZE_MISMATCH");
        var content = new byte[expectedLength];
        await using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            var read = 0;
            while (read < content.Length)
            {
                var count = await stream.ReadAsync(content.AsMemory(read), token).ConfigureAwait(false);
                if (count == 0) return new(null, "SURVEY_FILE_SIZE_MISMATCH");
                read += count;
            }
            if (stream.ReadByte() != -1) return new(null, "SURVEY_FILE_SIZE_MISMATCH");
        }
        return Convert.ToHexStringLower(SHA256.HashData(content)) == expectedSha256
            ? new(content, null)
            : new(null, "SURVEY_FILE_INTEGRITY_MISMATCH");
    }
}

/// <summary>
/// Durable work queue over <c>surveys</c>: atomic claims with a lease, bounded attempts, stale-claimer exclusion and
/// one-shot publication (extraction row + terminal state in one protected transaction). No transaction is held while
/// the Python service runs.
/// </summary>
internal sealed class SurveyWorkQueue(BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization)
{
    public sealed record Claim(SurveyEntity Survey, DateTimeOffset LeaseUntil, int Attempt);

    public Task<List<Guid>> FindClaimableAsync(DateTimeOffset now, int limit, CancellationToken token) =>
        context.Set<SurveyRecord>().AsNoTracking()
            .Where(s => s.Status == nameof(SurveyStatus.Queued)
                        || (s.Status == nameof(SurveyStatus.Processing) && s.ProcessingLeaseUntilUtc < now))
            .OrderBy(s => s.UploadedAtUtc).Select(s => s.Id).Take(limit).ToListAsync(token);

    /// <summary>Queued(1) → Processing(2), or reclaim an expired Processing lease. Null when another worker won.</summary>
    public async Task<Claim?> TryClaimAsync(Guid surveyId, DateTimeOffset now, TimeSpan lease, int maxAttempts, CancellationToken token)
    {
        // MySQL datetime(6) keeps microseconds; truncate so the claimer's lease token round-trips exactly.
        var raw = now + lease;
        var leaseUntil = raw.AddTicks(-(raw.Ticks % 10));
        var queued = await context.Set<SurveyRecord>()
            .Where(s => s.Id == surveyId && s.Status == nameof(SurveyStatus.Queued) && s.StateVersion == 1)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, nameof(SurveyStatus.Processing)).SetProperty(s => s.StateVersion, 2L)
                .SetProperty(s => s.ProcessingLeaseUntilUtc, leaseUntil).SetProperty(s => s.ProcessingAttempts, 1), token)
            .ConfigureAwait(false);
        if (queued == 0)
        {
            var reclaimed = await context.Set<SurveyRecord>()
                .Where(s => s.Id == surveyId && s.Status == nameof(SurveyStatus.Processing) && s.StateVersion == 2
                            && s.ProcessingLeaseUntilUtc < now && s.ProcessingAttempts < maxAttempts)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.ProcessingLeaseUntilUtc, leaseUntil)
                    .SetProperty(s => s.ProcessingAttempts, s => s.ProcessingAttempts + 1), token).ConfigureAwait(false);
            if (reclaimed == 0)
            {
                // Expired and out of attempts: fail it permanently (system transition, no extraction published).
                await context.Set<SurveyRecord>()
                    .Where(s => s.Id == surveyId && s.Status == nameof(SurveyStatus.Processing) && s.StateVersion == 2
                                && s.ProcessingLeaseUntilUtc < now && s.ProcessingAttempts >= maxAttempts)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, nameof(SurveyStatus.Failed)).SetProperty(s => s.StateVersion, 3L)
                        .SetProperty(s => s.FailureCode, "EXTRACTION_ABANDONED").SetProperty(s => s.FailedAtUtc, now)
                        .SetProperty(s => s.ProcessingLeaseUntilUtc, (DateTimeOffset?)null), token).ConfigureAwait(false);
                return null;
            }
        }
        var row = await context.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == surveyId, token).ConfigureAwait(false);
        return row.ProcessingLeaseUntilUtc == leaseUntil
            ? new Claim(SurveyRepository.ToDomain(row), leaseUntil, row.ProcessingAttempts)
            : null;
    }

    /// <summary>
    /// Publishes exactly once: under the project-row lock with the uploader's fresh authorization, inserts the extraction
    /// and moves Processing(2) → Completed/Failed(3) only while THIS claim's lease is still the recorded lease.
    /// </summary>
    public async Task<bool> TryPublishAsync(Claim claim, SurveyExtractionRecord? extraction, bool completed, string? failureCode,
        DateTimeOffset now, CancellationToken token)
    {
        var survey = claim.Survey;
        if (await ProtectedVersion.ReadAsync(authorization, survey.ProjectId, survey.UploadedBy, token).ConfigureAwait(false) is not { } version)
            return await FailAsync(claim, "PROJECT_ACCESS_REVOKED", now, token).ConfigureAwait(false);
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(survey.ProjectId, survey.UploadedBy, version, async inner =>
        {
            var moved = await context.Set<SurveyRecord>()
                .Where(s => s.Id == survey.Id && s.Status == nameof(SurveyStatus.Processing) && s.StateVersion == 2
                            && s.ProcessingLeaseUntilUtc == claim.LeaseUntil)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, completed ? nameof(SurveyStatus.Completed) : nameof(SurveyStatus.Failed))
                    .SetProperty(s => s.StateVersion, 3L)
                    .SetProperty(s => s.CompletedAtUtc, completed ? now : null)
                    .SetProperty(s => s.FailureCode, completed ? null : failureCode)
                    .SetProperty(s => s.FailedAtUtc, completed ? null : now)
                    .SetProperty(s => s.ProcessingLeaseUntilUtc, (DateTimeOffset?)null), inner).ConfigureAwait(false);
            if (moved != 1)
                return ProtectedWriteStatus.Conflict; // a newer claimer owns the survey, or it is already terminal
            if (extraction is not null)
            {
                context.Add(extraction);
                await context.SaveChangesAsync(inner).ConfigureAwait(false);
            }
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => true,
            ProtectedWriteStatus.Denied => await FailAsync(claim, "PROJECT_ACCESS_REVOKED", now, token).ConfigureAwait(false),
            _ => false,
        };
    }

    /// <summary>Extends the lease only while this claimer still owns it (lease-token CAS). Null when the claim was lost.</summary>
    public async Task<Claim?> TryRenewAsync(Claim claim, DateTimeOffset now, TimeSpan lease, CancellationToken token)
    {
        var raw = now + lease;
        var next = raw.AddTicks(-(raw.Ticks % 10));
        if (next <= claim.LeaseUntil) return claim;
        var renewed = await context.Set<SurveyRecord>()
            .Where(s => s.Id == claim.Survey.Id && s.Status == nameof(SurveyStatus.Processing) && s.StateVersion == 2
                        && s.ProcessingLeaseUntilUtc == claim.LeaseUntil)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ProcessingLeaseUntilUtc, next), token).ConfigureAwait(false);
        return renewed == 1 ? claim with { LeaseUntil = next } : null;
    }

    public async Task<bool> FailAsync(Claim claim, string code, DateTimeOffset now, CancellationToken token) =>
        await context.Set<SurveyRecord>()
            .Where(s => s.Id == claim.Survey.Id && s.Status == nameof(SurveyStatus.Processing) && s.StateVersion == 2
                        && s.ProcessingLeaseUntilUtc == claim.LeaseUntil)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, nameof(SurveyStatus.Failed)).SetProperty(s => s.StateVersion, 3L)
                .SetProperty(s => s.FailureCode, code).SetProperty(s => s.FailedAtUtc, now)
                .SetProperty(s => s.ProcessingLeaseUntilUtc, (DateTimeOffset?)null), token).ConfigureAwait(false) == 1;
}

/// <summary>Processes one claimed survey (scoped). Separated from the hosted loop so it is directly testable.</summary>
internal sealed class SurveyExtractionProcessor(
    SurveyWorkQueue queue, SurveyBlobReader blobs, ISurveyExtractionClient extraction, IProjectAuthorizationPort authorization,
    TimeProvider time, ILogger<SurveyExtractionProcessor> logger)
{
    public async Task<string> ProcessAsync(Guid surveyId, SurveyWorkerOptions options, CancellationToken token)
    {
        var clock = new MicrosecondTimeProvider(time);
        var claim = await queue.TryClaimAsync(surveyId, clock.GetUtcNow(), options.Lease, options.MaxAttempts, token).ConfigureAwait(false);
        if (claim is null) return "NOT_CLAIMED";
        var survey = claim.Survey;
        // Background work never bypasses project authorization: the uploader must still own the project.
        if (await ProtectedVersion.ReadAsync(authorization, survey.ProjectId, survey.UploadedBy, token).ConfigureAwait(false) is null)
            return await queue.FailAsync(claim, "PROJECT_ACCESS_REVOKED", clock.GetUtcNow(), token).ConfigureAwait(false) ? "FAILED" : "LOST";

        var blob = await blobs.ReadAsync(survey.StorageId, survey.ByteLength, survey.Sha256Hash, token).ConfigureAwait(false);
        if (blob.Content is null)
        {
            logger.LogWarning("Survey extraction failed closed: {Code} (survey {SurveyId})", blob.FailureCode, survey.Id);
            return await queue.FailAsync(claim, blob.FailureCode!, clock.GetUtcNow(), token).ConfigureAwait(false) ? "FAILED" : "LOST";
        }

        SurveyExtractionOutput output;
        var extractionId = Guid.NewGuid();
        using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            bounded.CancelAfter(options.ProcessingTimeout);
            // Lease heartbeat: renews this claim's lease (CAS on its own lease token) every third of the lease while
            // Python runs, so a healthy long extraction is never reclaimed. If the lease is lost, processing stops.
            var lost = false;
            using var heartbeatStop = new CancellationTokenSource();
            var heartbeat = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(options.Lease / 3, heartbeatStop.Token).ConfigureAwait(false);
                        if (await queue.TryRenewAsync(claim, clock.GetUtcNow(), options.Lease, heartbeatStop.Token).ConfigureAwait(false)
                            is { } renewed)
                            claim = renewed;
                        else
                        {
                            lost = true;
                            await bounded.CancelAsync().ConfigureAwait(false);
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, CancellationToken.None);
            try
            {
                try
                {
                    output = await extraction.ExtractAsync(new SurveyExtractionInput(extractionId, survey.StorageId, survey.MediaType,
                        blob.Content, survey.Sha256Hash), bounded.Token).ConfigureAwait(false);
                }
                finally
                {
                    await heartbeatStop.CancelAsync().ConfigureAwait(false);
                    await heartbeat.ConfigureAwait(false); // the DbContext is never used concurrently
                }
                if (lost) return "LOST";
            }
            catch (OperationCanceledException) when (lost)
            {
                logger.LogWarning("Survey extraction lease lost; result discarded (survey {SurveyId})", survey.Id);
                return "LOST";
            }
            catch (DependencyFailureException failure) when (failure.Kind == DependencyFailureKind.MalformedResponse)
            {
                return await queue.FailAsync(claim, "EXTRACTION_RESPONSE_INVALID", clock.GetUtcNow(), token).ConfigureAwait(false) ? "FAILED" : "LOST";
            }
            catch (Exception exception) when (exception is DependencyFailureException
                                                  || (exception is OperationCanceledException && !token.IsCancellationRequested))
            {
                // Dependency unavailable or timed out: leave the claim; it is retried after the lease expires (bounded).
                logger.LogWarning("Survey extraction deferred: dependency unavailable (survey {SurveyId}, attempt {Attempt})", survey.Id, claim.Attempt);
                return "DEFERRED";
            }
        }

        var record = new SurveyExtractionRecord
        {
            ExtractionId = extractionId, SurveyId = survey.Id, ProjectId = survey.ProjectId, Outcome = output.Outcome,
            Method = output.Method, ResultJson = output.ExtractionJson, CreatedAtUtc = clock.GetUtcNow(),
        };
        // Processing completion is separate from extraction accuracy: NEEDS_REVIEW completes processing; FAILED fails it.
        var completed = output.Outcome != "FAILED";
        var published = await queue.TryPublishAsync(claim, record, completed, completed ? null : "EXTRACTION_FAILED",
            clock.GetUtcNow(), token).ConfigureAwait(false);
        return published ? output.Outcome : "LOST";
    }
}

public sealed record SurveyWorkerOptions(TimeSpan PollInterval, int Concurrency, TimeSpan Lease, int MaxAttempts, TimeSpan ProcessingTimeout)
{
    public static SurveyWorkerOptions From(IConfiguration configuration)
    {
        int Read(string key, int fallback, int min, int max) =>
            int.TryParse(configuration[$"CaptainIntegration:SurveyWorker:{key}"], out var value) && value >= min && value <= max ? value : fallback;
        return new(TimeSpan.FromSeconds(Read("PollSeconds", 5, 1, 300)), Read("Concurrency", 2, 1, 8),
            TimeSpan.FromSeconds(Read("LeaseSeconds", 180, 30, 3600)), Read("MaxAttempts", 3, 1, 10),
            TimeSpan.FromSeconds(Read("ProcessingTimeoutSeconds", 150, 10, 1800)));
    }
}


internal sealed class MySqlSurveyExtractionReader(BuildMindDbContext context) : ISurveyExtractionReader
{
    public async Task<SurveyExtractionView?> GetAsync(Guid projectId, Guid surveyId, CancellationToken token)
    {
        var row = await context.Set<SurveyExtractionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(e => e.ProjectId == projectId && e.SurveyId == surveyId, token).ConfigureAwait(false);
        return row is null ? null : new SurveyExtractionView(row.ExtractionId, row.SurveyId, row.Outcome, row.Method, row.ResultJson, row.CreatedAtUtc);
    }
}

/// <summary>Scoped facade for the hosted loop (which lives in the API host): find claimable surveys, process one.</summary>
internal sealed class SurveyExtractionWork(SurveyWorkQueue queue, SurveyExtractionProcessor processor, IConfiguration configuration,
    TimeProvider time, SurveyStorageReconciler reconciler) : ISurveyExtractionWork
{
    public async Task<string> ReconcileStorageAsync(CancellationToken token)
    {
        var r = await reconciler.ReconcileAsync(SurveyStorageReconciler.DefaultGrace, 5000, token).ConfigureAwait(false);
        return $"scanned={r.Scanned} referenced={r.Referenced} recent={r.TooRecent} deleted={r.Deleted} unparsable={r.UnparsableNames}";
    }

    public async Task<IReadOnlyList<Guid>> FindClaimableAsync(int limit, CancellationToken token) =>
        await queue.FindClaimableAsync(time.GetUtcNow(), limit, token).ConfigureAwait(false);

    public Task<string> ProcessAsync(Guid surveyId, CancellationToken token) =>
        processor.ProcessAsync(surveyId, SurveyWorkerOptions.From(configuration), token);
}
