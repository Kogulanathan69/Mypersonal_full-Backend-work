using System.Text.Json;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.CaptainIntegration;

// ---- Response contracts (declared in OpenAPI through Produces<T>) -------------------------------------------------

public sealed record SurveySummaryDto(Guid SurveyId, string OriginalFileName, string MediaType, long ByteLength, string Sha256Hash,
    string Status, DateTimeOffset UploadedAtUtc, DateTimeOffset? CompletedAtUtc, string? FailureCode, int ProcessingAttempts);
public sealed record SurveyListDto(IReadOnlyList<SurveySummaryDto> Items);
public sealed record SurveyFileDto(byte[]? Content, string? FailureCode, string MediaType, string OriginalFileName, string Sha256Hash);

public sealed record LandRevisionSummaryDto(Guid LandRevisionId, long RevisionNumber, double AreaM2, int EdgeCount, Guid? SurveyId,
    string? GeometryDigest, bool Georeferenced, bool IsCurrent, DateTimeOffset CreatedAtUtc);
public sealed record LandRevisionListDto(IReadOnlyList<LandRevisionSummaryDto> Items);
public sealed record LandRevisionDetailDto(LandRevisionSummaryDto Revision, JsonElement Land, JsonElement? Frame);

public sealed record RequirementRevisionSummaryDto(Guid RevisionId, long RevisionNumber, string SchemaVersion, string CatalogueVersion,
    bool IsCurrent, DateTimeOffset CreatedAtUtc);
public sealed record RequirementRevisionListDto(IReadOnlyList<RequirementRevisionSummaryDto> Items);
public sealed record RequirementRevisionDetailDto(RequirementRevisionSummaryDto Revision, JsonElement Content);

