using System.Text.Json;
using System.Text.Json.Nodes;

namespace BuildMind.Application.Features.Generation.Contracts;

public static partial class GenerationContractValidation
{
    private static void Job(GenerationJobDto value)
    {
        Binding(value);
        Id(value.JobId);
        Id(value.CorrelationId);
        Hash(value.InputFingerprint);
        Require(value.SchemaVersion == GenerationJobWire.SchemaVersion && value.Seed >= 0);
        Require(value.State is "QUEUED" or "RUNNING" or "SUCCEEDED" or "INFEASIBLE" or "FAILED");
        var metadata = value.Idempotency;
        Require(metadata is not null && metadata.Operation == GenerationJobWire.Operation
            && metadata.ProjectId == value.ProjectId && metadata.Seed == value.Seed
            && metadata.InputFingerprint == value.InputFingerprint);
        Pattern(metadata.Key, @"\A[A-Za-z0-9._~-]{1,128}\z");
        var started = value.StartedAtUtc;
        var completed = value.CompletedAtUtc;
        Timestamp(value.CreatedAtUtc);
        if (started is not null)
            Timestamp(started.Value);
        if (completed is not null)
            Timestamp(completed.Value);
        Require(value.State == "QUEUED" ? started is null : started is not null && started >= value.CreatedAtUtc);
        if (value.State is "QUEUED" or "RUNNING")
        {
            Require(completed is null && value.Result is null && value.Failure is null);
            return;
        }

        Require(completed is not null && completed >= started);
        if (value.State == "SUCCEEDED")
        {
            Require(value.Result is not null && value.Failure is null && value.Result.Outcome == "SUCCESS");
            Result(value.Result);
            Require(Revision(value.Result) == Revision(value) && value.Result.Seed == value.Seed);
            return;
        }

        var failure = value.Failure;
        Require(failure is not null && value.Result is null);
        Code(failure.ReasonCode);
        Items(failure.Diagnostics, 1_000, 1);
        Critical(failure.Diagnostics);
        Require(failure.Diagnostics.All(item => item.Severity == "CRITICAL"
            && GenerationContractBoundary.HasPublicJobMessage(item.Code)));
        Require(failure.Diagnostics.Any(item => item.Code == failure.ReasonCode));
        var areaShortfall = RegisteredJobDiagnosticCodes.BuildableAreaInsufficient;
        Require(value.State == "INFEASIBLE"
            ? failure.Diagnostics.All(item => item.Code == areaShortfall)
            : failure.ReasonCode != areaShortfall);
    }

    /// <summary>Checks a proposed replacement against a trusted previous snapshot.</summary>
    public static void ValidateJobUpdate(GenerationJobDto previous, GenerationJobDto proposed)
    {
        Validate(previous);
        Validate(proposed);
        Require((previous.State, proposed.State) is ("QUEUED", "RUNNING")
            or ("RUNNING", "SUCCEEDED" or "INFEASIBLE" or "FAILED"));
        Require(previous.JobId == proposed.JobId && Revision(previous) == Revision(proposed)
            && previous.CorrelationId == proposed.CorrelationId && previous.Seed == proposed.Seed
            && previous.InputFingerprint == proposed.InputFingerprint
            && previous.Idempotency.Key == proposed.Idempotency.Key
            && previous.CreatedAtUtc == proposed.CreatedAtUtc
            && (previous.StartedAtUtc is null || previous.StartedAtUtc == proposed.StartedAtUtc));
    }
}

