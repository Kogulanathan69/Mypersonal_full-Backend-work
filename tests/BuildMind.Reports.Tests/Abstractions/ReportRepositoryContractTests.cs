using BuildMind.Application.Features.Reports.Abstractions;
using BuildMind.Domain.Entities;
using static BuildMind.Reports.Tests.Fixtures.SyntheticReportFixtures;
namespace BuildMind.Reports.Tests.Abstractions;
public class ReportRepositoryContractTests
{
    [Fact] public void Source_expectation_preserves_identity() { var s = Snapshot(); var e = new ReportSourceExpectation(s); Assert.Equal(SyntheticReportFixtures.Project, e.ProjectId); Assert.Equal(Requirement, e.RequirementsRevisionId); Assert.Equal(Revision, e.PlanRevisionId); Assert.Equal(Confirmation, e.ConfirmationId); Assert.Equal(s.Plan.GeometryHash, e.GeometryHash); }
    [Fact] public void Created_requires_pending_report() { var p = Pending(); Assert.Same(p, new ReportWriteResult(ReportWriteStatus.Created, p).SavedReport); Assert.ThrowsAny<ArgumentException>(() => new ReportWriteResult(ReportWriteStatus.Created, p.Fail(1, "TEST_ERROR", Time.AddMinutes(4)))); }
    [Fact] public void Transition_requires_terminal_report() { var p = Pending(); var r = p.Fail(1, "TEST_ERROR", Time.AddMinutes(4)); Assert.Same(r, new ReportWriteResult(ReportWriteStatus.Transitioned, r).SavedReport); Assert.ThrowsAny<ArgumentException>(() => new ReportWriteResult(ReportWriteStatus.Transitioned, p)); }
    [Theory] [InlineData(ReportWriteStatus.Created)] [InlineData(ReportWriteStatus.Transitioned)] public void Success_requires_payload(ReportWriteStatus status) => Assert.ThrowsAny<ArgumentException>(() => new ReportWriteResult(status));
    [Theory] [InlineData(ReportWriteStatus.ReportAlreadyExists)] [InlineData(ReportWriteStatus.ReportNotFound)] [InlineData(ReportWriteStatus.SourceConflict)] [InlineData(ReportWriteStatus.StateConflict)] [InlineData(ReportWriteStatus.SourceUnavailable)] public void Failure_forbids_payload(ReportWriteStatus status) { Assert.Null(new ReportWriteResult(status).SavedReport); Assert.ThrowsAny<ArgumentException>(() => new ReportWriteResult(status, Pending())); }
    [Fact] public void Undefined_status_rejected() => Assert.ThrowsAny<ArgumentException>(() => new ReportWriteResult((ReportWriteStatus)999));
}
