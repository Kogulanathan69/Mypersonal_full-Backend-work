using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildMind.Application.Abstractions.Regulations;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// M7 constraint evidence packs (constraints contract v0, conditionally approved by M6 on 2026-11-19) on the same
/// hardened transport. Every pack is verified on receipt, independently of the service that produced it:
/// <list type="bullet">
/// <item>Integrity: <c>evidence_pack_id</c> is recomputed as SHA-256 over BuildMind canonical JSON v1 (BM-JCS-1) of
/// the received pack without that field; any mismatch means tampering or a serialization disagreement.</item>
/// <item>Binding: exactly the requested revisions, jurisdiction, building use and kinds (same order).</item>
/// <item>States: VERIFIED needs a human-reviewed value and no overlapping reviewed disagreement; CONFLICTING needs a
/// real overlapping disagreement; UNVERIFIED has only unreviewed values; MISSING has none; REJECTED never appears.</item>
/// <item>Values: kind-specific comparator, unit and plausible range; condition fields with their unit and range.</item>
/// <item>Review identity: pseudonymous reviewer id and review reference on every reviewed value, review date not
/// in the future; synthetic identities only in synthetic packs and never in real ones. Reviewer AUTHORIZATION
/// (registry membership, revocation, role, jurisdiction/kind scope, period) is enforced when rag-service loads the
/// table; the pack carries the authorizing registry's version and digest, which are checked here.</item>
/// <item>Synthetic packs are rejected unless explicitly allowed (Development/Testing only).</item>
/// </list>
/// Any violation is a <c>MalformedResponse</c>, never a partially trusted pack. M6 remains the only owner of
/// feasibility decisions; this client returns evidence, never pass/fail.
/// </summary>
public sealed partial class RagServiceClient : IRegulatoryConstraintSource
{
    internal const string ConstraintsPath = "internal/v1/regulations/constraints";
    internal const string ConstraintsContractVersion = "constraints-0";
    internal const string SyntheticReviewerPrefix = "synthetic:";
    internal const string SyntheticReferencePrefix = "SYN-";
    internal const string SyntheticTablePrefix = "synthetic-";

    private static readonly Dictionary<RegulatoryConstraintKind, (string Wire, string Comparator, string Unit, double Min, double Max)> KindRules = new()
    {
        [RegulatoryConstraintKind.RearSpaceMin] = ("REAR_SPACE_MIN", ">=", "m", 0, 100),
        [RegulatoryConstraintKind.SideSpaceMin] = ("SIDE_SPACE_MIN", ">=", "m", 0, 100),
        [RegulatoryConstraintKind.FrontSetbackMin] = ("FRONT_SETBACK_MIN", ">=", "m", 0, 100),
        [RegulatoryConstraintKind.BuildingHeightMax] = ("BUILDING_HEIGHT_MAX", "<=", "m", 0, 1000),
        [RegulatoryConstraintKind.PlotCoverageMax] = ("PLOT_COVERAGE_MAX", "<=", "percent", 0, 100),
        [RegulatoryConstraintKind.FloorAreaRatioMax] = ("FLOOR_AREA_RATIO_MAX", "<=", "ratio", 0, 100),
        [RegulatoryConstraintKind.ParkingSpacesMin] = ("PARKING_SPACES_MIN", ">=", "count", 0, 1000),
    };

    private static readonly Dictionary<string, (string Unit, double Min, double Max)> ConditionRules = new(StringComparer.Ordinal)
    {
        ["building_height_m"] = ("m", 0, 1000),
        ["plot_area_m2"] = ("m2", 0, 1_000_000),
        ["floors"] = ("count", 0, 200),
        ["road_width_m"] = ("m", 0, 200),
    };

    private static readonly Dictionary<string, ConstraintKindStatus> KindStatuses = new(StringComparer.Ordinal)
    {
        ["VERIFIED"] = ConstraintKindStatus.Verified,
        ["CONFLICTING"] = ConstraintKindStatus.Conflicting,
        ["UNVERIFIED"] = ConstraintKindStatus.Unverified,
        ["MISSING"] = ConstraintKindStatus.Missing,
    };

