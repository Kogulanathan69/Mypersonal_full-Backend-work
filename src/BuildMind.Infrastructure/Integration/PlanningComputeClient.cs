using System.Text.Json;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.Planning;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// Captain routes on the private Python service (python-services/app/main.py): land validation, generation and plan
/// validation. Same hardened transport and internal token as <see cref="PythonServiceClient"/>; never retried.
/// Generation responses are accepted only after the existing strict M6 contract boundary parses them
/// (<see cref="GenerationContractBoundary"/>), which is the live C#↔Python contract-parity check.
/// </summary>
internal sealed class PlanningComputeClient(PythonServiceClient python) : IPlanningComputeClient
{
    private const string LandSchema = "captain-land-validation/1";
    private const string RunSchema = "captain-generation-run/1";
    private const string PlanSchema = "captain-plan-validation/1";

    public async Task<LandCheckResult> ValidateLandAsync(Guid projectId, Guid landRevisionId, MetricLandDto land, CancellationToken token)
    {
        var wire = await python.PostJsonAsync<object, LandWire>("internal/v1/land/validate",
            new { schemaVersion = LandSchema, projectId, landRevisionId, land },
            r => r.SchemaVersion == LandSchema && !string.IsNullOrWhiteSpace(r.Code)
                 && (!r.Valid || r.AreaM2 is > 0 && double.IsFinite(r.AreaM2.Value) && r.EdgeCount is >= 3),
            token).ConfigureAwait(false);
        return new LandCheckResult(wire.Valid, wire.Code, wire.AreaM2, wire.EdgeCount);
    }

    public async Task<GenerationResultDto> RunGenerationAsync(GenerationInputDto input, bool syntheticReviewedSetbacks, CancellationToken token)
    {
        var raw = await python.PostJsonVerifiedAsync<object, JsonElement>("internal/v1/generation/run",
            new { schemaVersion = RunSchema, input, syntheticReviewedSetbacks }, (_, _) => true, token).ConfigureAwait(false);
        try
        {
            return GenerationContractBoundary.Parse<GenerationResultDto>(raw.GetRawText());
        }
        catch (GenerationContractException)
        {
            throw new DependencyFailureException(PythonServiceClient.DependencyName, DependencyFailureKind.MalformedResponse);
        }
    }

    public async Task<PlanCheckResult> ValidatePlanAsync(PlanCheckRequest request, CancellationToken token)
    {
        var wire = await python.PostJsonAsync<object, PlanWire>("internal/v1/plans/validate",
            new
            {
                schemaVersion = PlanSchema, projectId = request.ProjectId, landRevisionId = request.LandRevisionId,
                land = request.Land, setbackDistancesM = request.SetbackDistancesM,
                expectedRoomCounts = request.ExpectedRoomCounts, geometry = request.Geometry,
            },
            r => r.SchemaVersion == PlanSchema && !string.IsNullOrWhiteSpace(r.ValidatorVersion)
                 && (r.GeometryValid || r.Diagnostics.Any(d => d.Severity == DiagnosticSeverities.Critical)),
            token).ConfigureAwait(false);
        return new PlanCheckResult(wire.GeometryValid, wire.RequirementsMatch, wire.ValidatorVersion, wire.Diagnostics);
    }

    private sealed record LandWire(string SchemaVersion, bool Valid, string Code, double? AreaM2, int? EdgeCount);

    private sealed record PlanWire(string SchemaVersion, string ValidatorVersion, bool GeometryValid, bool? RequirementsMatch,
        string? GeometryHash, DiagnosticDto[] Diagnostics);
}

/// <summary>
/// Captain survey extraction route (<c>POST /internal/v1/survey/extract</c>). The response is validated strictly: the
/// extraction and file identities must echo the request, the outcome must be a known terminal value and the stored
/// evidence is bounded. Anything else is a malformed dependency response.
/// </summary>
internal sealed class SurveyExtractionClient(PythonServiceClient python) : ISurveyExtractionClient
{
    private const string Schema = "captain-survey-extract/1";

    public async Task<SurveyExtractionOutput> ExtractAsync(SurveyExtractionInput input, CancellationToken token)
    {
        var raw = await python.PostJsonVerifiedAsync<object, JsonElement>("internal/v1/survey/extract", new
        {
            schemaVersion = Schema, extractionId = input.ExtractionId, fileId = input.FileId, mediaType = input.MediaType,
            byteLength = input.Content.Length, sha256 = input.Sha256, contentBase64 = Convert.ToBase64String(input.Content),
        }, (_, _) => true, token).ConfigureAwait(false);
        try
        {
            if (raw.GetProperty("schemaVersion").GetString() != Schema)
                throw new FormatException();
            var method = raw.GetProperty("method").GetString();
            var extraction = raw.GetProperty("extraction");
            var outcome = extraction.GetProperty("status").GetString();
            if (method is not ("PDF_TEXT" or "OCR") || outcome is not ("COMPLETED" or "NEEDS_REVIEW" or "FAILED")
                || extraction.GetProperty("extraction_id").GetGuid() != input.ExtractionId
                || extraction.GetProperty("file_id").GetGuid() != input.FileId
                || extraction.GetProperty("measurements").GetArrayLength() > 200
                || extraction.GetProperty("diagnostics").GetArrayLength() > 20)
                throw new FormatException();
            var json = extraction.GetRawText();
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 512 * 1024)
                throw new FormatException();
            return new SurveyExtractionOutput(outcome, method, json);
        }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new DependencyFailureException(PythonServiceClient.DependencyName, DependencyFailureKind.MalformedResponse);
        }
    }
}
