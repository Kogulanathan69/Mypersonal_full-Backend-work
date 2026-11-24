using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Planning;

/// <summary>Trusted actor + fresh owner-scoped authorization snapshot (A1/A2/A8). Never a client value.</summary>
public sealed class PlanningAccess(ICurrentUser user, IProjectAuthorizationPort authorization)
{
    public async Task<(PlanningStatus Status, Guid Actor, long Version)> AuthorizeAsync(Guid projectId, CancellationToken token)
    {
        if (user.UserId is not { } actor || actor == Guid.Empty || !user.IsAuthenticated)
            return (PlanningStatus.Unauthenticated, Guid.Empty, 0);
        if (projectId == Guid.Empty)
            return (PlanningStatus.NotFound, Guid.Empty, 0);
        var result = await authorization.AuthorizeOwnerAsync(projectId, actor, token).ConfigureAwait(false);
        if (result.Status != ProjectAuthorizationStatus.Authorized || result.Snapshot is not { } snapshot
            || snapshot.ProjectId != projectId || snapshot.ActorId != actor)
            return (PlanningStatus.NotFound, Guid.Empty, 0);
        return (PlanningStatus.Success, actor, snapshot.Version.Value);
    }
}

internal static class PlanningClock
{
    /// <summary>MySQL datetime(6) keeps microseconds; truncate so stored and returned values compare exactly.</summary>
    public static DateTimeOffset Now(TimeProvider time)
    {
        var now = time.GetUtcNow().ToUniversalTime();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }
}

public static class DeterministicIds
{
    /// <summary>Name-based (SHA-256 derived, RFC 9562 version 8 layout) identifier; stable across runs.</summary>
    public static Guid From(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}

public sealed record SubmitLandCommand(IReadOnlyList<CoordinatePointDto>? Exterior, Guid? ExpectedCurrentLandRevisionId, Guid? SurveyId);

public sealed class LandService(
    PlanningAccess access, ILandRevisionStore store, IPlanningComputeClient compute, ISurveyRepository surveys,
    TimeProvider time)
{
    public const int MaxVertices = 129;
    public const double MaxCoordinateM = 1_000_000;

    public async Task<PlanningResult<LandRevision>> GetCurrentAsync(Guid projectId, CancellationToken token)
    {
        var (status, _, _) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        var current = await store.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        return current is null ? new(PlanningStatus.InputsMissing, Code: "LAND_REVISION_MISSING") : PlanningResult<LandRevision>.Ok(current);
    }

    /// <summary>V3 R-16: validate a candidate boundary without persisting anything.</summary>
    public async Task<PlanningResult<LandCheckResult>> ValidateAsync(Guid projectId, IReadOnlyList<CoordinatePointDto>? exterior,
        CancellationToken token)
    {
        var (status, _, _) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        var ring = NormalizeRing(exterior);
        if (ring is null) return new(PlanningStatus.ValidationFailed, Code: "LAND_BOUNDARY_INVALID");
        var land = new MetricLandDto
        {
            SchemaVersion = PlanningSchemas.MetricLand, CoordinateSystem = LocalFrame(),
            Boundary = new PolygonBoundaryDto { Exterior = ring, Holes = [] },
        };
        try { return PlanningResult<LandCheckResult>.Ok(await compute.ValidateLandAsync(projectId, Guid.NewGuid(), land, token).ConfigureAwait(false)); }
        catch (DependencyFailureException failure) { return DependencyFailure<LandCheckResult>(failure); }
    }

    public async Task<PlanningResult<LandRevision>> SubmitAsync(Guid projectId, SubmitLandCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (status, actor, version) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);

        var ring = NormalizeRing(command.Exterior);
        if (ring is null)
            return new(PlanningStatus.ValidationFailed, Code: "LAND_BOUNDARY_INVALID");
        if (command.ExpectedCurrentLandRevisionId == Guid.Empty)
            return new(PlanningStatus.ValidationFailed, Code: "EXPECTED_REVISION_INVALID");
        if (command.SurveyId is { } surveyId
            && (surveyId == Guid.Empty || await surveys.GetAsync(projectId, surveyId, token).ConfigureAwait(false) is null))
            return new(PlanningStatus.ValidationFailed, Code: "SURVEY_NOT_FOUND");

        var current = await store.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        if (current?.Id != command.ExpectedCurrentLandRevisionId)
            return new(PlanningStatus.Conflict, Code: "REVISION_CONFLICT");

        var land = new MetricLandDto
        {
            SchemaVersion = PlanningSchemas.MetricLand,
            CoordinateSystem = LocalFrame(),
            Boundary = new PolygonBoundaryDto { Exterior = ring, Holes = [] },
        };
        var id = Guid.NewGuid();
        LandCheckResult check;
        try { check = await compute.ValidateLandAsync(projectId, id, land, token).ConfigureAwait(false); }
        catch (DependencyFailureException failure) { return DependencyFailure<LandRevision>(failure); }
        if (!check.Valid || check.AreaM2 is not { } area || check.EdgeCount is not { } edges)
            return new(PlanningStatus.ValidationFailed, Code: check.Code);

        var revision = new LandRevision(id, projectId, (current?.RevisionNumber ?? 0) + 1, land, area, edges,
            command.SurveyId, actor, PlanningClock.Now(time));
        return await store.TryAppendAsync(revision, command.ExpectedCurrentLandRevisionId, version, token).ConfigureAwait(false) switch
        {
            ProtectedWriteStatus.Written => PlanningResult<LandRevision>.Ok(revision),
            ProtectedWriteStatus.Conflict => new(PlanningStatus.Conflict, Code: "REVISION_CONFLICT"),
            _ => new(PlanningStatus.NotFound),
        };
    }

