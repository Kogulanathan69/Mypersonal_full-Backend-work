using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

/// <summary>
/// M4/M7 constraint integrity (M6 conditional approval, 2026-11-19): shared BM-JCS-1 digest vectors, the shared
/// synthetic setback scenarios end to end (client verification, evidence gate, M6's own contract validation), and the
/// Production/synthetic start-up rules. All data is SYNTHETIC.
/// </summary>
public sealed class ConstraintIntegrityTests
{
    private static readonly string ContractDirectory = Path.Combine(RepositoryPaths.Root, "rag-service", "contract");
    private static readonly JsonObject Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(ContractDirectory, "canonical_json_vectors.v1.json")))!.AsObject();
    private static readonly JsonObject Scenarios = JsonNode.Parse(File.ReadAllText(Path.Combine(ContractDirectory, "constraints_scenarios.v0.json")))!.AsObject();

    private static readonly RegulatoryConstraintKind[] Setbacks =
        [RegulatoryConstraintKind.FrontSetbackMin, RegulatoryConstraintKind.RearSpaceMin, RegulatoryConstraintKind.SideSpaceMin];

    // ---- shared canonical JSON vectors -----------------------------------------------------------------------------

    public static TheoryData<string> ValidVectors => [.. Vectors["valid"]!.AsArray().Select(v => v!["name"]!.GetValue<string>())];

    public static TheoryData<string> InvalidVectors => [.. Vectors["invalid"]!.AsArray().Select(v => v!["name"]!.GetValue<string>())];

    private static JsonNode Vector(string list, string name) => Vectors[list]!.AsArray().Single(v => v!["name"]!.GetValue<string>() == name)!;

    [Theory]
    [MemberData(nameof(ValidVectors))]
    public void Csharp_canonical_json_matches_the_shared_vectors(string name)
    {
        var vector = Vector("valid", name);
        using var document = JsonDocument.Parse(vector["json"]!.GetValue<string>());

        var canonical = CanonicalJson.TryCanonicalize(document.RootElement);

        Assert.Equal(vector["canonical"]!.GetValue<string>(), canonical);
        Assert.Equal(vector["sha256"]!.GetValue<string>(), CanonicalJson.TrySha256(document.RootElement));
        Assert.Equal(vector["sha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical!))));
    }

    [Theory]
    [MemberData(nameof(InvalidVectors))]
    public void Csharp_canonical_json_rejects_the_shared_invalid_vectors(string name)
    {
        // Lenient parse on purpose (duplicates allowed): the canonicalizer itself must reject these inputs.
        using var document = JsonDocument.Parse(
            Vector("invalid", name)["json"]!.GetValue<string>(), new JsonDocumentOptions { AllowDuplicateProperties = true });

        Assert.Null(CanonicalJson.TryCanonicalize(document.RootElement));
        Assert.Null(CanonicalJson.TrySha256(document.RootElement));
    }

    [Fact]
    public void Vectors_declare_the_shared_algorithm() => Assert.Equal(CanonicalJson.Algorithm, Vectors["algorithm"]!.GetValue<string>());

    // ---- shared M6 setback scenarios: client -> gate -> M6 contract validation ------------------------------------

    public static TheoryData<string> ScenarioNames => [.. Scenarios["scenarios"]!.AsArray().Select(s => s!["name"]!.GetValue<string>())];

    private static JsonNode Scenario(string name) => Scenarios["scenarios"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == name)!;

    private static RegulatoryConstraintQuery Query(JsonNode request) => new(
        request["jurisdiction"]!.GetValue<string>(),
        [.. request["kinds"]!.AsArray().Select(k => Kind(k!.GetValue<string>()))],
        new ConstraintBinding(
            Guid.Parse(request["binding"]!["project_id"]!.GetValue<string>()),
            request["binding"]!["project_revision"]!.GetValue<int>(),
            Guid.Parse(request["binding"]!["land_revision_id"]!.GetValue<string>()),
            Guid.Parse(request["binding"]!["requirement_revision_id"]!.GetValue<string>())),
        ResidentialOnly: request["building_use"]?.GetValue<string>() == "residential");

    private static RegulatoryConstraintKind Kind(string wire) => wire switch
    {
        "FRONT_SETBACK_MIN" => RegulatoryConstraintKind.FrontSetbackMin,
        "REAR_SPACE_MIN" => RegulatoryConstraintKind.RearSpaceMin,
        "SIDE_SPACE_MIN" => RegulatoryConstraintKind.SideSpaceMin,
        _ => throw new InvalidOperationException(wire),
    };

    private static async Task<RegulatoryConstraintPack> FetchAsync(JsonNode scenario, bool allowSynthetic = true) =>
        await new RagServiceClient(
                new HttpClient(StubHttpHandler.Json(scenario["response"]!.ToJsonString())),
                Options.Create(new ServicesOptions
                {
                    Rag = new InternalServiceOptions { Enabled = true, BaseUrl = "https://rag.internal:8100", AllowSyntheticConstraints = allowSynthetic },
                }),
                new InternalServiceBulkhead())
            .GetConstraintsAsync(Query(scenario["request"]!), CancellationToken.None);

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task Shared_scenarios_verify_and_map_to_the_expected_m6_evidence_status(string name)
    {
        var scenario = Scenario(name);
        var expected = scenario["expected"]!;

        var pack = await FetchAsync(scenario);
        var assessment = ConstraintEvidenceGate.Assess(pack, Setbacks, allowSynthetic: true);

        Assert.Equal(expected["overall_status"]!.GetValue<string>() == "COMPLETE", pack.Complete);
        Assert.Equal(expected["m6_regulatory_evidence_status"]!.GetValue<string>(), assessment.RegulatoryEvidenceStatus);
        Assert.Equal(expected["feasible_permitted"]!.GetValue<bool>(), assessment.PermitsFeasible);
        Assert.Equal(assessment.PermitsFeasible, assessment.Gaps.Count == 0);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task M6_contract_validation_refuses_feasible_unless_the_gate_reports_reviewed(string name)
    {
        var assessment = ConstraintEvidenceGate.Assess(await FetchAsync(Scenario(name)), Setbacks, allowSynthetic: true);
        var feasible = FeasibleResult(assessment);

        if (assessment.PermitsFeasible)
        {
            GenerationContractValidation.Validate(feasible); // positive control: the M6 contract accepts it
        }
        else
        {
            Assert.Throws<GenerationContractException>(() => GenerationContractValidation.Validate(feasible));
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task Gate_output_always_satisfies_the_m6_evidence_contract(string name)
    {
        var assessment = ConstraintEvidenceGate.Assess(await FetchAsync(Scenario(name)), Setbacks, allowSynthetic: true);
        var needsReview = Deserialize<FeasibilityResultDto>(FeasibilityJson("NEEDS_REVIEW", assessment, footprint: false,
            diagnostics: "[{\"code\":\"SETBACK_EVIDENCE_INCOMPLETE\",\"severity\":\"CRITICAL\",\"message\":\"Synthetic evidence gap.\",\"path\":null,\"relatedEntityIds\":[],\"sourceReferenceIds\":[]}]"));

        GenerationContractValidation.Validate(needsReview); // a truthful non-feasible outcome is always expressible
    }

    [Fact]
    public async Task Synthetic_evidence_is_rejected_by_default_and_never_reviewed_without_the_isolated_opt_in()
    {
        var verified = Scenario("all-setbacks-verified");

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() => FetchAsync(verified, allowSynthetic: false));
        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);

        var assessment = ConstraintEvidenceGate.Assess(await FetchAsync(verified), Setbacks); // gate default: no synthetic
        Assert.Equal(RegulatoryEvidenceStatuses.Unverified, assessment.RegulatoryEvidenceStatus);
        Assert.False(assessment.PermitsFeasible);
        Assert.Equal(["FRONT_SETBACK_MIN_SYNTHETIC", "REAR_SPACE_MIN_SYNTHETIC", "SIDE_SPACE_MIN_SYNTHETIC"], assessment.Gaps);
        Assert.Throws<GenerationContractException>(() => GenerationContractValidation.Validate(FeasibleResult(assessment)));
    }

    // ---- gate rules ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_pack_means_missing_for_every_mandatory_kind()
    {
        var assessment = ConstraintEvidenceGate.Assess(null, Setbacks);

        Assert.Equal((RegulatoryEvidenceStatuses.Missing, (string?)null), (assessment.RegulatoryEvidenceStatus, assessment.RuleSetVersion));
        Assert.Equal(["FRONT_SETBACK_MIN_MISSING", "REAR_SPACE_MIN_MISSING", "SIDE_SPACE_MIN_MISSING"], assessment.Gaps);
    }

    [Fact]
    public async Task A_mandatory_kind_absent_from_the_pack_is_missing_even_if_everything_returned_is_verified()
    {
        var pack = await FetchAsync(Scenario("all-setbacks-verified"));

        var assessment = ConstraintEvidenceGate.Assess(pack, [.. Setbacks, RegulatoryConstraintKind.BuildingHeightMax], allowSynthetic: true);

        Assert.Equal(RegulatoryEvidenceStatuses.Missing, assessment.RegulatoryEvidenceStatus);
        Assert.Equal(["BUILDING_HEIGHT_MAX_MISSING"], assessment.Gaps);
    }

    [Fact]
    public async Task Conflicting_takes_precedence_over_missing_and_unverified()
    {
        var pack = await FetchAsync(Scenario("rear-conflict-overlapping-conditions"));
        var withMissing = pack with { Kinds = [.. pack.Kinds.Where(k => k.Kind != RegulatoryConstraintKind.FrontSetbackMin)] };

        Assert.Equal(RegulatoryEvidenceStatuses.Conflicting, ConstraintEvidenceGate.Assess(withMissing, Setbacks, allowSynthetic: true).RegulatoryEvidenceStatus);
    }

    [Fact]
    public async Task Only_the_mandatory_kinds_decide_the_outcome()
    {
        var pack = await FetchAsync(Scenario("side-unverified-only"));

        var assessment = ConstraintEvidenceGate.Assess(pack, [RegulatoryConstraintKind.FrontSetbackMin, RegulatoryConstraintKind.RearSpaceMin], allowSynthetic: true);

        Assert.True(assessment.PermitsFeasible);
    }

    [Fact]
    public void Mandatory_kinds_must_be_explicit_and_distinct()
    {
        Assert.Throws<ArgumentException>(() => ConstraintEvidenceGate.Assess(null, []));
        Assert.Throws<ArgumentException>(() => ConstraintEvidenceGate.Assess(null, [RegulatoryConstraintKind.RearSpaceMin, RegulatoryConstraintKind.RearSpaceMin]));
        Assert.Throws<ArgumentException>(() => ConstraintEvidenceGate.Assess(null, [(RegulatoryConstraintKind)99]));
    }

    // ---- start-up rules -----------------------------------------------------------------------------------------------

    private const string Token = "synthetic-internal-token-0123456789abcdef";

    [Theory]
    [InlineData("https://rag.internal:8100", Token, false, false)]
    [InlineData("http://127.0.0.1:8100", Token, false, false)]       // loopback is not network transport
    [InlineData("http://rag.internal:8100", Token, false, true)]     // plaintext with a token
    [InlineData("http://rag.internal:8100", null, false, true)]      // plaintext without a token: still refused in Production
    [InlineData("http://rag.internal:8100", Token, true, true)]      // the exception flag does not apply in Production
    [InlineData("https://rag.internal:8100", Token, true, true)]     // and setting it is itself refused
    public void Production_requires_secure_transport_without_plaintext_exceptions(string url, string? token, bool allowPlaintext, bool fails)
    {
        var options = new ServicesOptions
        {
            Rag = new InternalServiceOptions { Enabled = true, BaseUrl = url, AuthToken = token, AllowPlaintextPrivateNetwork = allowPlaintext },
        };

        var failures = options.Validate(requireRagAuthToken: false, enforceSecureTransport: true, production: true, isolatedTestEnvironment: false);

        Assert.Equal(fails, failures.Count > 0);
    }

    [Fact]
    public void Staging_keeps_the_explicit_reviewable_private_network_exception()
    {
        var options = new ServicesOptions
        {
            Rag = new InternalServiceOptions { Enabled = true, BaseUrl = "http://rag.internal:8100", AuthToken = Token, AllowPlaintextPrivateNetwork = true },
        };

        Assert.Empty(options.Validate(requireRagAuthToken: true, enforceSecureTransport: true, production: false, isolatedTestEnvironment: false));
    }

    [Theory]
    [InlineData(true, false, true, false)]   // Rag synthetic in Development/Testing: allowed
    [InlineData(true, false, false, true)]   // Rag synthetic in Staging/Production: refused
    [InlineData(false, true, true, true)]    // Python never accepts synthetic constraints
    public void Synthetic_constraints_are_allowed_only_in_isolated_environments(bool rag, bool python, bool isolated, bool fails)
    {
        var options = new ServicesOptions
        {
            Rag = new InternalServiceOptions { AllowSyntheticConstraints = rag },
            Python = new InternalServiceOptions { AllowSyntheticConstraints = python },
        };

        var failures = options.Validate(isolatedTestEnvironment: isolated);

        Assert.Equal(fails, failures.Any(f => f.Contains("AllowSyntheticConstraints", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Production", "Services:Rag:AllowSyntheticConstraints", "true", "AllowSyntheticConstraints")]
    [InlineData("Staging", "Services:Rag:AllowSyntheticConstraints", "true", "AllowSyntheticConstraints")]
    [InlineData("Production", "Services:Rag:AllowPlaintextPrivateNetwork", "true", "not allowed in Production")]
    public async Task Host_start_up_fails_closed(string environment, string key, string value, string expected)
    {
        await using var factory = new BuildMindApiFactory(environment, new Dictionary<string, string?>
        {
            ["Services:Rag:Enabled"] = "true",
            ["Services:Rag:BaseUrl"] = "https://rag.internal:8100",
            ["Services:Rag:AuthToken"] = Token,
            [key] = value,
        });

        var exception = Record.Exception(() => factory.CreateClient());

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(exception, factory.Logs);
        Assert.Contains(validation.Failures, f => f.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_host_refuses_a_plaintext_network_python_service()
    {
        await using var factory = new BuildMindApiFactory("Production", new Dictionary<string, string?>
        {
            ["Services:Python:Enabled"] = "true",
            ["Services:Python:BaseUrl"] = "http://python-services.internal:8000",
        });

        var exception = Record.Exception(() => factory.CreateClient());

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(exception, factory.Logs);
        Assert.Contains(validation.Failures, f => f.Contains("Services:Python:BaseUrl must use https", StringComparison.Ordinal));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, GenerationContractJson.StrictOptions)!;

    private static FeasibilityResultDto FeasibleResult(ConstraintEvidenceAssessment assessment) =>
        Deserialize<FeasibilityResultDto>(FeasibilityJson("FEASIBLE", assessment, footprint: true, diagnostics: "[]"));

    private static string FeasibilityJson(string status, ConstraintEvidenceAssessment assessment, bool footprint, string diagnostics)
    {
        const string Land = "{\"schemaVersion\":\"metric-land/0.1-draft\",\"coordinateSystem\":{\"type\":\"localCartesian\",\"unit\":\"m\",\"origin\":{\"x\":0,\"y\":0},\"xDirection\":\"right\",\"yDirection\":\"up\"},\"boundary\":{\"exterior\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0},{\"x\":10,\"y\":8},{\"x\":0,\"y\":8},{\"x\":0,\"y\":0}],\"holes\":[]}}";
        var version = assessment.RuleSetVersion is null ? "null" : JsonSerializer.Serialize(assessment.RuleSetVersion);
        return "{\"projectId\":\"11111111-1111-4111-8111-111111111111\",\"landRevisionId\":\"22222222-2222-4222-8222-222222222222\","
            + "\"requirementsRevisionId\":\"33333333-3333-4333-8333-333333333333\",\"schemaVersion\":\"m6-feasibility-result/0.1-draft\","
            + $"\"status\":\"{status}\",\"buildableFootprint\":{(footprint ? Land : "null")},\"landAreaM2\":120,"
            + $"\"buildableAreaM2\":{(footprint ? "80" : "null")},\"requiredAreaM2\":{(footprint ? "60" : "null")},"
            + $"\"regulatoryEvidenceStatus\":\"{assessment.RegulatoryEvidenceStatus}\",\"ruleSetVersion\":{version},"
            + $"\"feasibilityPolicyVersion\":\"synthetic-policy-v1\",\"diagnostics\":{diagnostics}}}";
    }
}
