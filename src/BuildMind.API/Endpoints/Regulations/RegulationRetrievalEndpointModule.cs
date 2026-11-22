using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions.Regulations;

namespace BuildMind.API.Endpoints.Regulations;

/// <summary>
/// M7 public regulation evidence retrieval: <c>POST /api/v1/regulations/retrieve</c> (proposed contract, pending lead
/// approval and M1 authentication). Isolated: never registered by the production composition root.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Authenticated callers only; classified as an expensive operation (S4-B concurrency limits).</item>
/// <item>Returns cited source passages and an explicit evidence status. Never a generated answer, a legal-currency
/// claim or a compliance decision: <c>legalStatusVerified</c> and <c>answerGenerated</c> are always false.</item>
/// <item>Internal dependency failures propagate as <see cref="Application.Abstractions.Errors.DependencyFailureException"/>
/// and become 503 <c>DEPENDENCY_UNAVAILABLE</c> / 504 <c>DEPENDENCY_TIMEOUT</c> through the global handler.</item>
/// <item>Neither the query text nor the caller identity is logged or echoed in validation errors.</item>
/// </list>
/// </remarks>
public sealed partial class RegulationRetrievalEndpointModule : IApiEndpointModule
{
    public const string ContractVersion = "1";
    public const int MaxQueryLength = 500;
    public const int MaxJurisdictionLength = 200;
    public const int MaxDocumentIds = 3;
    public const int MaxResultsLimit = 10;
    public const int DefaultMaxResults = 5;

