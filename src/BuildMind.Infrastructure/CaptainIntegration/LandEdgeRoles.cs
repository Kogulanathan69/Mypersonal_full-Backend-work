using System.Text.Json;
using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.CaptainIntegration;

public sealed record LandEdgeDto(Guid EdgeId, double[] Start, double[] End, double LengthM, double GridBearingDeg, Guid? ParentEdgeId,
    string Role, string? RoleSource, string? EvidenceNote, DateTimeOffset? ReviewedAtUtc, string? SuggestedRole);

public sealed record LandEdgeListDto(Guid LandRevisionId, long RevisionNumber, string? GeometryDigest, IReadOnlyList<LandEdgeDto> Edges,
    int RoadFrontageEdges, int UnassignedEdges, bool ReviewComplete, string Notice);

public enum EdgeRoleWriteStatus { Written, NotFound, StaleLandRevision, EdgeNotFound, InvalidRole, Denied }

/// <summary>
/// Boundary roles (road frontage / side / rear) are recorded ONLY from an explicit customer review, bound to the exact
/// land revision and physical edge id (UUIDv5 of the edge's endpoints). Nothing is inferred: an edge without a review
/// stays UNASSIGNED. Across revisions the parent edge's reviewed role is offered as <c>suggestedRole</c> (lineage),
/// never applied. Writes run inside the protected planning transaction (project row lock, owner, authorization version)
/// and only against the CURRENT land revision.
/// </summary>
public sealed class LandEdgeRoleService
{
    public static readonly IReadOnlySet<string> Roles = new HashSet<string>(StringComparer.Ordinal) { "ROAD_FRONTAGE", "SIDE", "REAR" };
    private readonly BuildMindDbContext _context;
    private readonly TransactionRunner _transactions;
    private readonly TimeProvider _time;

    internal LandEdgeRoleService(BuildMindDbContext context, TransactionRunner transactions, TimeProvider time)
    {
        _context = context;
        _transactions = transactions;
        _time = time;
    }

    public async Task<LandEdgeListDto?> GetAsync(Guid projectId, CancellationToken token)
    {
        var lands = await _context.Set<LandRevisionRecord>().AsNoTracking().Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.RevisionNumber).Take(2).ToListAsync(token).ConfigureAwait(false);
        if (lands.Count == 0 || lands[0].FrameJson is not { } frame) return null;
        var current = lands[0];
        var ids = lands.Select(l => l.Id).ToArray();
        var reviews = await _context.Set<LandEdgeRoleRecord>().AsNoTracking().Where(r => ids.Contains(r.LandRevisionId))
            .ToListAsync(token).ConfigureAwait(false);
        var mine = reviews.Where(r => r.LandRevisionId == current.Id).ToDictionary(r => r.EdgeId);
        var parent = lands.Count > 1 ? reviews.Where(r => r.LandRevisionId == lands[1].Id).ToDictionary(r => r.EdgeId) : [];
        var edges = Edges(frame).Select(e =>
        {
            var review = mine.GetValueOrDefault(e.EdgeId);
            var suggested = review is null
                ? (parent.GetValueOrDefault(e.EdgeId) ?? (e.ParentEdgeId is { } p ? parent.GetValueOrDefault(p) : null))?.Role
                : null;
            return e with
            {
                Role = review?.Role ?? "UNASSIGNED", RoleSource = review?.EvidenceSource, EvidenceNote = review?.EvidenceNote,
                ReviewedAtUtc = review?.ReviewedAtUtc, SuggestedRole = suggested,
            };
        }).ToList();
        var unassigned = edges.Count(e => e.Role == "UNASSIGNED");
        var frontage = edges.Count(e => e.Role == "ROAD_FRONTAGE");
        return new LandEdgeListDto(current.Id, current.RevisionNumber, current.GeometryDigest, edges, frontage, unassigned,
            unassigned == 0 && frontage >= 1,
            "Boundary roles are recorded only from your review; nothing is inferred. Roles are not a cadastral or legal determination.");
    }

    public async Task<EdgeRoleWriteStatus> SetAsync(Guid projectId, Guid actor, long authorizationVersion, Guid expectedLandRevisionId,
        Guid edgeId, string role, string? note, CancellationToken token)
    {
        if (role != "UNASSIGNED" && !Roles.Contains(role)) return EdgeRoleWriteStatus.InvalidRole;
        if (note is { Length: > 500 }) return EdgeRoleWriteStatus.InvalidRole;
        var outcome = EdgeRoleWriteStatus.Written;
        var status = await new ProtectedPlanningWriter(_context, _transactions).RunAsync(projectId, actor, authorizationVersion, async inner =>
        {
            var current = await _context.Set<LandRevisionRecord>().AsNoTracking().Where(l => l.ProjectId == projectId)
                .OrderByDescending(l => l.RevisionNumber).Select(l => new { l.Id, l.FrameJson }).FirstOrDefaultAsync(inner).ConfigureAwait(false);
            if (current?.FrameJson is null) { outcome = EdgeRoleWriteStatus.NotFound; return ProtectedWriteStatus.Conflict; }
            if (current.Id != expectedLandRevisionId) { outcome = EdgeRoleWriteStatus.StaleLandRevision; return ProtectedWriteStatus.Conflict; }
            if (!Edges(current.FrameJson).Any(e => e.EdgeId == edgeId)) { outcome = EdgeRoleWriteStatus.EdgeNotFound; return ProtectedWriteStatus.Conflict; }
            await _context.Set<LandEdgeRoleRecord>().Where(r => r.LandRevisionId == current.Id && r.EdgeId == edgeId)
                .ExecuteDeleteAsync(inner).ConfigureAwait(false);
            if (role != "UNASSIGNED")
            {
                _context.Add(new LandEdgeRoleRecord
                {
                    Id = Guid.NewGuid(), ProjectId = projectId, LandRevisionId = current.Id, EdgeId = edgeId, Role = role,
                    EvidenceSource = "CUSTOMER_DECLARED", EvidenceNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                    ReviewedBy = actor, ReviewedAtUtc = Micro(_time.GetUtcNow()),
                });
                await _context.SaveChangesAsync(inner).ConfigureAwait(false);
            }
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => EdgeRoleWriteStatus.Written,
            ProtectedWriteStatus.Denied => EdgeRoleWriteStatus.Denied,
            _ => outcome == EdgeRoleWriteStatus.Written ? EdgeRoleWriteStatus.StaleLandRevision : outcome,
        };
    }

    private static DateTimeOffset Micro(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    private static IEnumerable<LandEdgeDto> Edges(string frameJson)
    {
        using var document = JsonDocument.Parse(frameJson);
        if (!document.RootElement.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array) yield break;
        foreach (var e in edges.EnumerateArray())
        {
            static double[] P(JsonElement v) => v.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            yield return new LandEdgeDto(e.GetProperty("edgeId").GetGuid(), P(e.GetProperty("start")), P(e.GetProperty("end")),
                e.GetProperty("lengthM").GetDouble(), e.GetProperty("gridBearingDeg").GetDouble(),
                e.TryGetProperty("parentEdgeId", out var parent) && parent.ValueKind == JsonValueKind.String ? parent.GetGuid() : null,
                "UNASSIGNED", null, null, null, null);
        }
    }
}
