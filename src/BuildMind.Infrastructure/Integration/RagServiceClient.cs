using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// M7 typed client for the private regulation retrieval service (rag-service, evidence contract v1) on the hardened
/// shared transport (BE-M4-D05): allowlisted path, own deadline, bulkhead, size limit, no retries, traceparent.
/// Every response is validated against the contract before use; a response that claims verified legal status, a
/// generated answer, passages without evidence, or impossible citations is rejected as malformed.
/// </summary>
public sealed partial class RagServiceClient(HttpClient http, IOptions<ServicesOptions> options, InternalServiceBulkhead bulkhead)
    : InternalServiceClient(http, options.Value.Rag, DependencyName, DefaultMaxResponseBytes, bulkhead), IRegulationEvidenceSource
{
    public const string DependencyName = "rag";
    public const string ServiceIdentity = "buildmind-rag";
    internal const string SearchPath = "internal/v1/regulations/search";

    // Synthetic constraint packs are test data: accepted only where start-up validation allowed them (Development or
    // Testing environments), never in Staging or Production.
    private readonly bool allowSyntheticConstraints = options.Value.Rag.AllowSyntheticConstraints;

    public async Task<RagHealthResponse> CheckHealthAsync(CancellationToken cancellationToken) =>
        await GetJsonAsync<RagHealthResponse>(
            "health", h => h.Service == ServiceIdentity && h.Status == "ok" && h.ContractVersion == "1", cancellationToken)
            .ConfigureAwait(false);

    public async Task<RegulationEvidence> SearchAsync(RegulationSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        var wire = await PostJsonAsync<WireRequest, WireResponse>(
                SearchPath,
                new WireRequest(query.Query.Trim(), query.Jurisdiction, query.DocumentIds, query.MaxPassages),
                IsValid,
                cancellationToken)
            .ConfigureAwait(false);

        return new RegulationEvidence(
            ParseStatus(wire.Status!),
            [.. wire.Passages!.Select(p => new RegulationPassage(
                p.PassageId!, p.DocumentId!, p.SourceTitle!, p.Jurisdiction, p.Page, p.StartChar, p.EndChar,
                p.Excerpt!, p.Score, ParsePassageStatus(p.EvidenceStatus!), [.. p.Warnings!]))],
            [.. wire.Warnings!]);
    }

    private static void Validate(RegulationSearchQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Query) || query.Query.Trim().Length > 500)
        {
            throw new ArgumentException("Query must contain 1 to 500 characters.", nameof(query));
        }

        if (query.Jurisdiction is { } jurisdiction && (string.IsNullOrWhiteSpace(jurisdiction) || jurisdiction.Length > 200))
        {
            throw new ArgumentException("Jurisdiction must contain 1 to 200 characters.", nameof(query));
        }

        if (query.DocumentIds is { } ids && (ids.Count is < 1 or > 3 || ids.Any(id => id is null || !DocumentIdPattern().IsMatch(id))))
        {
            throw new ArgumentException("Document identities must be 1 to 3 approved SHA-256 identities.", nameof(query));
        }

        if (query.MaxPassages is < 1 or > 10)
        {
            throw new ArgumentException("MaxPassages must be between 1 and 10.", nameof(query));
        }
    }

    internal static bool IsValid(WireResponse r)
    {
        if (r.Status is null || !Statuses.ContainsKey(r.Status) || r.Passages is null || r.Warnings is null
            || r.LegalStatusVerified is not false || r.AnswerGenerated is not false || r.Warnings.Any(w => w is null))
        {
            return false;
        }

        var presentable = r.Status is "EVIDENCE_FOUND" or "AMBIGUOUS_EVIDENCE";
        if (!presentable && r.Passages.Count > 0)
        {
            return false; // passages are never presented without supporting evidence status
        }

        return r.Passages.All(p => p is not null
            && p.PassageId is { Length: > 0 }
            && p.DocumentId is not null && DocumentIdPattern().IsMatch(p.DocumentId)
            && p.SourceTitle is { Length: > 0 } && !p.SourceTitle.Contains('/') && !p.SourceTitle.Contains('\\')
            && p.Page >= 1 && p.StartChar >= 0 && p.EndChar > p.StartChar
            && p.Excerpt is { Length: > 0 }
            && double.IsFinite(p.Score)
            && p.EvidenceStatus is "EVIDENCE_CANDIDATE" or "AMBIGUOUS_EVIDENCE"
            && p.Warnings is not null && p.Warnings.All(w => w is not null));
    }

    private static readonly Dictionary<string, RegulationEvidenceStatus> Statuses = new(StringComparer.Ordinal)
    {
        ["EVIDENCE_FOUND"] = RegulationEvidenceStatus.EvidenceFound,
        ["NO_MATCHING_EVIDENCE"] = RegulationEvidenceStatus.NoMatchingEvidence,
        ["INSUFFICIENT_JURISDICTION_EVIDENCE"] = RegulationEvidenceStatus.InsufficientJurisdictionEvidence,
        ["INSUFFICIENT_SOURCE_EVIDENCE"] = RegulationEvidenceStatus.InsufficientSourceEvidence,
        ["AMBIGUOUS_EVIDENCE"] = RegulationEvidenceStatus.AmbiguousEvidence,
    };

    private static RegulationEvidenceStatus ParseStatus(string status) => Statuses[status];

    private static PassageEvidenceStatus ParsePassageStatus(string status) =>
        status == "EVIDENCE_CANDIDATE" ? PassageEvidenceStatus.EvidenceCandidate : PassageEvidenceStatus.AmbiguousEvidence;

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentIdPattern();

    public sealed record RagHealthResponse(
        [property: JsonPropertyName("service")] string? Service,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("contract_version")] string? ContractVersion,
        [property: JsonPropertyName("corpus_loaded")] bool CorpusLoaded);

    internal sealed record WireRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("jurisdiction"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Jurisdiction,
        [property: JsonPropertyName("document_ids"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? DocumentIds,
        [property: JsonPropertyName("k")] int K);

    internal sealed record WireResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("passages")] List<WirePassage>? Passages,
        [property: JsonPropertyName("warnings")] List<string>? Warnings,
        [property: JsonPropertyName("legal_status_verified")] bool? LegalStatusVerified,
        [property: JsonPropertyName("answer_generated")] bool? AnswerGenerated);

    internal sealed record WirePassage(
        [property: JsonPropertyName("passage_id")] string? PassageId,
        [property: JsonPropertyName("document_id")] string? DocumentId,
        [property: JsonPropertyName("source_title")] string? SourceTitle,
        [property: JsonPropertyName("jurisdiction")] string? Jurisdiction,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("start_char")] int StartChar,
        [property: JsonPropertyName("end_char")] int EndChar,
        [property: JsonPropertyName("excerpt")] string? Excerpt,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("evidence_status")] string? EvidenceStatus,
        [property: JsonPropertyName("warnings")] List<string>? Warnings);
}