    private static readonly HashSet<string> ConditionComparators = ["<", "<=", ">", ">=", "=="];

    public async Task<RegulatoryConstraintPack> GetConstraintsAsync(RegulatoryConstraintQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        var request = new WireConstraintsRequest(
            query.Jurisdiction,
            [.. query.Kinds.Select(k => KindRules[k].Wire)],
            query.ResidentialOnly ? "residential" : null,
            WireBinding.From(query.Binding));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wire = await PostJsonVerifiedAsync<WireConstraintsRequest, WireConstraintPack>(
                ConstraintsPath,
                request,
                (pack, raw) => IsValidPack(pack, request, today) && IsAuthentic(pack, raw) && (allowSyntheticConstraints || pack.Synthetic is false),
                cancellationToken)
            .ConfigureAwait(false);

        return new RegulatoryConstraintPack(
            wire.EvidencePackId!,
            wire.TableVersion!,
            wire.TableSha256!,
            wire.ReviewerRegistryVersion!,
            wire.ReviewerRegistrySha256!,
            wire.Synthetic!.Value,
            wire.Jurisdiction!,
            query.Binding,
            [.. wire.Kinds!.Select(k => k!).Select(k => new ConstraintKindResult(
                KindRules.Single(r => r.Value.Wire == k.Kind).Key,
                KindStatuses[k.Status!],
                [.. k.Constraints!.Select(c => c!).Select(c => new RegulatoryConstraint(
                    c.ConstraintId!, c.Comparator!, c.Value!.Value, c.Unit!, c.AppliesTo!,
                    [.. c.Conditions!.Select(x => x!).Select(x => new ConstraintCondition(x.Field!, x.Comparator!, x.Value!.Value, x.Unit!))],
                    c.ReviewStatus!, c.ReviewerRole, c.ReviewerId, c.ReviewReference, c.ReviewedAt,
                    new ConstraintSource(c.Source!.DocumentId!, c.Source.Page, c.Source.StartChar, c.Source.EndChar, c.Source.Excerpt!)))]))],
            wire.OverallStatus == "COMPLETE",
            [.. wire.Warnings!.Select(w => w!)]); // every element proven non-null by IsValidPack
    }

