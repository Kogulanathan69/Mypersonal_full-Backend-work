using BuildMind.Application.Features.Reports.Contracts;
using static BuildMind.Reports.Tests.Fixtures.SyntheticReportFixtures;
namespace BuildMind.Reports.Tests.Contracts;
public class ReportInputSnapshotTests
{
    [Fact] public void Mapper_preserves_exact_geometry() { var p = PlanSource(); var s = ReportInputSnapshot.FromResolvedSources("test-report-v1", Time.AddMinutes(2), Requirements(), p, Confirm(p), Disclaimer()).Snapshot; Assert.Equal(p.GeometryJson, s.Plan.GeometryJson); Assert.Equal(p.GeometryHash, s.Plan.GeometryHash); }
    [Fact] public void Tampered_bytes_rejected() { var p = PlanSource(); var changed = p with { GeometryJson = p.GeometryJson + " " }; Assert.ThrowsAny<ArgumentException>(() => ReportInputSnapshot.FromResolvedSources("test-report-v1", Time.AddMinutes(2), Requirements(), changed, Confirm(p), Disclaimer())); }
    [Fact] public void Mismatching_confirmation_rejected() { var p = PlanSource(); var c = Confirm(p) with { ConfirmedRevisionId = Id(99) }; Assert.ThrowsAny<ArgumentException>(() => ReportInputSnapshot.FromResolvedSources("test-report-v1", Time.AddMinutes(2), Requirements(), p, c, Disclaimer())); }
    [Fact] public void Malformed_json_rejected_even_with_matching_hash() { var p = PlanSource("{"); Assert.ThrowsAny<ArgumentException>(() => ReportInputSnapshot.FromResolvedSources("test-report-v1", Time.AddMinutes(2), Requirements(), p, Confirm(p), Disclaimer())); }
    [Fact] public void Unknown_evidence_not_promoted() { var p = PlanSource(requirementsMatch: null, evidence: "UNVERIFIED"); var s = ReportInputSnapshot.FromResolvedSources("test-report-v1", Time.AddMinutes(2), Requirements(), p, Confirm(p), Disclaimer()).Snapshot; Assert.Null(s.Plan.Validation.RequirementsMatch); Assert.Equal("UNVERIFIED", s.Plan.Validation.RegulatoryEvidenceStatus); }
    // RPT-01 and RPT-02 rejection coverage pending shared M3 contract agreement.
}