    internal static CoordinateSystemDto LocalFrame() => new()
    {
        Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 },
        XDirection = "right", YDirection = "up",
    };

    private static IReadOnlyList<CoordinatePointDto>? NormalizeRing(IReadOnlyList<CoordinatePointDto>? points)
    {
        if (points is null || points.Count < 3 || points.Count > MaxVertices || points.Any(p => p is null))
            return null;
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > MaxCoordinateM || Math.Abs(p.Y) > MaxCoordinateM))
            return null;
        var ring = points.Select(p => new CoordinatePointDto { X = p.X, Y = p.Y }).ToList();
        if (ring[0].X != ring[^1].X || ring[0].Y != ring[^1].Y)
            ring.Add(new CoordinatePointDto { X = ring[0].X, Y = ring[0].Y });
        return ring.Count is >= 4 and <= MaxVertices ? ring : null;
    }

    internal static PlanningResult<T> DependencyFailure<T>(DependencyFailureException failure) =>
        new(failure.Kind == DependencyFailureKind.Timeout ? PlanningStatus.DependencyTimeout : PlanningStatus.DependencyUnavailable,
            Code: failure.Code);
}

public sealed record StartGenerationCommand(
    string? IdempotencyKey, Guid ExpectedLandRevisionId, Guid ExpectedRequirementsRevisionId, int? Seed);

public sealed record GenerationJobView(GenerationJob Job, IReadOnlyList<GenerationCandidate> Candidates);

