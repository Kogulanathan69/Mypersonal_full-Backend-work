using BuildMind.Application.Features.Planning;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using BuildMind.Infrastructure.Persistence.Transactions;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Fixes the uncertain-commit cleanup defect of the M2 upload path without changing it: when the metadata insert
/// throws, the commit outcome may be unknown (it can have succeeded), so an immediate blob delete could leave committed
/// metadata pointing at a missing file. Deletes requested by the upload path are therefore DEFERRED; the reconciler
/// below removes a blob only when MySQL authoritatively shows no survey row references it and the blob is older than a
/// grace period that exceeds any in-flight upload.
/// </summary>
internal sealed class DeferredDeleteSurveyStorage(ISurveyStorage inner, ILogger<DeferredDeleteSurveyStorage> logger) : ISurveyStorage
{
    public Task<Guid> StoreAsync(Stream content, string mediaType, CancellationToken cancellationToken) =>
        inner.StoreAsync(content, mediaType, cancellationToken);

    public Task DeleteAsync(Guid storageId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Survey blob delete deferred to reconciliation");
        return Task.CompletedTask;
    }
}

public sealed record SurveyStorageReconciliationReport(int Scanned, int Referenced, int TooRecent, int Deleted, int UnparsableNames);

/// <summary>Authoritative orphan cleanup: unreferenced (no surveys.storage_id row), older than the grace period.</summary>
internal sealed class SurveyStorageReconciler(BuildMindDbContext context, IConfiguration configuration, TimeProvider time)
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(30);

    public async Task<SurveyStorageReconciliationReport> ReconcileAsync(TimeSpan grace, int maxFiles, CancellationToken token)
    {
        if (grace < TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(grace), "Grace must exceed any in-flight upload.");
        var root = configuration[CaptainIntegrationInfrastructure.SurveyStorageDirectoryKey];
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return new(0, 0, 0, 0, 0);
        var directory = new DirectoryInfo(root);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return new(0, 0, 0, 0, 0);
        var cutoff = time.GetUtcNow().UtcDateTime - grace;
        int scanned = 0, referenced = 0, recent = 0, deleted = 0, unparsable = 0;
        var candidates = new Dictionary<Guid, FileInfo>();
        foreach (var file in directory.EnumerateFiles("*.blob").Take(maxFiles))
        {
            scanned++;
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out var id))
            {
                unparsable++;
                continue;
            }
            if (file.LastWriteTimeUtc > cutoff)
            {
                recent++;
                continue;
            }
            candidates[id] = file;
        }
        foreach (var chunk in candidates.Keys.Chunk(200))
        {
            var known = await context.Set<SurveyRecord>().AsNoTracking().Where(s => chunk.Contains(s.StorageId)).Select(s => s.StorageId)
                .ToListAsync(token).ConfigureAwait(false);
            referenced += known.Count;
            foreach (var orphan in chunk.Except(known))
            {
                try
                {
                    candidates[orphan].Delete();
                    deleted++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return new(scanned, referenced, recent, deleted, unparsable);
    }
}

internal sealed class MySqlSurveyReviewStore(BuildMindDbContext context, TransactionRunner transactions) : ISurveyReviewStore
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SurveyReviewRevision>> ListAsync(Guid projectId, Guid surveyId, CancellationToken token) =>
        (await context.Set<SurveyExtractionReviewRecord>().AsNoTracking().Where(r => r.ProjectId == projectId && r.SurveyId == surveyId)
            .OrderByDescending(r => r.Version).Take(100).ToListAsync(token).ConfigureAwait(false))
        .Select(r => new SurveyReviewRevision(r.ReviewId, r.ExtractionId, r.SurveyId, r.Version, r.ReviewedBy, r.CreatedAtUtc,
            System.Text.Json.JsonSerializer.Deserialize<ReviewedMeasurement[]>(r.DecisionsJson, Json) ?? [], r.Note)).ToArray();

    public Task<ProtectedWriteStatus> AppendAsync(SurveyReviewRevision revision, Guid projectId, long authorizationVersion, CancellationToken token) =>
        new ProtectedPlanningWriter(context, transactions).RunAsync(projectId, revision.ReviewedBy, authorizationVersion, async inner =>
        {
            var latest = await context.Set<SurveyExtractionReviewRecord>().Where(r => r.ExtractionId == revision.ExtractionId)
                .MaxAsync(r => (int?)r.Version, inner).ConfigureAwait(false) ?? 0;
            if (latest != revision.Version - 1) return ProtectedWriteStatus.Conflict;
            context.Add(new SurveyExtractionReviewRecord
            {
                ReviewId = revision.ReviewId, ExtractionId = revision.ExtractionId, SurveyId = revision.SurveyId, ProjectId = projectId,
                Version = revision.Version, ReviewedBy = revision.ReviewedBy, CreatedAtUtc = revision.CreatedAtUtc, Note = revision.Note,
                DecisionsJson = System.Text.Json.JsonSerializer.Serialize(revision.Measurements, Json),
            });
            try { await context.SaveChangesAsync(inner).ConfigureAwait(false); }
            catch (DbUpdateException) { return ProtectedWriteStatus.Conflict; } // unique (extraction, version)
            return ProtectedWriteStatus.Written;
        }, token);
}
