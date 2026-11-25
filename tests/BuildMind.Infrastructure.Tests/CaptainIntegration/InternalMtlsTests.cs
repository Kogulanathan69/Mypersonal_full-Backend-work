using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BuildMind.Infrastructure.CaptainIntegration;

namespace BuildMind.Infrastructure.Tests.CaptainIntegration;

/// <summary>Server-certificate validation used by the API → python-services mTLS handler (no accept-any path).</summary>
public sealed class InternalMtlsTests
{
    private static X509Certificate2 Ca(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(60));
    }

    private static X509Certificate2 Leaf(X509Certificate2 issuer, string eku, DateTimeOffset? notAfter = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=python-services", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(eku)], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        var end = notAfter ?? DateTimeOffset.UtcNow.AddDays(10);
        return request.Create(issuer, end.AddDays(-20), end, Guid.NewGuid().ToByteArray());
    }

    private const string ServerAuth = "1.3.6.1.5.5.7.3.1", ClientAuth = "1.3.6.1.5.5.7.3.2";

    [Fact]
    public void Server_certificate_from_the_pinned_ca_with_matching_name_is_accepted()
    {
        using var ca = Ca("server-ca");
        using var leaf = Leaf(ca, ServerAuth);
        Assert.True(InternalMtls.ValidateServer(leaf, SslPolicyErrors.RemoteCertificateChainErrors, ca));
    }

    [Fact]
    public void Name_mismatch_untrusted_ca_wrong_purpose_expired_and_missing_are_rejected()
    {
        using var ca = Ca("server-ca");
        using var rogue = Ca("rogue-ca");
        using var good = Leaf(ca, ServerAuth);
        Assert.False(InternalMtls.ValidateServer(good, SslPolicyErrors.RemoteCertificateNameMismatch, ca));
        using var fromRogue = Leaf(rogue, ServerAuth);
        Assert.False(InternalMtls.ValidateServer(fromRogue, SslPolicyErrors.None, ca));
        using var clientOnly = Leaf(ca, ClientAuth);
        Assert.False(InternalMtls.ValidateServer(clientOnly, SslPolicyErrors.None, ca));
        using var expired = Leaf(ca, ServerAuth, DateTimeOffset.UtcNow.AddDays(-2));
        Assert.False(InternalMtls.ValidateServer(expired, SslPolicyErrors.None, ca));
        Assert.False(InternalMtls.ValidateServer(null, SslPolicyErrors.RemoteCertificateNotAvailable, ca));
    }
}
