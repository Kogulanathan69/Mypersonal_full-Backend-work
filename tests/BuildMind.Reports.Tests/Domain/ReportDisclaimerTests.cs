using BuildMind.Domain.ValueObjects;
using static BuildMind.Reports.Tests.Fixtures.SyntheticReportFixtures;
namespace BuildMind.Reports.Tests.Domain;
public class ReportDisclaimerTests
{
    [Fact] public void Wording_preserved() { var d = Disclaimer(); Assert.Equal("test-v1", d.Version); Assert.Equal("Synthetic preliminary report disclaimer.", d.Text); }
    [Theory] [InlineData(null, "text")] [InlineData("", "text")] [InlineData("v1", null)] [InlineData("v1", " ")] public void Blank_fields_rejected(string? version, string? text) => Assert.ThrowsAny<ArgumentException>(() => new ReportDisclaimer(version!, text!));
}
