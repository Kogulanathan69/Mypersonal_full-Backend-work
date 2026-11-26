using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;

namespace BuildMind.Infrastructure.Tests.Persistence;

public sealed class RequirementMutationCanonicalPayloadTests
{
    [Theory]
    [InlineData(1, "424D5251010101000000017600000002000000010100000001410000000300000001010000000142000000000101000000014101000000014201000000014B00", "7B0D3D8F5DE9E03295D69F18E617ECCD7DE4F162ECC888353A4D737D528C87DE")]
    [InlineData(2, "424D525101020100000001760000000101000000014200", "F4F024F6139BEE73325E0FAE9C1C5DA654640831745AB7F18D489737E247BA28")]
    [InlineData(3, "424D525101030100000001760000000101000000014101000000014201000000014B", "AE4ECF8440D1938BC3EF61026672AE12387C06CD0721195A8F8C6212A0D926E2")]
    [InlineData(4, "424D5251010401000000017600", "B02D3D7DC8C3ED3F4C07E44FCD64E696148B98B2EA7EB13BFE9DDA94B483502A")]
    public void Byte_profile_and_envelope_digest_match_fixed_vectors(int category, string bytes, string digest)
    {
        static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
        Guid? expected = category == 1 ? null : Id(4);
        var fields = new SaveRequirementsRequest(expected, "v", 2, [new("A", 3)], [new("B", null)],
            [new("A", "B", "K")], null);
        var payload = RequirementMutationCanonicalPayload.Encode(new((RequirementMutationCategory)category, fields));
        Assert.Equal(bytes, Convert.ToHexString(payload));
        Assert.Equal(digest, Convert.ToHexString(RequirementMutationFingerprint.Compute(Id(1), Id(2), Id(3), 1, expected, payload)));
    }
}