public sealed partial class GenerationService(
    PlanningAccess access, ILandRevisionStore lands, IRequirementRevisionRepository requirements,
    IGenerationPolicyProvider policies, IRegulatoryEvidenceProvider evidence, IGenerationJobStore jobs,
    IPlanningComputeClient compute, TimeProvider time)
{
    public const int MaxRooms = 100;
    private static readonly Guid SyntheticReviewer = Guid.Parse("00000000-0000-4000-8000-00000000c0de");

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public async Task<PlanningResult<GenerationJobView>> GetAsync(Guid projectId, Guid jobId, CancellationToken token)
    {
        var (status, _, _) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        var job = jobId == Guid.Empty ? null : await jobs.GetAsync(projectId, jobId, token).ConfigureAwait(false);
        if (job is null) return new(PlanningStatus.NotFound);
        return PlanningResult<GenerationJobView>.Ok(new(job, await jobs.GetCandidatesAsync(projectId, jobId, token).ConfigureAwait(false)));
    }

    public async Task<PlanningResult<GenerationJobView>> StartAsync(Guid projectId, StartGenerationCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (status, actor, version) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        if (command.IdempotencyKey is not { } key || !KeyPattern().IsMatch(key))
            return new(PlanningStatus.ValidationFailed, Code: "IDEMPOTENCY_KEY_INVALID");
        if (command.ExpectedLandRevisionId == Guid.Empty || command.ExpectedRequirementsRevisionId == Guid.Empty)
            return new(PlanningStatus.ValidationFailed, Code: "EXPECTED_REVISIONS_REQUIRED");
        if (command.Seed is < 0)
            return new(PlanningStatus.ValidationFailed, Code: "SEED_INVALID");

        var replay = await ReplayAsync(projectId, actor, key, command, token).ConfigureAwait(false);
        if (replay is not null) return replay;

        var land = await lands.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        var requirement = await requirements.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        if (land is null) return new(PlanningStatus.InputsMissing, Code: "LAND_REVISION_MISSING");
        if (requirement is null) return new(PlanningStatus.InputsMissing, Code: "REQUIREMENTS_REVISION_MISSING");
        if (land.Id != command.ExpectedLandRevisionId || requirement.Id != command.ExpectedRequirementsRevisionId)
            return new(PlanningStatus.StaleInputs, Code: "STALE_INPUT_REVISIONS");
        if (policies.Get() is not { } policy)
            return new(PlanningStatus.PolicyUnavailable, Code: "GENERATION_POLICY_UNAVAILABLE");

        var seed = command.Seed ?? 1;
        var (program, programCode) = BuildRoomProgram(requirement, policy);
        if (program is null) return new(PlanningStatus.ValidationFailed, Code: programCode);

        RegulatoryEvidenceSelection regulatory;
        try { regulatory = await evidence.GetAsync(projectId, land, requirement.Id, policy, token).ConfigureAwait(false); }
        catch (DependencyFailureException failure) { return LandService.DependencyFailure<GenerationJobView>(failure); }

        var input = new GenerationInputDto
        {
            SchemaVersion = GenerationSchemas.Input, ProjectId = projectId, LandRevisionId = land.Id,
            RequirementsRevisionId = requirement.Id, Land = land.Land, RoomProgram = program,
            AdjacencyPreferences = BuildAdjacency(requirement, program), RegulatoryEvidence = regulatory.Evidence, Seed = seed,
        };

        var mode = evidence.Mode(policy);
        var running = new GenerationJob(Guid.NewGuid(), projectId, actor, key, land.Id, requirement.Id,
            GenerationJobStatus.Running, null, null, null, mode, seed, PlanningClock.Now(time), null, [], regulatory.EvidencePackId);
        switch (await jobs.TryStartAsync(running, version, token).ConfigureAwait(false))
        {
            case ProtectedWriteStatus.Denied: return new(PlanningStatus.NotFound);
            case ProtectedWriteStatus.Conflict:
                return await ReplayAsync(projectId, actor, key, command, token).ConfigureAwait(false)
                    ?? new PlanningResult<GenerationJobView>(PlanningStatus.Conflict, Code: "IDEMPOTENCY_CONFLICT");
        }

        GenerationResultDto result;
        try
        {
            result = await compute.RunGenerationAsync(input, mode == PlanningSchemas.EvidenceModeSynthetic, token).ConfigureAwait(false);
            if (result.ProjectId != projectId || result.LandRevisionId != land.Id || result.RequirementsRevisionId != requirement.Id
                || result.Seed != seed || result.Alternatives.Any(a => a.Geometry.ProjectId != projectId
                    || a.Geometry.LandRevisionId != land.Id || a.Geometry.RequirementsRevisionId != requirement.Id))
                throw new DependencyFailureException("python", DependencyFailureKind.UnexpectedResponse);
        }
        catch (DependencyFailureException failure)
        {
            var failed = running with
            {
                Status = GenerationJobStatus.Failed, Outcome = GenerationOutcomes.Unavailable,
                ReasonCode = failure.Code, CompletedAtUtc = PlanningClock.Now(time),
            };
            await jobs.TryCompleteAsync(failed, [], version, CancellationToken.None).ConfigureAwait(false);
            return LandService.DependencyFailure<GenerationJobView>(failure);
        }

        var success = result.Outcome == GenerationOutcomes.Success;
        var now = PlanningClock.Now(time);
        var candidates = success
            ? result.Alternatives.Select((alternative, index) =>
            {
                var json = JsonSerializer.Serialize(alternative.Geometry, PlanEditorJson.StrictOptions);
                return new GenerationCandidate(alternative.Geometry.PlanId, running.Id, projectId, index, alternative.Geometry,
                    json, PlanRevisionService.ComputeGeometryHash(json), alternative.Metrics, alternative.Validation,
                    alternative.DiversitySignature.Value, now);
            }).ToArray()
            : [];
        var completed = running with
        {
            Status = success ? GenerationJobStatus.Succeeded : GenerationJobStatus.Failed,
            Outcome = result.Outcome, ReasonCode = result.ReasonCode, AlgorithmVersion = result.AlgorithmVersion,
            CompletedAtUtc = now, Diagnostics = result.Diagnostics,
        };
        if (await jobs.TryCompleteAsync(completed, candidates, version, token).ConfigureAwait(false) != ProtectedWriteStatus.Written)
            return new(PlanningStatus.NotFound); // authorization changed during generation: nothing is published

        var view = new GenerationJobView(completed, candidates);
        return success
            ? PlanningResult<GenerationJobView>.Ok(view)
            : new(PlanningStatus.GenerationUnsuccessful, view, result.ReasonCode, result.Diagnostics);
    }

    private async Task<PlanningResult<GenerationJobView>?> ReplayAsync(Guid projectId, Guid actor, string key,
        StartGenerationCommand command, CancellationToken token)
    {
        var existing = await jobs.FindByKeyAsync(projectId, actor, key, token).ConfigureAwait(false);
        if (existing is null) return null;
        if (existing.LandRevisionId != command.ExpectedLandRevisionId
            || existing.RequirementsRevisionId != command.ExpectedRequirementsRevisionId
            || command.Seed is { } seed && seed != existing.Seed)
            return new(PlanningStatus.Conflict, Code: "IDEMPOTENCY_KEY_REUSED");
        if (existing.Status == GenerationJobStatus.Running)
            return new(PlanningStatus.GenerationInProgress, Code: "GENERATION_IN_PROGRESS");
        var view = new GenerationJobView(existing, await jobs.GetCandidatesAsync(projectId, existing.Id, token).ConfigureAwait(false));
        return existing.Status == GenerationJobStatus.Succeeded
            ? PlanningResult<GenerationJobView>.Ok(view)
            : new(PlanningStatus.GenerationUnsuccessful, view, existing.ReasonCode, existing.Diagnostics);
    }

    internal static (RoomProgramDto? Program, string Code) BuildRoomProgram(RequirementRevision requirement, GenerationPolicy policy)
    {
        if (requirement.CatalogueVersion != GenerationSchemas.RoomCatalogue)
            return (null, "CATALOGUE_UNSUPPORTED");
        if (requirement.Content.FloorCount != 1)
            return (null, "FLOOR_SCOPE_UNSUPPORTED");
        var rooms = new List<RoomSlotDto>();
        foreach (var room in requirement.Content.GroundFloorRooms.OrderBy(r => r.RoomType, StringComparer.Ordinal))
        {
            if (!policy.RoomDimensions.TryGetValue(room.RoomType, out var dimensions))
                return (null, "ROOM_TYPE_UNSUPPORTED");
            for (var index = 0; index < room.Count; index++)
                rooms.Add(new RoomSlotDto
                {
                    SlotId = DeterministicIds.From($"slot|{requirement.Id:N}|{room.RoomType}|{index}"),
                    RoomType = room.RoomType, Dimensions = dimensions,
                });
        }
        if (rooms.Count is 0 or > MaxRooms) return (null, "ROOM_PROGRAM_SIZE_UNSUPPORTED");
        return (new RoomProgramDto { Level = 0, CatalogueVersion = GenerationSchemas.RoomCatalogue, Rooms = rooms }, "");
    }

    internal static IReadOnlyList<AdjacencyPreferenceDto> BuildAdjacency(RequirementRevision requirement, RoomProgramDto program)
    {
        var first = program.Rooms.GroupBy(r => r.RoomType).ToDictionary(g => g.Key, g => g.First().SlotId);
        var pairs = new HashSet<(Guid, Guid)>();
        var result = new List<AdjacencyPreferenceDto>();
        foreach (var relation in requirement.Content.Relationships)
        {
            if (relation.Kind is not (GenerationAdjacencyRelationships.Adjacent or GenerationAdjacencyRelationships.Separated)
                || !first.TryGetValue(relation.SourceRoomType, out var from) || !first.TryGetValue(relation.TargetRoomType, out var to)
                || from == to)
                continue;
            var pair = from.CompareTo(to) < 0 ? (from, to) : (to, from);
            if (pairs.Add(pair))
                result.Add(new AdjacencyPreferenceDto { FromSlotId = from, ToSlotId = to, Relationship = relation.Kind });
        }
        return result;
    }

    /// <summary>Synthetic, explicitly labelled REVIEWED evidence for local development (never real rules).</summary>
    internal static RegulatoryEvidenceDto SyntheticEvidence(LandRevision land, GenerationPolicy policy) => new()
    {
        Status = RegulatoryEvidenceStatuses.Reviewed,
        RuleSetVersion = policy.SyntheticRuleSetVersion,
        Constraints = Enumerable.Range(0, land.EdgeCount).Select(edge => new ReviewedConstraintDto
        {
            RuleId = $"synthetic-setback-edge-{edge}",
            Type = GenerationConstraintTypes.Setback,
            Source = new SourceProvenanceDto
            {
                SourceVersion = policy.Version, SourceLocator = $"captain-synthetic-fixture/edge/{edge}",
                Jurisdiction = "Synthetic fixture only - not a real jurisdiction",
            },
            ParcelEdgeIndex = edge,
            DistanceM = policy.SyntheticSetbackM!.Value,
            Applicability = new ConstraintApplicabilityDto { Status = "APPLICABLE", Basis = "Synthetic development fixture" },
            ReviewedBy = SyntheticReviewer,
            ReviewReference = "synthetic-fixture-not-reviewed",
        }).ToArray(),
        Gaps = [],
    };

    internal static RegulatoryEvidenceDto MissingEvidence(string gap) => new()
    {
        Status = RegulatoryEvidenceStatuses.Missing, RuleSetVersion = null, Constraints = [],
        Gaps = [new EvidenceGapDto { Code = gap }],
    };
}

