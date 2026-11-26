using BuildMind.Domain.ValueObjects;
using static BuildMind.Reports.Tests.Fixtures.SyntheticReportFixtures;
namespace BuildMind.Reports.Tests.Domain;
public class ReportSnapshotTests
{
    [Fact] public void Exact_sources_preserved() { var s = Snapshot(); Assert.Equal(Project, s.ProjectId); Assert.Equal(Requirement, s.Requirements.Id); Assert.Equal(Revision, s.Plan.RevisionId); Assert.Equal(Confirmation, s.Confirmation.ConfirmationId); Assert.Equal(s.Plan.GeometryHash, s.Confirmation.GeometryHash); }
    [Fact] public void Wrong_confirmation_revision_rejected() { var s = Snapshot(); var c = s.Confirmation; var wrong = new ReportConfirmationSnapshot(c.ConfirmationId, c.ProjectId, c.PlanId, Id(99), c.GeometryHash, c.ValidationId, c.ConfirmedBy, c.ConfirmedAtUtc, c.WarningAcknowledgement); Assert.ThrowsAny<ArgumentException>(() => new ReportSnapshot(s.SchemaVersion, s.CapturedAtUtc, s.Requirements, s.Plan, wrong, s.Disclaimer)); }
    [Fact] public void Null_disclaimer_rejected() { var s = Snapshot(); Assert.Throws<ArgumentNullException>(() => new ReportSnapshot(s.SchemaVersion, s.CapturedAtUtc, s.Requirements, s.Plan, s.Confirmation, null!)); }
}