    public const string Disclaimer =
        "Retrieved text is source evidence only. It is not a legal, compliance or currency determination.";

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        apiV1.MapPost("/regulations/retrieve", RetrieveAsync)
            .WithTags("Regulations")
            .WithName("RetrieveRegulationEvidence")
            .RequireAuthorization()
            .RequireExpensiveOperationLimit();
    }

    private static async Task<IResult> RetrieveAsync(HttpContext context, IRegulationEvidenceSource source, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.HasJsonContentType())
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType);
        }

        RetrieveRegulationsHttpRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RetrieveRegulationsHttpRequest>(context.Request.Body, RequestJson, cancellationToken);
        }
        catch (JsonException)
        {
            return Invalid("request", "A valid JSON object with only the documented members is required.");
        }

        if (request is null)
        {
            return Invalid("request", "A request object is required.");
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ApiProblems.ValidationFailed(errors);
        }

        var evidence = await source.SearchAsync(
            new RegulationSearchQuery(
                request.Query!.Trim(),
                request.Jurisdiction,
                request.DocumentIds,
                request.MaxResults ?? DefaultMaxResults),
            cancellationToken);

        return Results.Ok(ToResponse(evidence));
    }

    /// <summary>Structural validation, identical to the internal client's rules so no valid request is refused there.</summary>
    internal static Dictionary<string, string[]> Validate(RetrieveRegulationsHttpRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length > MaxQueryLength)
        {
            errors["query"] = [$"Query must contain 1 to {MaxQueryLength} non-blank characters."];
        }

        if (request.Jurisdiction is { } jurisdiction
            && (string.IsNullOrWhiteSpace(jurisdiction) || jurisdiction.Length > MaxJurisdictionLength))
        {
            errors["jurisdiction"] = [$"Jurisdiction, when supplied, must contain 1 to {MaxJurisdictionLength} characters."];
        }

        if (request.DocumentIds is { } ids
            && (ids.Length is < 1 or > MaxDocumentIds || ids.Any(id => id is null || !DocumentIdPattern().IsMatch(id))
                || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length))
        {
            errors["documentIds"] = [$"Document identities, when supplied, must be 1 to {MaxDocumentIds} distinct approved identities (64 lowercase hex)."];
        }

        if (request.MaxResults is < 1 or > MaxResultsLimit)
        {
            errors["maxResults"] = [$"maxResults must be between 1 and {MaxResultsLimit}."];
        }

        return errors;
    }

    internal static RetrieveRegulationsHttpResponse ToResponse(RegulationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new RetrieveRegulationsHttpResponse(
            ContractVersion,
            StatusCode(evidence.Status),
            evidence.Passages.Select(p => new RegulationPassageHttpResponse(
                p.PassageId,
                p.DocumentId,
                p.SourceTitle,
                p.Jurisdiction,
                p.Page,
                p.StartChar,
                p.EndChar,
                p.Excerpt,
                p.Score,
                p.EvidenceStatus switch
                {
                    PassageEvidenceStatus.EvidenceCandidate => "EVIDENCE_CANDIDATE",
                    PassageEvidenceStatus.AmbiguousEvidence => "AMBIGUOUS_EVIDENCE",
                    _ => throw new InvalidOperationException("Unknown passage evidence status."),
                },
                p.Warnings)).ToArray(),
            evidence.Warnings,
            LegalStatusVerified: false,
            AnswerGenerated: false,
            Disclaimer);
    }

    internal static string StatusCode(RegulationEvidenceStatus status) => status switch
    {
        RegulationEvidenceStatus.EvidenceFound => "EVIDENCE_FOUND",
        RegulationEvidenceStatus.NoMatchingEvidence => "NO_MATCHING_EVIDENCE",
        RegulationEvidenceStatus.InsufficientJurisdictionEvidence => "INSUFFICIENT_JURISDICTION_EVIDENCE",
        RegulationEvidenceStatus.InsufficientSourceEvidence => "INSUFFICIENT_SOURCE_EVIDENCE",
        RegulationEvidenceStatus.AmbiguousEvidence => "AMBIGUOUS_EVIDENCE",
        _ => throw new InvalidOperationException("Unknown regulation evidence status."),
    };

    private static IResult Invalid(string field, string message) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult Problem(int status)
    {
        var code = ProblemDetailsDefaults.GetDefaultCode(status)!;
        return Results.Problem(statusCode: status, type: ProblemDetailsDefaults.GetTypeForCode(code),
            extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentIdPattern();
}

/// <summary>Public request (camelCase JSON). Unknown members are rejected.</summary>
public sealed record RetrieveRegulationsHttpRequest(
    string? Query,
    string? Jurisdiction = null,
    string[]? DocumentIds = null,
    int? MaxResults = null);

/// <summary>Public response. <c>LexicalScore</c> is BM25 relevance only, never legal confidence.</summary>
public sealed record RetrieveRegulationsHttpResponse(
    string ContractVersion,
    string Status,
    IReadOnlyList<RegulationPassageHttpResponse> Passages,
    IReadOnlyList<string> Warnings,
    bool LegalStatusVerified,
    bool AnswerGenerated,
    string Disclaimer);

/// <param name="Page">One-based PDF page position (not the printed page label).</param>
/// <param name="StartChar">Unicode code-point offset within the extracted page text.</param>
public sealed record RegulationPassageHttpResponse(
    string PassageId,
    string DocumentId,
    string SourceTitle,
    string? Jurisdiction,
    int Page,
    int StartChar,
    int EndChar,
    string Excerpt,
    double LexicalScore,
    string EvidenceStatus,
    IReadOnlyList<string> Warnings);

public static class RegulationRetrievalFeatureRegistration
{
    /// <summary>Explicit opt-in for the public endpoint. Default off; enabling it is a lead decision (contract approval).</summary>
    public const string EnabledKey = "Regulations:Retrieval:Enabled";

    private const string RagEnabledKey = "Services:Rag:Enabled";

    /// <summary>
    /// Production-capable registration path, fail closed and <b>off by default</b>. Registers the module only when
    /// <see cref="EnabledKey"/> is true; then start-up is refused unless end-user authentication is active (the
    /// endpoint requires it) and the internal RAG service is enabled (otherwise every call would be a 503).
    /// Authentication itself is currently Development-only, so this path cannot expose the endpoint elsewhere.
    /// </summary>
    /// <returns>True when the endpoint module was registered.</returns>
    public static bool AddRegulationRetrievalFeature(this IServiceCollection services, IConfiguration configuration, bool authenticationEnabled)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>(EnabledKey))
        {
            return false;
        }

        if (!authenticationEnabled)
        {
            throw new InvalidOperationException(
                $"{EnabledKey} requires end-user authentication (Authentication:Enabled); the endpoint is never exposed anonymously.");
        }

        if (!configuration.GetValue<bool>(RagEnabledKey))
        {
            throw new InvalidOperationException($"{EnabledKey} requires the internal RAG service ({RagEnabledKey}).");
        }

        RegisterModuleOnce(services);
        return true;
    }

    /// <summary>
    /// Fail-closed opt-in for Testing hosts only. Production registration requires lead approval of the public contract
    /// and M1 authentication in the composition root (an endpoint with authorization metadata needs that middleware).
    /// </summary>
    public static IServiceCollection AddIsolatedRegulationRetrievalFeature(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Regulation retrieval registration is restricted to isolated Testing hosts pending contract approval and M1 authentication.");
        }

        RegisterModuleOnce(services);
        return services;
    }

    /// <summary>
    /// Fail closed on double registration: two copies of the module would map the same route twice and every request
    /// would fail with an ambiguous match. A second registration is a composition error, so it is refused loudly at
    /// start-up instead of being silently ignored.
    /// </summary>
    private static void RegisterModuleOnce(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IApiEndpointModule)
                && (descriptor.ImplementationType == typeof(RegulationRetrievalEndpointModule)
                    || descriptor.ImplementationInstance is RegulationRetrievalEndpointModule)))
        {
            throw new InvalidOperationException(
                "The regulation retrieval endpoint module is already registered; use exactly one registration path.");
        }

        services.AddSingleton<IApiEndpointModule, RegulationRetrievalEndpointModule>();
    }
}