public sealed record GenerationJobSummaryDto(Guid JobId, string Status, string? Outcome, string? ReasonCode, string EvidenceMode,
    Guid LandRevisionId, Guid RequirementsRevisionId, int Seed, string? AlgorithmVersion, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
public sealed record GenerationJobListDto(IReadOnlyList<GenerationJobSummaryDto> Items);

public sealed record EditCandidateSummaryDto(Guid CandidateId, Guid BaseRevisionId, Guid CommandId, string Status, string GeometryHash,
    bool Expired, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record EditCandidateListDto(IReadOnlyList<EditCandidateSummaryDto> Items);
public sealed record EditCandidateDetailDto(EditCandidateSummaryDto Candidate, JsonElement Geometry);

public sealed record PlanConfirmationDto(Guid ConfirmationId, Guid PlanId, Guid ConfirmedRevisionId, string GeometryHash,
    Guid ValidationId, bool WarningAcknowledgement, DateTimeOffset ConfirmedAtUtc);

public sealed record SessionDto(Guid SessionId, DateTimeOffset StartedAtUtc, DateTimeOffset LastRefreshedAtUtc,
    DateTimeOffset ExpiresAtUtc, DateTimeOffset AbsoluteExpiresAtUtc, bool Active);
public sealed record SessionListDto(IReadOnlyList<SessionDto> Items);

public sealed record AdminProjectDetailDto(Guid ProjectId, Guid OwnerId, string Name, string? ProjectType, string Status,
    long AuthorizationVersion, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, int Surveys, int LandRevisions,
    int RequirementRevisions, int GenerationJobs, int Plans, int Reports, int ArchitectRequests);

/// <summary>
/// Read models for the Captain completion routes. Every project-scoped method is called only after the endpoint has
/// authorized the caller for that project (owner check + authorization version); these queries additionally filter by
/// project id so a foreign id never matches. Owner-scoped session methods filter by the authenticated user id.
/// </summary>
public sealed class CaptainReadModels
{
    private readonly BuildMindDbContext _context;
    private readonly TimeProvider _time;
    private readonly SurveyBlobReader _blobs;

    internal CaptainReadModels(BuildMindDbContext context, TimeProvider time, SurveyBlobReader blobs)
    {
        _context = context;
        _time = time;
        _blobs = blobs;
    }

    /// <summary>
    /// The original survey file of a project the caller was authorized for. Resolved only from the server-side storage
    /// GUID (never from client input), refusing symlinks, size mismatch and SHA-256 mismatch (integrity).
    /// </summary>
    public async Task<SurveyFileDto?> SurveyFileAsync(Guid projectId, Guid surveyId, CancellationToken token)
    {
        var s = await _context.Set<SurveyRecord>().AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Id == surveyId)
            .Select(x => new { x.StorageId, x.ByteLength, x.Sha256Hash, x.MediaType, x.OriginalFileName })
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (s is null) return null;
        var read = await _blobs.ReadAsync(s.StorageId, s.ByteLength, s.Sha256Hash, token).ConfigureAwait(false);
        return new SurveyFileDto(read.Content, read.FailureCode, s.MediaType, s.OriginalFileName, s.Sha256Hash);
    }

    public async Task<SurveyListDto> SurveysAsync(Guid projectId, CancellationToken token) =>
        new(await _context.Set<SurveyRecord>().AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.UploadedAtUtc)
            .Select(s => new SurveySummaryDto(s.Id, s.OriginalFileName, s.MediaType, s.ByteLength, s.Sha256Hash, s.Status,
                s.UploadedAtUtc, s.CompletedAtUtc, s.FailureCode, s.ProcessingAttempts))
            .ToListAsync(token).ConfigureAwait(false));

    public async Task<LandRevisionListDto> LandRevisionsAsync(Guid projectId, CancellationToken token)
    {
        var rows = await _context.Set<LandRevisionRecord>().AsNoTracking().Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.RevisionNumber).ToListAsync(token).ConfigureAwait(false);
        var current = rows.Count > 0 ? rows[0].Id : Guid.Empty;
        return new(rows.Select(l => Land(l, l.Id == current)).ToList());
    }

    public async Task<LandRevisionDetailDto?> LandRevisionAsync(Guid projectId, Guid landRevisionId, CancellationToken token)
    {
        var row = await _context.Set<LandRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.ProjectId == projectId && l.Id == landRevisionId, token).ConfigureAwait(false);
        if (row is null) return null;
        var latest = await _context.Set<LandRevisionRecord>().AsNoTracking().Where(l => l.ProjectId == projectId)
            .MaxAsync(l => l.RevisionNumber, token).ConfigureAwait(false);
        return new(Land(row, row.RevisionNumber == latest), Parse(row.LandJson), row.FrameJson is { } f ? Parse(f) : null);
    }

    public async Task<RequirementRevisionListDto> RequirementRevisionsAsync(Guid projectId, CancellationToken token)
    {
        var rows = await _context.Set<RequirementRevisionRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.RevisionNumber).ToListAsync(token).ConfigureAwait(false);
        var current = rows.Count > 0 ? rows[0].Id : Guid.Empty;
        return new(rows.Select(r => Requirement(r, r.Id == current)).ToList());
    }

    public async Task<RequirementRevisionDetailDto?> RequirementRevisionAsync(Guid projectId, Guid revisionId, CancellationToken token)
    {
        var row = await _context.Set<RequirementRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == revisionId, token).ConfigureAwait(false);
        if (row is null) return null;
        var latest = await _context.Set<RequirementRevisionRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .MaxAsync(r => r.RevisionNumber, token).ConfigureAwait(false);
        return new(Requirement(row, row.RevisionNumber == latest), Parse(row.ContentJson));
    }

    public async Task<GenerationJobListDto> GenerationJobsAsync(Guid projectId, CancellationToken token) =>
        new(await _context.Set<GenerationJobRecord>().AsNoTracking().Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Select(j => new GenerationJobSummaryDto(j.Id, j.Status, j.Outcome, j.ReasonCode, j.EvidenceMode, j.LandRevisionId,
                j.RequirementsRevisionId, j.Seed, j.AlgorithmVersion, j.CreatedAtUtc, j.CompletedAtUtc))
            .ToListAsync(token).ConfigureAwait(false));

    public async Task<EditCandidateListDto> CandidatesAsync(Guid projectId, Guid planId, CancellationToken token)
    {
        var now = _time.GetUtcNow();
        var rows = await _context.Set<PlanEditCandidateRecord>().AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.PlanId == planId).OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(token).ConfigureAwait(false);
        return new(rows.Select(c => Candidate(c, now)).ToList());
    }

    public async Task<EditCandidateDetailDto?> CandidateAsync(Guid projectId, Guid planId, Guid candidateId, CancellationToken token)
    {
        var row = await _context.Set<PlanEditCandidateRecord>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ProjectId == projectId && c.PlanId == planId && c.CandidateId == candidateId, token)
            .ConfigureAwait(false);
        return row is null ? null : new(Candidate(row, _time.GetUtcNow()), Parse(row.GeometryJson));
    }

    public async Task<PlanConfirmationDto?> ConfirmationAsync(Guid projectId, Guid planId, CancellationToken token) =>
        await _context.Set<PlanConfirmationRecord>().AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.PlanId == planId).OrderByDescending(c => c.ConfirmedAtUtc)
            .Select(c => new PlanConfirmationDto(c.ConfirmationId, c.PlanId, c.ConfirmedRevisionId, c.GeometryHash, c.ValidationId,
                c.WarningAcknowledgement, c.ConfirmedAtUtc))
            .FirstOrDefaultAsync(token).ConfigureAwait(false);

    /// <summary>Sessions = refresh-token families of the caller (token hashes are never exposed).</summary>
    public async Task<SessionListDto> SessionsAsync(Guid userId, CancellationToken token)
    {
        var now = _time.GetUtcNow();
        var tokens = await _context.Set<RefreshTokenRecord>().AsNoTracking().Where(t => t.UserId == userId)
            .Select(t => new { t.FamilyId, t.CreatedAtUtc, t.ExpiresAtUtc, t.AbsoluteExpiresAtUtc, t.ConsumedAtUtc, t.RevokedAtUtc })
            .ToListAsync(token).ConfigureAwait(false);
        var sessions = tokens.GroupBy(t => t.FamilyId).Select(g =>
        {
            var latest = g.OrderByDescending(t => t.CreatedAtUtc).First();
            var active = latest.RevokedAtUtc is null && latest.ConsumedAtUtc is null && latest.ExpiresAtUtc > now
                && latest.AbsoluteExpiresAtUtc > now && g.All(t => t.RevokedAtUtc is null);
            return new SessionDto(g.Key, g.Min(t => t.CreatedAtUtc), latest.CreatedAtUtc, latest.ExpiresAtUtc, latest.AbsoluteExpiresAtUtc, active);
        }).OrderByDescending(s => s.LastRefreshedAtUtc).ToList();
        return new(sessions);
    }

    /// <summary>Revokes every token of one of the caller's session families; returns false when the family is not the caller's.</summary>
    public async Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken token)
    {
        // Same lock order as every M1 session mutation (users row first), so a concurrent refresh rotation of this family
        // either completes before the revocation (and its new token is revoked here) or sees the family revoked.
        await using var transaction = await _context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        await BuildMind.Infrastructure.Persistence.Repositories.Identity.SessionSql.LockUserAsync(_context, userId, token).ConfigureAwait(false);
        var owned = await _context.Set<RefreshTokenRecord>().AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.FamilyId == sessionId, token).ConfigureAwait(false);
        if (!owned) return false;
        await BuildMind.Infrastructure.Persistence.Repositories.Identity.SessionSql.RevokeFamilyAsync(_context, sessionId, _time.GetUtcNow(),
            "SESSION_REVOKED_BY_USER", token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return true;
    }

    public async Task<AdminProjectDetailDto?> AdminProjectAsync(Guid projectId, CancellationToken token)
    {
        var p = await _context.Set<ProjectRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, token).ConfigureAwait(false);
        if (p is null) return null;
        return new(p.Id, p.OwnerId, p.Name, p.ProjectType, p.Status, p.AuthorizationVersion, p.CreatedAtUtc, p.UpdatedAtUtc,
            await _context.Set<SurveyRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false),
            await _context.Set<LandRevisionRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false),
            await _context.Set<RequirementRevisionRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false),
            await _context.Set<GenerationJobRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false),
            await _context.Set<BuildMind.Infrastructure.Persistence.Records.PlanEditor.PlanRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false),
            await _context.Set<ReportRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false),
            await _context.Set<ArchitectRequestRecord>().CountAsync(x => x.ProjectId == projectId, token).ConfigureAwait(false));
    }

    private static LandRevisionSummaryDto Land(LandRevisionRecord l, bool current) =>
        new(l.Id, l.RevisionNumber, l.AreaM2, l.EdgeCount, l.SurveyId, l.GeometryDigest, l.FrameJson is not null, current, l.CreatedAtUtc);

    private static RequirementRevisionSummaryDto Requirement(RequirementRevisionRecord r, bool current) =>
        new(r.Id, r.RevisionNumber, r.SchemaVersion, r.CatalogueVersion, current, r.CreatedAtUtc);

    private static EditCandidateSummaryDto Candidate(PlanEditCandidateRecord c, DateTimeOffset now) =>
        new(c.CandidateId, c.BaseRevisionId, c.CommandId, c.Status, c.GeometryHash, c.ExpiresAtUtc <= now, c.CreatedAtUtc, c.ExpiresAtUtc);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
