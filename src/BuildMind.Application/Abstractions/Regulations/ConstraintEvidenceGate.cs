using BuildMind.Application.Features.Generation.Contracts;

namespace BuildMind.Application.Abstractions.Regulations;

/// <summary>
/// Outcome of assessing a verified constraint evidence pack against the kinds a feasibility check needs, expressed
/// in M6's provisional regulatory-evidence vocabulary (<see cref="RegulatoryEvidenceStatuses"/>).
/// </summary>
/// <param name="RegulatoryEvidenceStatus">MISSING, UNVERIFIED, CONFLICTING or REVIEWED.</param>
/// <param name="RuleSetVersion">The table version for REVIEWED/UNVERIFIED/CONFLICTING; null for MISSING (M6 rule).</param>
/// <param name="Gaps">Gap codes (for example <c>FRONT_SETBACK_MIN_MISSING</c>); empty only when REVIEWED.</param>
public sealed record ConstraintEvidenceAssessment(string RegulatoryEvidenceStatus, string? RuleSetVersion, IReadOnlyList<string> Gaps)
{
    /// <summary>True only for REVIEWED: anything else must never yield a FEASIBLE or compliant result.</summary>
    public bool PermitsFeasible => RegulatoryEvidenceStatus == RegulatoryEvidenceStatuses.Reviewed;
}

/// <summary>
/// Evidence sufficiency gate between the M7 constraint pack and M6's deterministic feasibility (M4/M7, shared with
/// M6). It never decides feasibility: it only states whether the evidence for every MANDATORY kind is human-reviewed.
/// Precedence over the mandatory kinds is CONFLICTING &gt; MISSING &gt; UNVERIFIED &gt; REVIEWED, so missing, conflicting
/// or unverified mandatory evidence can never be reported as REVIEWED. Synthetic packs count as UNVERIFIED unless
/// explicitly allowed (isolated development/testing only). M6's contract validation independently refuses a
/// FEASIBLE result whose regulatory evidence status is not REVIEWED.
/// </summary>
public static class ConstraintEvidenceGate
{
    public static ConstraintEvidenceAssessment Assess(
        RegulatoryConstraintPack? pack, IReadOnlyCollection<RegulatoryConstraintKind> mandatoryKinds, bool allowSynthetic = false)
    {
        ArgumentNullException.ThrowIfNull(mandatoryKinds);
        if (mandatoryKinds.Count == 0 || mandatoryKinds.Distinct().Count() != mandatoryKinds.Count
            || mandatoryKinds.Any(k => !Enum.IsDefined(k)))
        {
            throw new ArgumentException("Mandatory kinds must be one or more distinct known constraint kinds.", nameof(mandatoryKinds));
        }

        if (pack is null)
        {
            return new(RegulatoryEvidenceStatuses.Missing, null, [.. mandatoryKinds.Select(k => Gap(k, "MISSING"))]);
        }

        var gaps = new List<string>();
        var statuses = new List<ConstraintKindStatus>();
        foreach (var kind in mandatoryKinds)
        {
            var results = pack.Kinds.Where(r => r.Kind == kind).ToList();
            var status = results.Count == 1 ? results[0].Status : ConstraintKindStatus.Missing; // absent or ambiguous
            if (status == ConstraintKindStatus.Verified && pack.Synthetic && !allowSynthetic)
            {
                status = ConstraintKindStatus.Unverified;
                gaps.Add(Gap(kind, "SYNTHETIC"));
            }
            else if (status != ConstraintKindStatus.Verified)
            {
                gaps.Add(Gap(kind, status.ToString().ToUpperInvariant()));
            }

            statuses.Add(status);
        }

        var overall = statuses.Contains(ConstraintKindStatus.Conflicting) ? RegulatoryEvidenceStatuses.Conflicting
            : statuses.Contains(ConstraintKindStatus.Missing) ? RegulatoryEvidenceStatuses.Missing
            : statuses.Contains(ConstraintKindStatus.Unverified) ? RegulatoryEvidenceStatuses.Unverified
            : statuses.All(s => s == ConstraintKindStatus.Verified) ? RegulatoryEvidenceStatuses.Reviewed
            : RegulatoryEvidenceStatuses.Unverified;

        return new(overall, overall == RegulatoryEvidenceStatuses.Missing ? null : pack.TableVersion, gaps);
    }

    private static string Gap(RegulatoryConstraintKind kind, string suffix) => $"{Wire(kind)}_{suffix}";

    private static string Wire(RegulatoryConstraintKind kind) => kind switch
    {
        RegulatoryConstraintKind.RearSpaceMin => "REAR_SPACE_MIN",
        RegulatoryConstraintKind.SideSpaceMin => "SIDE_SPACE_MIN",
        RegulatoryConstraintKind.FrontSetbackMin => "FRONT_SETBACK_MIN",
        RegulatoryConstraintKind.BuildingHeightMax => "BUILDING_HEIGHT_MAX",
        RegulatoryConstraintKind.PlotCoverageMax => "PLOT_COVERAGE_MAX",
        RegulatoryConstraintKind.FloorAreaRatioMax => "FLOOR_AREA_RATIO_MAX",
        RegulatoryConstraintKind.ParkingSpacesMin => "PARKING_SPACES_MIN",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