    private static void ValidateQuery(RegulatoryConstraintQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Jurisdiction) || query.Jurisdiction.Length > 200)
        {
            throw new ArgumentException("Jurisdiction must contain 1 to 200 characters.", nameof(query));
        }

        if (query.Kinds is null || query.Kinds.Count is < 1 || query.Kinds.Count > KindRules.Count
            || query.Kinds.Distinct().Count() != query.Kinds.Count || query.Kinds.Any(k => !KindRules.ContainsKey(k)))
        {
            throw new ArgumentException("Kinds must be 1 to 7 distinct known constraint kinds.", nameof(query));
        }

        if (query.Binding is null || query.Binding.ProjectRevision is < 1 or > ConstraintBinding.MaxProjectRevision
            || query.Binding.ProjectId == Guid.Empty
            || query.Binding.LandRevisionId == Guid.Empty || query.Binding.RequirementRevisionId == Guid.Empty)
        {
            throw new ArgumentException("The binding must identify exact, non-empty project, land and requirement revisions.", nameof(query));
        }
    }

    /// <summary>Independent integrity check: the digest is recomputed from the received bytes, never trusted.</summary>
    internal static bool IsAuthentic(WireConstraintPack pack, JsonElement raw) =>
        pack.DigestAlgorithm == CanonicalJson.Algorithm
        && CanonicalJson.TrySha256(raw, excludeRootMember: "evidence_pack_id") is { } recomputed
        && string.Equals(recomputed, pack.EvidencePackId, StringComparison.Ordinal);

    internal static bool IsValidPack(WireConstraintPack p, WireConstraintsRequest request, DateOnly today)
    {
        if (p.ContractVersion != ConstraintsContractVersion || p.LegalStatusVerified is not false || p.Synthetic is not { } synthetic
            || p.EvidencePackId is null || !DocumentIdPattern().IsMatch(p.EvidencePackId)
            || p.TableSha256 is null || !DocumentIdPattern().IsMatch(p.TableSha256)
            || p.TableVersion is null || !TableVersionPattern().IsMatch(p.TableVersion)
            || p.TableVersion.StartsWith(SyntheticTablePrefix, StringComparison.Ordinal) != synthetic
            || p.ReviewerRegistrySha256 is null || !DocumentIdPattern().IsMatch(p.ReviewerRegistrySha256)
            || p.ReviewerRegistryVersion is null || !TableVersionPattern().IsMatch(p.ReviewerRegistryVersion)
            || p.ReviewerRegistryVersion.StartsWith(SyntheticTablePrefix, StringComparison.Ordinal) != synthetic
            || p.Jurisdiction != request.Jurisdiction
            || p.BuildingUse != request.BuildingUse || p.Binding != request.Binding
            || p.Warnings is null || p.Warnings.Any(w => w is null)
            || p.Kinds is null || !p.Kinds.Select(k => k?.Kind).SequenceEqual(request.Kinds)
            || p.OverallStatus is not ("COMPLETE" or "INCOMPLETE"))
        {
            return false;
        }

        foreach (var k in p.Kinds)
        {
            if (k!.Status is null || !KindStatuses.TryGetValue(k.Status, out var status) || k.Constraints is null)
            {
                return false;
            }

            var rule = KindRules.Single(r => r.Value.Wire == k.Kind).Value;
            if (!k.Constraints.All(c => IsValidConstraint(c, rule, synthetic, today))
                || k.Constraints.Select(c => c!.ConstraintId).Distinct(StringComparer.Ordinal).Count() != k.Constraints.Count)
            {
                return false;
            }

            var reviewed = k.Constraints.Where(c => c!.ReviewStatus == "HUMAN_REVIEWED").Select(c => c!).ToList();
            var conflict = HasOverlappingDisagreement(reviewed);
            var consistent = status switch
            {
                ConstraintKindStatus.Verified => reviewed.Count >= 1 && !conflict,
                ConstraintKindStatus.Conflicting => conflict,
                ConstraintKindStatus.Unverified => reviewed.Count == 0 && k.Constraints.Count >= 1,
                ConstraintKindStatus.Missing => k.Constraints.Count == 0,
                _ => false,
            };
            if (!consistent)
            {
                return false;
            }
        }

        var complete = p.Kinds.All(k => k!.Status == "VERIFIED");
        return (p.OverallStatus == "COMPLETE") == complete;
    }

    private static bool IsValidConstraint(WireConstraint? c, (string Wire, string Comparator, string Unit, double Min, double Max) rule, bool synthetic, DateOnly today) =>
        c is not null
        && c.ConstraintId is not null && ConstraintIdPattern().IsMatch(c.ConstraintId)
        && c.Kind == rule.Wire
        && c.Comparator == rule.Comparator && c.Unit == rule.Unit
        && c.Value is { } value && double.IsFinite(value) && value >= rule.Min && value <= rule.Max
        && c.AppliesTo is "residential" or "any"
        && c.Conditions is not null && c.Conditions.Count <= 5 && c.Conditions.All(IsValidCondition) && Region(c.Conditions!) is not null
        && IsValidReview(c, synthetic, today)
        && c.Source is { } s && s.DocumentId is not null && DocumentIdPattern().IsMatch(s.DocumentId)
        && s.Page >= 1 && s.StartChar >= 0 && s.EndChar > s.StartChar && !string.IsNullOrEmpty(s.Excerpt);

    private static bool IsValidCondition(WireCondition? x) =>
        x is { Field: { } field, Comparator: { } comparator, Value: { } v, Unit: { } unit }
        && ConditionRules.TryGetValue(field, out var rule) && unit == rule.Unit
        && ConditionComparators.Contains(comparator) && double.IsFinite(v) && v >= rule.Min && v <= rule.Max;

    private static bool IsValidReview(WireConstraint c, bool synthetic, DateOnly today)
    {
        if (c.ReviewStatus == "UNREVIEWED")
        {
            return c.ReviewerRole is null && c.ReviewerId is null && c.ReviewReference is null && c.ReviewedAt is null;
        }

        if (c.ReviewStatus != "HUMAN_REVIEWED" // REJECTED must never be returned; nothing else exists
            || string.IsNullOrWhiteSpace(c.ReviewerRole) || c.ReviewedAt is not { } reviewedAt || reviewedAt > today
            || c.ReviewerId is null || !ReviewerIdPattern().IsMatch(c.ReviewerId)
            || c.ReviewReference is null || !ReviewReferencePattern().IsMatch(c.ReviewReference))
        {
            return false;
        }

        var syntheticIdentity = c.ReviewerId.StartsWith(SyntheticReviewerPrefix, StringComparison.Ordinal);
        var syntheticReference = c.ReviewReference.StartsWith(SyntheticReferencePrefix, StringComparison.Ordinal);
        return synthetic ? syntheticIdentity && syntheticReference : !syntheticIdentity && !syntheticReference;
    }

    // ---- applicability (shared semantics with rag-service/app/constraints.py; no precedence is ever assumed) -------

    private readonly record struct Interval(double Low, bool LowInclusive, double High, bool HighInclusive);

    private static readonly Interval Unbounded = new(double.NegativeInfinity, false, double.PositiveInfinity, false);

    private static Interval ToInterval(WireCondition c) => c.Comparator switch
    {
        ">=" => new(c.Value!.Value, true, double.PositiveInfinity, false),
        ">" => new(c.Value!.Value, false, double.PositiveInfinity, false),
        "<=" => new(double.NegativeInfinity, false, c.Value!.Value, true),
        "<" => new(double.NegativeInfinity, false, c.Value!.Value, false),
        _ => new(c.Value!.Value, true, c.Value!.Value, true),
    };

    private static Interval? Intersect(Interval a, Interval b)
    {
        var (low, lowIn) = a.Low > b.Low ? (a.Low, a.LowInclusive) : b.Low > a.Low ? (b.Low, b.LowInclusive) : (a.Low, a.LowInclusive && b.LowInclusive);
        var (high, highIn) = a.High < b.High ? (a.High, a.HighInclusive) : b.High < a.High ? (b.High, b.HighInclusive) : (a.High, a.HighInclusive && b.HighInclusive);
        return low > high || (low == high && !(lowIn && highIn)) ? null : new Interval(low, lowIn, high, highIn);
    }

    private static Dictionary<string, Interval>? Region(IEnumerable<WireCondition?> conditions)
    {
        var region = new Dictionary<string, Interval>(StringComparer.Ordinal);
        foreach (var c in conditions)
        {
            var merged = Intersect(region.GetValueOrDefault(c!.Field!, Unbounded), ToInterval(c));
            if (merged is null)
            {
                return null;
            }

            region[c.Field!] = merged.Value;
        }

        return region;
    }

    private static bool Overlap(WireConstraint a, WireConstraint b)
    {
        var ra = Region(a.Conditions!)!;
        var rb = Region(b.Conditions!)!;
        return ra.Keys.Union(rb.Keys).All(f => Intersect(ra.GetValueOrDefault(f, Unbounded), rb.GetValueOrDefault(f, Unbounded)) is not null);
    }

    private static bool HasOverlappingDisagreement(List<WireConstraint> reviewed)
    {
        for (var i = 0; i < reviewed.Count; i++)
        {
            for (var j = i + 1; j < reviewed.Count; j++)
            {
                var (a, b) = (reviewed[i], reviewed[j]);
                var differ = a.Comparator != b.Comparator || a.Value != b.Value || a.Unit != b.Unit;
                if (differ && Overlap(a, b))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TableVersionPattern();

    [GeneratedRegex("^(synthetic:)?[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReviewerIdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}-[0-9]{4}-[0-9]{3,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReviewReferencePattern();

    internal sealed record WireConstraintsRequest(
        [property: JsonPropertyName("jurisdiction")] string Jurisdiction,
        [property: JsonPropertyName("kinds")] IReadOnlyList<string> Kinds,
        [property: JsonPropertyName("building_use"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BuildingUse,
        [property: JsonPropertyName("binding")] WireBinding Binding);

    internal sealed record WireBinding(
        [property: JsonPropertyName("project_id")] Guid ProjectId,
        [property: JsonPropertyName("project_revision")] long ProjectRevision,
        [property: JsonPropertyName("land_revision_id")] Guid LandRevisionId,
        [property: JsonPropertyName("requirement_revision_id")] Guid RequirementRevisionId)
    {
        public static WireBinding From(ConstraintBinding b) => new(b.ProjectId, b.ProjectRevision, b.LandRevisionId, b.RequirementRevisionId);
    }

    internal sealed record WireConstraintPack(
        [property: JsonPropertyName("contract_version")] string? ContractVersion,
        [property: JsonPropertyName("digest_algorithm")] string? DigestAlgorithm,
        [property: JsonPropertyName("table_version")] string? TableVersion,
        [property: JsonPropertyName("table_sha256")] string? TableSha256,
        [property: JsonPropertyName("reviewer_registry_version")] string? ReviewerRegistryVersion,
        [property: JsonPropertyName("reviewer_registry_sha256")] string? ReviewerRegistrySha256,
        [property: JsonPropertyName("synthetic")] bool? Synthetic,
        [property: JsonPropertyName("jurisdiction")] string? Jurisdiction,
        [property: JsonPropertyName("building_use")] string? BuildingUse,
        [property: JsonPropertyName("binding")] WireBinding? Binding,
        [property: JsonPropertyName("kinds")] List<WireKindResult?>? Kinds,
        [property: JsonPropertyName("overall_status")] string? OverallStatus,
        [property: JsonPropertyName("legal_status_verified")] bool? LegalStatusVerified,
        [property: JsonPropertyName("evidence_pack_id")] string? EvidencePackId,
        [property: JsonPropertyName("warnings")] List<string?>? Warnings);

    internal sealed record WireKindResult(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("constraints")] List<WireConstraint?>? Constraints);

    internal sealed record WireConstraint(
        [property: JsonPropertyName("constraint_id")] string? ConstraintId,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("comparator")] string? Comparator,
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("unit")] string? Unit,
        [property: JsonPropertyName("applies_to")] string? AppliesTo,
        [property: JsonPropertyName("conditions")] List<WireCondition?>? Conditions,
        [property: JsonPropertyName("review_status")] string? ReviewStatus,
        [property: JsonPropertyName("reviewer_role")] string? ReviewerRole,
        [property: JsonPropertyName("reviewer_id")] string? ReviewerId,
        [property: JsonPropertyName("review_reference")] string? ReviewReference,
        [property: JsonPropertyName("reviewed_at")] DateOnly? ReviewedAt,
        [property: JsonPropertyName("source")] WireSource? Source);

    internal sealed record WireCondition(
        [property: JsonPropertyName("field")] string? Field,
        [property: JsonPropertyName("comparator")] string? Comparator,
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("unit")] string? Unit);

    internal sealed record WireSource(
        [property: JsonPropertyName("document_id")] string? DocumentId,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("start_char")] int StartChar,
        [property: JsonPropertyName("end_char")] int EndChar,
        [property: JsonPropertyName("excerpt")] string? Excerpt);
}
