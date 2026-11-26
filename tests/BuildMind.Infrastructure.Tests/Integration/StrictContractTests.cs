using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

/// <summary>
/// M6-review corrections (M4 + M7): strict raw-JSON validation before deserialization, duplicate-property rejection
/// on API input, token transport enforcement, and the human-reviewed constraint client (constraints contract v0)
/// validated against the SAME example the Python provider generates (rag-service/contract/constraints_response.v0.example.json).
/// </summary>
public sealed class StrictContractTests
{
    private const string Base = "http://rag.internal:8100/";
    private const string Health = "{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}";
    private static readonly string Example = File.ReadAllText(
        Path.Combine(RepositoryPaths.Root, "rag-service", "contract", "constraints_response.v0.example.json"));

    private static readonly ConstraintBinding Binding = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), 3,
        Guid.Parse("22222222-2222-4222-8222-222222222222"),
        Guid.Parse("33333333-3333-4333-8333-333333333333"));

    private static readonly RegulatoryConstraintQuery ExampleQuery = new(
        "Synthetic North Council planning area",
        [RegulatoryConstraintKind.RearSpaceMin, RegulatoryConstraintKind.SideSpaceMin, RegulatoryConstraintKind.FloorAreaRatioMax],
        Binding);

    /// <param name="allowSynthetic">The shared example is SYNTHETIC; only an explicitly isolated test client may accept it.</param>
    private static RagServiceClient Client(StubHttpHandler handler, int timeoutSeconds = 5, bool allowSynthetic = true) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions
            {
                Rag = new InternalServiceOptions { Enabled = true, BaseUrl = Base, TimeoutSeconds = timeoutSeconds, AllowSyntheticConstraints = allowSynthetic },
            }),
            new InternalServiceBulkhead());

    /// <summary>
    /// Applies a change and, by default, RESEALS the pack (recomputes evidence_pack_id with BM-JCS-1), so the
    /// structural rule under test, not the digest check, is what must reject it.
    /// </summary>
    private static string Tamper(Action<JsonObject> change, bool reseal = true)
    {
        var node = JsonNode.Parse(Example)!.AsObject();
        change(node);
        if (reseal)
        {
            Reseal(node);
        }

        return node.ToJsonString();
    }

    internal static void Reseal(JsonObject node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        node["evidence_pack_id"] = CanonicalJson.TrySha256(document.RootElement, excludeRootMember: "evidence_pack_id")
            ?? throw new InvalidOperationException("not canonicalizable");
    }

    private static JsonObject Kind(JsonObject pack, int index) => pack["kinds"]![index]!.AsObject();

    // ---- strict raw JSON (all internal responses) ------------------------------------------------------------------

    [Theory]
    [InlineData("{\"service\":\"buildmind-rag\",\"service\":\"x\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}")] // duplicate
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true,}")]                // trailing comma
    [InlineData("{/*c*/\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}")]            // comment
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true,\"admin\":true}")]  // unknown member
    [InlineData("[{\"service\":\"buildmind-rag\"}]")]                                                                                      // non-object root
    public async Task Internal_responses_must_be_strict_json(string body)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(body)).CheckHealthAsync(CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public void Raw_validation_detects_nested_duplicates()
    {
        Assert.True(InternalServiceClient.IsStrictJsonObject("{\"a\":{\"b\":1,\"c\":2}}"u8));
        Assert.False(InternalServiceClient.IsStrictJsonObject("{\"a\":{\"b\":1,\"b\":2}}"u8));
        Assert.False(InternalServiceClient.IsStrictJsonObject("{\"a\":1"u8));
    }

    [Fact]
    public async Task Api_rejects_duplicate_json_properties_as_malformed()
    {
        await using var factory = new BuildMindApiFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"name\":\"a\",\"name\":\"b\",\"count\":1}", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(SyntheticTestEndpoints.Prefix + "/json", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- token transport enforcement -------------------------------------------------------------------------------

    [Theory]
    [InlineData("http://rag.internal:8100", false, true)]   // plaintext to a network host with a token: refused
    [InlineData("https://rag.internal:8100", false, false)] // TLS: allowed
    [InlineData("http://127.0.0.1:8100", false, false)]     // loopback: allowed
    [InlineData("http://rag.internal:8100", true, false)]   // explicit, reviewable private-network exception
    public void Tokens_never_travel_in_cleartext_over_a_network_outside_development(string url, bool allowPlaintext, bool fails)
    {
        var options = new ServicesOptions
        {
            Rag = new InternalServiceOptions { Enabled = true, BaseUrl = url, AuthToken = "synthetic-internal-token-0123456789abcdef", AllowPlaintextPrivateNetwork = allowPlaintext },
        };

        var failures = options.Validate(requireRagAuthToken: true, enforceSecureTransport: true);

        Assert.Equal(fails, failures.Any(f => f.Contains("must use https", StringComparison.Ordinal)));
        Assert.Empty(options.Validate(requireRagAuthToken: false, enforceSecureTransport: false)); // Development
    }

    // ---- constraint client (constraints contract v0) -------------------------------------------------------------

    [Fact]
    public async Task Shared_example_maps_to_a_bound_pack_with_explicit_states()
    {
        var pack = await Client(StubHttpHandler.Json(Example)).GetConstraintsAsync(ExampleQuery, CancellationToken.None);

        Assert.Equal(Binding, pack.Binding);
        Assert.False(pack.Complete);
        Assert.True(pack.Synthetic);
        Assert.Equal(
            [ConstraintKindStatus.Verified, ConstraintKindStatus.Unverified, ConstraintKindStatus.Missing],
            pack.Kinds.Select(k => k.Status));
        var rear = Assert.Single(pack.Kinds[0].Constraints);
        Assert.Equal((">=", 3.0, "m", "HUMAN_REVIEWED"), (rear.Comparator, rear.Value, rear.Unit, rear.ReviewStatus));
        Assert.Equal(64, pack.EvidencePackId.Length);
        Assert.Equal(("synthetic:reviewer-north", "SYN-2026-001"), (rear.ReviewerId, rear.ReviewReference));
        Assert.Equal("synthetic-reviewers-0.1", pack.ReviewerRegistryVersion);
        Assert.Equal(64, pack.TableSha256.Length);
        Assert.Equal(64, pack.ReviewerRegistrySha256.Length);
    }

    [Fact]
    public async Task Request_carries_the_exact_binding_in_snake_case()
    {
        var handler = StubHttpHandler.Json(Example);

        await Client(handler).GetConstraintsAsync(ExampleQuery, CancellationToken.None);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies)!)!;
        Assert.Equal("33333333-3333-4333-8333-333333333333", (string?)body["binding"]!["requirement_revision_id"]);
        Assert.Equal(3, (int)body["binding"]!["project_revision"]!);
        Assert.Equal("REAR_SPACE_MIN", (string?)body["kinds"]![0]);
        Assert.Null(body["building_use"]); // omitted, not null
    }

    public static TheoryData<string> TamperedPacks => new()
    {
        Tamper(p => p["binding"]!["project_revision"] = 4),                                         // bound to another revision
        Tamper(p => p["binding"]!["land_revision_id"] = "44444444-4444-4444-8444-444444444444"),
        Tamper(p => p["jurisdiction"] = "Synthetic South Council planning area"),
        Tamper(p => p["kinds"]!.AsArray().RemoveAt(2)),                                              // kinds differ from the request
        Tamper(p => p["legal_status_verified"] = true),
        Tamper(p => p["contract_version"] = "constraints-1"),
        Tamper(p => p["overall_status"] = "COMPLETE"),                                                // inconsistent with kinds
        Tamper(p => Kind(p, 1)["status"] = "VERIFIED"),                                               // VERIFIED without a reviewed value
        Tamper(p => Kind(p, 2)["status"] = "VERIFIED"),                                               // VERIFIED with no value at all
        Tamper(p => Kind(p, 0)["constraints"]![0]!["review_status"] = "REJECTED"),                    // REJECTED must never appear
        Tamper(p => Kind(p, 0)["constraints"]![0]!["unit"] = "percent"),                              // unit/kind mismatch
        Tamper(p => Kind(p, 0)["constraints"]![0]!["comparator"] = "<="),                             // comparator/kind mismatch
        Tamper(p => Kind(p, 0)["constraints"]![0]!["source"]!["page"] = 0),
        Tamper(p => Kind(p, 0)["constraints"]![0]!["reviewer_role"] = null),                          // reviewed without reviewer
        Tamper(p => p["evidence_pack_id"] = "not-a-hash", reseal: false),
        Tamper(p => p["unexpected"] = "field"),                                                       // unknown member
        // constraint integrity corrections (M6 conditional approval)
        Tamper(p => p["digest_algorithm"] = "SHA-256/JSON"),
        Tamper(p => p["table_sha256"] = "not-a-hash"),
        Tamper(p => p["reviewer_registry_sha256"] = null),
        Tamper(p => p["reviewer_registry_version"] = "council-reviewers-1"),                          // real registry in a synthetic pack
        Tamper(p => p["table_version"] = "council-2026.1"),                                           // synthetic flag/version disagree
        Tamper(p => p["synthetic"] = false),                                                          // synthetic table relabelled as real
        Tamper(p => Kind(p, 0)["constraints"]![0]!["value"] = 101),                                   // outside the kind's range
        Tamper(p => Kind(p, 0)["constraints"]![0]!["value"] = -0.5),
        Tamper(p => Kind(p, 0)["constraints"]![0]!["kind"] = "SIDE_SPACE_MIN"),                       // constraint filed under another kind
        Tamper(p => Kind(p, 0)["constraints"]![0]!["reviewer_id"] = null),
        Tamper(p => Kind(p, 0)["constraints"]![0]!["reviewer_id"] = "Jane Perera"),                   // a personal name is never an id
        Tamper(p => Kind(p, 0)["constraints"]![0]!["reviewer_id"] = "reviewer-north"),                // real identity in a synthetic pack
        Tamper(p => Kind(p, 0)["constraints"]![0]!["review_reference"] = "BMR-2026-001"),             // real reference in a synthetic pack
        Tamper(p => Kind(p, 0)["constraints"]![0]!["review_reference"] = "review 1"),
        Tamper(p => Kind(p, 0)["constraints"]![0]!["reviewed_at"] = "2999-01-01"),                    // review in the future
        Tamper(p => Kind(p, 1)["constraints"]![0]!["reviewer_id"] = "synthetic:reviewer-north"),      // unreviewed with review details
        Tamper(p => Kind(p, 0)["constraints"]![0]!["conditions"] = JsonNode.Parse(
            "[{\"field\":\"floors\",\"comparator\":\">=\",\"value\":2,\"unit\":\"m\"}]")),           // condition unit mismatch
        Tamper(p => Kind(p, 0)["constraints"]![0]!["conditions"] = JsonNode.Parse(
            "[{\"field\":\"floors\",\"comparator\":\">=\",\"value\":500,\"unit\":\"count\"}]")),     // condition out of range
        Tamper(p => Kind(p, 0)["constraints"]![0]!["conditions"] = JsonNode.Parse(
            "[{\"field\":\"floors\",\"comparator\":\"<\",\"value\":2,\"unit\":\"count\"},{\"field\":\"floors\",\"comparator\":\">\",\"value\":3,\"unit\":\"count\"}]")), // unsatisfiable
        Tamper(p => HiddenConflict(p)),                                                               // VERIFIED hiding an overlapping disagreement
        Tamper(p => Kind(p, 0)["status"] = "CONFLICTING"),                                            // CONFLICTING without a real conflict
        Tamper(p => Kind(p, 0)["constraints"]!.AsArray().Add(Kind(p, 0)["constraints"]![0]!.DeepClone())), // duplicate constraint id
    };

    private static void HiddenConflict(JsonObject pack)
    {
        var clone = Kind(pack, 0)["constraints"]![0]!.DeepClone().AsObject();
        clone["constraint_id"] = "syn.north.rear-space-2";
        clone["value"] = 4;
        Kind(pack, 0)["constraints"]!.AsArray().Add(clone); // status stays VERIFIED
    }

    [Theory]
    [MemberData(nameof(TamperedPacks))]
    public async Task Packs_violating_the_contract_are_rejected(string json)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(json)).GetConstraintsAsync(ExampleQuery, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    public static TheoryData<RegulatoryConstraintQuery> InvalidQueries => new()
    {
        ExampleQuery with { Jurisdiction = " " },
        ExampleQuery with { Kinds = [] },
        ExampleQuery with { Kinds = [RegulatoryConstraintKind.RearSpaceMin, RegulatoryConstraintKind.RearSpaceMin] },
        ExampleQuery with { Binding = Binding with { ProjectRevision = 0 } },
        ExampleQuery with { Binding = Binding with { ProjectRevision = ConstraintBinding.MaxProjectRevision + 1 } }, // outside BM-JCS-1
        ExampleQuery with { Binding = Binding with { ProjectRevision = long.MaxValue } },
        ExampleQuery with { Binding = Binding with { LandRevisionId = Guid.Empty } },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task Invalid_constraint_queries_are_rejected_before_any_request(RegulatoryConstraintQuery query)
    {
        var handler = StubHttpHandler.Json(Example);

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).GetConstraintsAsync(query, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Resealing_a_harmless_change_is_accepted_so_the_digest_is_genuinely_recomputed()
    {
        var resealed = Tamper(p => p["warnings"]!.AsArray().Add("extra warning"));

        var pack = await Client(StubHttpHandler.Json(resealed)).GetConstraintsAsync(ExampleQuery, CancellationToken.None);

        Assert.Contains("extra warning", pack.Warnings);
    }

    public static TheoryData<string> UnsealedTampering => new()
    {
        Tamper(p => p["warnings"]!.AsArray().Add("extra warning"), reseal: false),                    // any edit after sealing
        Tamper(p => Kind(p, 0)["constraints"]![0]!["value"] = 2.5, reseal: false),                     // a plausible, valid value
        Tamper(p => Kind(p, 0)["constraints"]![0]!["source"]!["excerpt"] = "A minimum rear space of two metres", reseal: false),
        Tamper(p => p["evidence_pack_id"] = new string('0', 64), reseal: false),
        Tamper(p => p.Remove("digest_algorithm"), reseal: false),
    };

    [Theory]
    [MemberData(nameof(UnsealedTampering))]
    public async Task Tampered_packs_fail_independent_digest_recomputation(string json)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(json)).GetConstraintsAsync(ExampleQuery, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Example_digest_matches_the_independent_csharp_recomputation()
    {
        using var document = JsonDocument.Parse(Example);

        Assert.Equal(
            document.RootElement.GetProperty("evidence_pack_id").GetString(),
            CanonicalJson.TrySha256(document.RootElement, excludeRootMember: "evidence_pack_id"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Synthetic_packs_are_rejected_unless_explicitly_allowed()
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(Example), allowSynthetic: false).GetConstraintsAsync(ExampleQuery, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Theory]
    [InlineData(3_000_000_000L)]                          // beyond Int32: Project.Revision is Int64 (M6 H4-R1 C08)
    [InlineData(ConstraintBinding.MaxProjectRevision)]
    public async Task Int64_project_revisions_are_bound_and_verified(long revision)
    {
        var resealed = Tamper(p => p["binding"]!["project_revision"] = revision);

        var pack = await Client(StubHttpHandler.Json(resealed))
            .GetConstraintsAsync(ExampleQuery with { Binding = Binding with { ProjectRevision = revision } }, CancellationToken.None);

        Assert.Equal(revision, pack.Binding.ProjectRevision);
    }

    public static TheoryData<string> LenientTyping => new()
    {
        Tamper(p => p["binding"]!["project_revision"] = "3"),                    // number as string (Web default would accept)
        Tamper(p => Kind(p, 0)["constraints"]![0]!["value"] = "3"),
        Tamper(p => Kind(p, 0)["constraints"]![0]!["source"]!["page"] = "1"),
        Tamper(p => { var v = p["overall_status"]; p.Remove("overall_status"); p["Overall_Status"] = v!.DeepClone(); }), // wrong case
        Tamper(p => { var v = p["synthetic"]; p.Remove("synthetic"); p["Synthetic"] = v!.DeepClone(); }),
    };

    [Theory]
    [MemberData(nameof(LenientTyping))]
    public async Task Internal_contracts_are_exact_case_and_strictly_typed(string json)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(json)).GetConstraintsAsync(ExampleQuery, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Theory]
    [InlineData("{\"Service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}")]
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":\"true\"}")]
    public async Task Health_contract_is_exact_case_and_strictly_typed(string body)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(body)).CheckHealthAsync(CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Unavailable_table_is_a_truthful_dependency_failure()
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json("{\"code\":\"CONSTRAINTS_UNAVAILABLE\"}", HttpStatusCode.ServiceUnavailable))
                .GetConstraintsAsync(ExampleQuery, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Unavailable, failure.Kind);
    }

    [Fact]
    public async Task Hanging_service_times_out_within_the_configured_bound()
    {
        var watch = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Hangs(), timeoutSeconds: 1).GetConstraintsAsync(ExampleQuery, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Timeout, failure.Kind);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_dependency_failure()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(StubHttpHandler.Hangs(), timeoutSeconds: 30).GetConstraintsAsync(ExampleQuery, cancel.Token));
    }

    [Fact]
    public void Constraint_port_resolves_to_the_hardened_rag_client()
    {
        using var factory = new BuildMindApiFactory("Production");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<RagServiceClient>(scope.ServiceProvider.GetRequiredService<IRegulatoryConstraintSource>());
    }
}