public sealed record RegulatoryEvidenceSelection(RegulatoryEvidenceDto Evidence, string? EvidencePackId = null);

/// <summary>Selects regulatory evidence for generation according to the policy's evidence mode.</summary>
public interface IRegulatoryEvidenceProvider
{
    string Mode(GenerationPolicy policy);

    /// <summary>Dependency failures propagate (fail closed); the caller maps them to 503/504.</summary>
    Task<RegulatoryEvidenceSelection> GetAsync(Guid projectId, LandRevision land, Guid requirementsRevisionId, GenerationPolicy policy,
        CancellationToken token);
}

/// <summary>"synthetic-fixture" or MISSING. Never invents real rule values.</summary>
public sealed class PolicyRegulatoryEvidenceProvider : IRegulatoryEvidenceProvider
{
    public string Mode(GenerationPolicy policy) =>
        policy.EvidenceMode == PlanningSchemas.EvidenceModeSynthetic && policy.SyntheticSetbackM is >= 0
            && !string.IsNullOrWhiteSpace(policy.SyntheticRuleSetVersion)
            ? PlanningSchemas.EvidenceModeSynthetic
            : PlanningSchemas.EvidenceModeNone;

    public Task<RegulatoryEvidenceSelection> GetAsync(Guid projectId, LandRevision land, Guid requirementsRevisionId,
        GenerationPolicy policy, CancellationToken token) =>
        Task.FromResult(new RegulatoryEvidenceSelection(Mode(policy) == PlanningSchemas.EvidenceModeSynthetic
            ? GenerationService.SyntheticEvidence(land, policy)
            : GenerationService.MissingEvidence("REGULATORY_EVIDENCE_NOT_INTEGRATED")));
}

