using BuildMind.Domain.ValueObjects;
using static BuildMind.Reports.Tests.Fixtures.SyntheticReportFixtures;
namespace BuildMind.Reports.Tests.Domain;
public class ReportArtifactTests
{
    [Fact] public void Metadata_preserved() { var a = Artifact(); Assert.Equal(ArtifactId, a.ArtifactId); Assert.Equal("application/pdf", a.MediaType); Assert.Equal(128L, a.ByteLength); }
    [Theory] [InlineData(0)] [InlineData(-1)] public void Bad_length_rejected(int length) => Assert.ThrowsAny<ArgumentException>(() => new ReportArtifact(ArtifactId, "application/pdf", length, "test-hash", Time));
    [Fact] public void Empty_identity_rejected() => Assert.ThrowsAny<ArgumentException>(() => new ReportArtifact(Guid.Empty, "application/pdf", 1, "test-hash", Time));
    [Theory] [InlineData("", "hash")] [InlineData("application/pdf", " ")] public void Blank_metadata_rejected(string type, string hash) => Assert.ThrowsAny<ArgumentException>(() => new ReportArtifact(ArtifactId, type, 1, hash, Time));
}