public static partial class GenerationContractBoundary
{
    private static readonly IReadOnlyDictionary<string, string> PublicJobMessages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RegisteredJobDiagnosticCodes.InvalidIdentifier] = "An identifier is invalid.",
        [RegisteredJobDiagnosticCodes.SchemaVersionUnsupported] = "The contract schema version is unsupported.",
        [RegisteredJobDiagnosticCodes.MetricUnitsRequired] = "Metric units are required.",
        [RegisteredJobDiagnosticCodes.CoordinateSystemUnsupported] = "The coordinate system is unsupported.",
        [RegisteredJobDiagnosticCodes.InputRevisionMismatch] = "The input revisions do not match.",
        [RegisteredJobDiagnosticCodes.PolygonInvalid] = "A polygon failed structural validation.",
        [RegisteredJobDiagnosticCodes.RoomOverlap] = "Rooms overlap.",
        [RegisteredJobDiagnosticCodes.OutsideBuildableFootprint] = "Geometry extends beyond the buildable footprint.",
        [RegisteredJobDiagnosticCodes.WallTopologyInvalid] = "Wall topology is invalid.",
        [RegisteredJobDiagnosticCodes.OpeningWallReferenceInvalid] = "An opening references an invalid wall.",
        [RegisteredJobDiagnosticCodes.OpeningOutsideWall] = "An opening extends beyond its wall.",
        [RegisteredJobDiagnosticCodes.OpeningCollision] = "Openings collide.",
        [RegisteredJobDiagnosticCodes.RoomInaccessible] = "A room has no supported access.",
        [RegisteredJobDiagnosticCodes.RoomRequirementUnmet] = "A room requirement is unmet.",
        [RegisteredJobDiagnosticCodes.SetbackEvidenceMissing] = "Setback evidence is missing.",
        [RegisteredJobDiagnosticCodes.RegulatoryEvidenceConflict] = "Regulatory evidence conflicts.",
        [RegisteredJobDiagnosticCodes.RequiredAreaPolicyUnavailable] = "The required-area policy is unavailable.",
        [RegisteredJobDiagnosticCodes.ValidationEvidenceStale] = "Validation evidence is stale.",
        [RegisteredJobDiagnosticCodes.ValidationUnavailable] = "Validation is unavailable.",
        [RegisteredJobDiagnosticCodes.GenerationUnavailable] = "Plan generation is unavailable.",
        [RegisteredJobDiagnosticCodes.SearchExhausted] = "The generation search ended without a result.",
        [RegisteredJobDiagnosticCodes.InsufficientDistinctAlternatives] = "The search produced too few distinct alternatives.",
        [RegisteredJobDiagnosticCodes.BuildableAreaInsufficient] = "The available buildable area is insufficient.",
        [RegisteredJobDiagnosticCodes.GenerationTimeout] = "Plan generation timed out.",
        [RegisteredJobDiagnosticCodes.GenerationFailed] = "Plan generation failed."
    };

    internal static bool HasPublicJobMessage(string code) => PublicJobMessages.ContainsKey(code);

    /// <summary>Publishes a validated snapshot. Updates require a trusted previous snapshot from the caller.</summary>
    public static string PublishJob(GenerationJobDto snapshot, GenerationJobDto? previous = null)
    {
        if (previous is null)
            GenerationContractValidation.Validate(snapshot);
        else
            GenerationContractValidation.ValidateJobUpdate(previous, snapshot);

        var payload = JsonSerializer.SerializeToNode(snapshot, Options)!.AsObject();
        if (payload["failure"] is JsonObject failure)
            Sanitize(failure);
        if (payload["result"] is JsonObject result)
        {
            Sanitize(result);
            foreach (var alternative in result["alternatives"]!.AsArray())
                Sanitize(alternative!["validation"]!.AsObject());
        }

        var output = payload.ToJsonString();
        Parse<GenerationJobDto>(output);
        return output;
    }

    private static void Sanitize(JsonObject owner)
    {
        var items = owner["diagnostics"]!.AsArray();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index]!;
            var code = item["code"]!.GetValue<string>();
            var registered = PublicJobMessages.TryGetValue(code, out var message);
            items[index] = new JsonObject
            {
                ["code"] = registered ? code : "UNCLASSIFIED_DIAGNOSTIC",
                ["severity"] = item["severity"]!.GetValue<string>(),
                ["message"] = registered ? message : "A contract check reported an issue."
            };
        }
    }
}