/// <summary>
/// "rag-constraints" mode: consumes the existing M7 constraint contract (<see cref="IRegulatoryConstraintSource"/>) and
/// the existing <see cref="ConstraintEvidenceGate"/> unchanged. FAIL CLOSED:
/// <list type="bullet">
/// <item>the RAG dependency failing propagates (no generation; 503/504);</item>
/// <item>synthetic constraint packs never count as reviewed (<c>allowSynthetic: false</c>);</item>
/// <item>even fully verified kinds are passed to generation as UNVERIFIED with the gap
/// <c>PARCEL_EDGE_ROLE_MAPPING_UNAPPROVED</c>: mapping front/rear/side rules to parcel edges is an open M6 decision
/// (doc 29 §11), so no setback value is placed on an edge and generation truthfully answers NEEDS_REVIEW.</item>
/// </list>
/// The evidence pack id is returned for traceability on the generation job.
/// </summary>
public sealed class RagRegulatoryEvidenceProvider(
    IRegulatoryConstraintSource constraints, IProjectRepository projects, PolicyRegulatoryEvidenceProvider fallback)
    : IRegulatoryEvidenceProvider
{
    public static readonly RegulatoryConstraintKind[] MandatoryKinds =
        [RegulatoryConstraintKind.FrontSetbackMin, RegulatoryConstraintKind.RearSpaceMin, RegulatoryConstraintKind.SideSpaceMin];

    public string Mode(GenerationPolicy policy) =>
        policy.EvidenceMode == PlanningSchemas.EvidenceModeRag ? PlanningSchemas.EvidenceModeRag : fallback.Mode(policy);

    public async Task<RegulatoryEvidenceSelection> GetAsync(Guid projectId, LandRevision land, Guid requirementsRevisionId,
        GenerationPolicy policy, CancellationToken token)
    {
        if (Mode(policy) != PlanningSchemas.EvidenceModeRag)
            return await fallback.GetAsync(projectId, land, requirementsRevisionId, policy, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(policy.Jurisdiction))
            return new(GenerationService.MissingEvidence("JURISDICTION_NOT_CONFIGURED"));
        var project = await projects.GetByIdAsync(projectId, token).ConfigureAwait(false);
        if (project is null)
            return new(GenerationService.MissingEvidence("PROJECT_REVISION_UNAVAILABLE"));

        var pack = await constraints.GetConstraintsAsync(new RegulatoryConstraintQuery(policy.Jurisdiction, MandatoryKinds,
            new ConstraintBinding(projectId, project.Revision, land.Id, requirementsRevisionId), ResidentialOnly: true), token)
            .ConfigureAwait(false);
        var assessment = ConstraintEvidenceGate.Assess(pack, MandatoryKinds, allowSynthetic: false);
        var gaps = assessment.Gaps.ToList();
        var status = assessment.RegulatoryEvidenceStatus;
        if (status == RegulatoryEvidenceStatuses.Reviewed)
        {
            status = RegulatoryEvidenceStatuses.Unverified;
            gaps.Add("PARCEL_EDGE_ROLE_MAPPING_UNAPPROVED");
        }
        if (gaps.Count == 0)
            gaps.Add("REGULATORY_EVIDENCE_INCOMPLETE");
        return new(new RegulatoryEvidenceDto
        {
            Status = status,
            RuleSetVersion = status == RegulatoryEvidenceStatuses.Missing ? null : assessment.RuleSetVersion,
            Constraints = [],
            Gaps = gaps.Distinct(StringComparer.Ordinal).Select(code => new EvidenceGapDto { Code = code }).ToArray(),
        }, pack.EvidencePackId);
    }
}

