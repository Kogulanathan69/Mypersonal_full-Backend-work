using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Mutual TLS for API → python-services (Captain profile). The API presents its own client certificate (separate key)
/// and accepts the server ONLY if its chain builds to the dedicated internal server CA (custom root trust; the system
/// store is not used) AND its name matches the configured base URL. There is no accept-any callback and no plaintext
/// fallback: a failed handshake is a dependency failure. The internal token is still sent on every call.
/// Certificates are read when the handler is created; IHttpClientFactory recycles handlers (default 2 minutes), so
/// rotated files are picked up without restarting the API.
/// </summary>
public static class InternalMtls
{
    public const string Prefix = "CaptainIntegration:PythonMtls:";

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[Prefix + "ServerCaFile"]);

    public static SocketsHttpHandler CreatePythonHandler(IConfiguration configuration)
    {
        var caFile = Require(configuration, "ServerCaFile");
        var certFile = Require(configuration, "ClientCertFile");
        var keyFile = Require(configuration, "ClientKeyFile");
        var serverCa = X509Certificate2.CreateFromPem(File.ReadAllText(caFile));
        using var ephemeral = X509Certificate2.CreateFromPemFile(certFile, keyFile);
        // Re-import so the private key is usable for TLS client authentication on every platform.
        var client = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            MaxConnectionsPerServer = 32,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxResponseHeadersLength = 64,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificates = [client],
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck, // private offline CA; short-lived leaves
                RemoteCertificateValidationCallback = (_, certificate, _, errors) => ValidateServer(certificate, errors, serverCa),
            },
        };
    }

    /// <summary>Name must match (no RemoteCertificateNameMismatch) and the chain must build to the pinned server CA only.</summary>
    public static bool ValidateServer(X509Certificate? certificate, SslPolicyErrors errors, X509Certificate2 serverCa)
    {
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
            || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            return false;
        using var leaf = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(serverCa);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")); // serverAuth EKU
        return chain.Build(leaf);
    }

    private static string Require(IConfiguration configuration, string key) =>
        configuration[Prefix + key] is { Length: > 0 } path && File.Exists(path)
            ? path
            : throw new InvalidOperationException($"{Prefix}{key} must name an existing file.");
}
