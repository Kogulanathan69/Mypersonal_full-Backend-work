using BuildMind.Application.Features.Reports.Services;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportArtifactIntegrityTests
{
    [Fact] public void MatchingReadBackBytesPassIntegrityOnly()
    {
        byte[] bytes="%PDF-synthetic-integrity-only"u8.ToArray();
        var metadata=new ReportArtifact(Guid.NewGuid(),"application/pdf",bytes.Length,ReportArtifactIntegrity.Hash(bytes),SyntheticReportFixtures.Time);
        Assert.True(ReportArtifactIntegrity.Matches(metadata,bytes,bytes.Length));
        Assert.False(ReportArtifactIntegrity.Matches(metadata,bytes,bytes.Length-1));
        bytes[^1]^=1;
        Assert.False(ReportArtifactIntegrity.Matches(metadata,bytes,1000));
    }
    [Fact] public void MatchingHashWithoutPdfSignatureDoesNotPass()
    {
        byte[] bytes="synthetic-not-a-pdf"u8.ToArray();
        var metadata=new ReportArtifact(Guid.NewGuid(),"application/pdf",bytes.Length,ReportArtifactIntegrity.Hash(bytes),SyntheticReportFixtures.Time);
        Assert.False(ReportArtifactIntegrity.Matches(metadata,bytes,1000));
    }
}