public sealed record SaveCandidateCommand(string? ChangeSummary);

/// <summary>Saves one generated candidate as a new plan (revision 1) after server-side re-validation.</summary>
public sealed class PlanSaveService(
    PlanningAccess access, IGenerationJobStore jobs, ILandRevisionStore lands, IRequirementRevisionRepository requirements,
    IGenerationPolicyProvider policies, IPlanningComputeClient compute, IPlanCreationStore plans, TimeProvider time)
{
    public async Task<PlanningResult<PlanRevisionSnapshot>> SaveCandidateAsync(
        Guid projectId, Guid jobId, Guid candidateId, SaveCandidateCommand command, CancellationToken token)
    {
        var (status, actor, version) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        if (command.ChangeSummary is { Length: > 500 })
            return new(PlanningStatus.ValidationFailed, Code: "CHANGE_SUMMARY_TOO_LONG");
        var job = jobId == Guid.Empty ? null : await jobs.GetAsync(projectId, jobId, token).ConfigureAwait(false);
        if (job is null) return new(PlanningStatus.NotFound);
        if (job.Status != GenerationJobStatus.Succeeded) return new(PlanningStatus.Conflict, Code: "GENERATION_NOT_SUCCESSFUL");
        var candidate = (await jobs.GetCandidatesAsync(projectId, jobId, token).ConfigureAwait(false))
            .SingleOrDefault(c => c.Id == candidateId);
        if (candidate is null) return new(PlanningStatus.NotFound);

        var land = await lands.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        var requirement = await requirements.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        if (land?.Id != job.LandRevisionId || requirement?.Id != job.RequirementsRevisionId)
            return new(PlanningStatus.StaleInputs, Code: "STALE_INPUT_REVISIONS");

        var policy = policies.Get();
        var synthetic = job.EvidenceMode == PlanningSchemas.EvidenceModeSynthetic;
        if (synthetic && policy?.SyntheticSetbackM is null)
            return new(PlanningStatus.PolicyUnavailable, Code: "GENERATION_POLICY_UNAVAILABLE");
        var check = await ValidateAsync(compute, land, requirement, candidate.Geometry,
            synthetic ? policy!.SyntheticSetbackM : null, token).ConfigureAwait(false);
        if (check.Status != PlanningStatus.Success) return new(check.Status, Code: check.Code);
        if (!check.Value!.GeometryValid || check.Value.RequirementsMatch != true)
            return new(PlanningStatus.ValidationFailed, Code: "CANDIDATE_REVALIDATION_FAILED", Diagnostics: check.Value.Diagnostics);

        var now = PlanningClock.Now(time);
        var evidence = IssueEvidence(projectId, candidate.Geometry.PlanId, candidate.GeometryHash, check.Value,
            synthetic ? PlanningSchemas.SyntheticEvidenceStatus : PlanningSchemas.UnverifiedEvidenceStatus, now);
        var revision = new PlanRevisionSnapshot(Guid.NewGuid(), candidate.Geometry.PlanId, projectId, null, 1,
            candidate.Geometry.SchemaVersion, candidate.GeometryJson, candidate.GeometryHash, land.Id, requirement.Id,
            evidence.Reference, null, actor, now,
            command.ChangeSummary ?? $"Saved generated alternative {candidate.Ordinal + 1} (preliminary)");
        return await plans.TryCreateAsync(revision, evidence, version, token).ConfigureAwait(false) switch
        {
            ProtectedWriteStatus.Written => PlanningResult<PlanRevisionSnapshot>.Ok(revision),
            ProtectedWriteStatus.Conflict => new(PlanningStatus.Conflict, Code: "PLAN_ALREADY_SAVED"),
            _ => new(PlanningStatus.NotFound),
        };
    }

    internal static async Task<PlanningResult<PlanCheckResult>> ValidateAsync(IPlanningComputeClient compute, LandRevision land,
        RequirementRevision requirement, PlanGeometryDto geometry, double? setbackM, CancellationToken token)
    {
        try
        {
            var counts = requirement.Content.GroundFloorRooms.Where(r => r.Count > 0)
                .ToDictionary(r => r.RoomType, r => r.Count, StringComparer.Ordinal);
            var setbacks = setbackM is { } distance ? Enumerable.Repeat(distance, land.EdgeCount).ToArray() : null;
            return PlanningResult<PlanCheckResult>.Ok(await compute.ValidatePlanAsync(
                new PlanCheckRequest(land.ProjectId, land.Id, land.Land, setbacks, counts, geometry), token).ConfigureAwait(false));
        }
        catch (DependencyFailureException failure)
        {
            return LandService.DependencyFailure<PlanCheckResult>(failure);
        }
    }

    internal static IssuedValidationEvidence IssueEvidence(Guid projectId, Guid planId, string geometryHash, PlanCheckResult check,
        string evidenceStatus, DateTimeOffset now)
    {
        var blocking = !check.GeometryValid || check.Diagnostics.Any(d => d.Severity == DiagnosticSeverities.Critical);
        return new IssuedValidationEvidence(projectId, planId, new PlanValidationReferenceDto
        {
            ValidationId = Guid.NewGuid(), ValidatorVersion = check.ValidatorVersion, GeometryHash = geometryHash,
            GeometryValid = check.GeometryValid, RequirementsMatch = check.RequirementsMatch,
            RegulatoryEvidenceStatus = evidenceStatus, HasBlockingIssues = blocking, ValidatedAtUtc = now,
        }, check.Diagnostics);
    }
}
