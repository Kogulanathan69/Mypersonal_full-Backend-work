using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Derives the project lifecycle from persisted, revision-bound facts (V3 06-WORKFLOWS: "project state derived from
/// valid prerequisites, not a client-controlled flag"). Nothing is written; a stale artefact (generated for, saved on,
/// confirmed at or reported from inputs that are no longer current) never counts towards a later stage.
/// The stage names are a Captain-local proposal (PROJECT-LIFECYCLE-STATE-MACHINE.md), not ratified enums.
/// </summary>
internal sealed class MySqlProjectLifecycleReader(BuildMindDbContext context) : IProjectLifecycleReader
{
    public async Task<ProjectLifecycleView> GetAsync(Guid projectId, CancellationToken token)
    {
        var land = await context.Set<LandRevisionRecord>().AsNoTracking().Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.RevisionNumber).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(token).ConfigureAwait(false);
        var requirements = await context.Set<RequirementHeadRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .Select(r => (Guid?)r.CurrentRevisionId).SingleOrDefaultAsync(token).ConfigureAwait(false);
        var surveys = await context.Set<SurveyRecord>().AsNoTracking().CountAsync(s => s.ProjectId == projectId, token).ConfigureAwait(false);
        var reviewed = await context.Set<SurveyExtractionReviewRecord>().AsNoTracking().AnyAsync(r => r.ProjectId == projectId, token)
            .ConfigureAwait(false);

        var jobs = await context.Set<GenerationJobRecord>().AsNoTracking().Where(j => j.ProjectId == projectId && j.Status == "Succeeded")
            .Select(j => new { j.LandRevisionId, j.RequirementsRevisionId }).ToListAsync(token).ConfigureAwait(false);
        var heads = await (from p in context.Set<PlanRecord>().AsNoTracking()
                           join r in context.Set<PlanRevisionRecord>().AsNoTracking() on p.HeadRevisionId equals r.RevisionId
                           where p.ProjectId == projectId
                           select new { p.PlanId, r.RevisionId, r.LandRevisionId, r.RequirementsRevisionId }).ToListAsync(token).ConfigureAwait(false);
        var confirmations = await context.Set<PlanConfirmationRecord>().AsNoTracking().Where(c => c.ProjectId == projectId)
            .Select(c => new { c.ConfirmationId, c.PlanId, c.ConfirmedRevisionId }).ToListAsync(token).ConfigureAwait(false);
        var reports = await context.Set<ReportRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .Select(r => new { r.Id, r.ConfirmationId, r.Status }).ToListAsync(token).ConfigureAwait(false);
        var requests = await context.Set<ArchitectRequestRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .Select(r => new { r.ReportId, r.Status }).ToListAsync(token).ConfigureAwait(false);

        bool Current(Guid landId, Guid requirementsId) => landId == land && requirementsId == requirements;
        var currentJobs = jobs.Count(j => Current(j.LandRevisionId, j.RequirementsRevisionId));
        var currentPlans = heads.Where(h => Current(h.LandRevisionId, h.RequirementsRevisionId)).ToArray();
        var headByPlan = heads.ToDictionary(h => h.PlanId, h => h.RevisionId);
        var liveConfirmations = confirmations.Where(c => headByPlan.TryGetValue(c.PlanId, out var head) && head == c.ConfirmedRevisionId
                                                         && currentPlans.Any(p => p.PlanId == c.PlanId)).Select(c => c.ConfirmationId).ToHashSet();
        var readyReports = reports.Where(r => r.Status == "Ready" && liveConfirmations.Contains(r.ConfirmationId)).Select(r => r.Id).ToHashSet();
        var activeRequests = requests.Count(r => r.ReportId is { } report && readyReports.Contains(report) && r.Status is "Pending" or "Accepted");

        var milestones = new List<LifecycleMilestone>
        {
            new("SURVEY_UPLOADED", surveys > 0, false),
            new("SURVEY_REVIEWED", reviewed, false),
            new("LAND_CONFIRMED", land is not null, true),
            new("REQUIREMENTS_COMPLETED", requirements is not null, true),
            new("PLAN_GENERATED", currentJobs > 0, true),
            new("PLAN_SELECTED", currentPlans.Length > 0, true),
            new("PLAN_CONFIRMED", liveConfirmations.Count > 0, true),
            new("REPORT_READY", readyReports.Count > 0, true),
            new("ARCHITECT_REVIEW_REQUESTED", activeRequests > 0, false),
        };
        // Stage = the furthest milestone whose required predecessors are all satisfied (survey steps are optional because
        // the boundary may be entered manually; architect review is optional).
        var stage = "DRAFT";
        var chainIntact = true;
        foreach (var m in milestones)
        {
            if (m.Required) chainIntact &= m.Satisfied;
            if (m.Satisfied && chainIntact) stage = m.Code;
        }
        if (land is not null && requirements is not null && stage is "REQUIREMENTS_COMPLETED" or "LAND_CONFIRMED") stage = "GENERATION_READY";

        var stale = new LifecycleStaleness(
            StaleGenerationJobs: jobs.Count - currentJobs,
            StalePlans: heads.Count - currentPlans.Length,
            SupersededConfirmations: confirmations.Count - liveConfirmations.Count,
            StaleReports: reports.Count(r => r.Status == "Ready" && !liveConfirmations.Contains(r.ConfirmationId)));
        return new ProjectLifecycleView(projectId, stage, milestones, stale, Next(stage, land, requirements));
    }

    private static IReadOnlyList<string> Next(string stage, Guid? land, Guid? requirements) => stage switch
    {
        "DRAFT" or "SURVEY_UPLOADED" or "SURVEY_REVIEWED" => [.. land is null ? ["CONFIRM_LAND"] : Array.Empty<string>(),
            .. requirements is null ? ["SAVE_REQUIREMENTS"] : Array.Empty<string>()],
        "GENERATION_READY" => ["START_GENERATION"],
        "PLAN_GENERATED" => ["SELECT_ALTERNATIVE"],
        "PLAN_SELECTED" => ["EDIT_OR_CONFIRM_PLAN"],
        "PLAN_CONFIRMED" => ["GENERATE_REPORT"],
        "REPORT_READY" => ["REQUEST_ARCHITECT_REVIEW_OPTIONAL"],
        _ => [],
    };
}
