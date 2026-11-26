using BuildMind.Infrastructure.CaptainIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuildMind.Infrastructure.Tests.CaptainIntegration;

/// <summary>External email requires explicit enablement AND STARTTLS; loopback capture needs neither.</summary>
public sealed class SmtpDeliveryPolicyTests
{
    private static bool Configured(string host, string? allow = null, string? tls = null, string? from = "no-reply@buildmind.invalid")
    {
        var values = new Dictionary<string, string?>
        {
            ["CaptainIntegration:Email:SmtpHost"] = host, ["CaptainIntegration:Email:FromAddress"] = from,
            ["CaptainIntegration:Email:AllowExternalDelivery"] = allow, ["CaptainIntegration:Email:UseStartTls"] = tls,
        };
        return new SmtpEmailSender(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), NullLogger<SmtpEmailSender>.Instance).IsConfigured;
    }

    [Theory]
    [InlineData("127.0.0.1", null, null, true)]
    [InlineData("localhost", null, null, true)]
    [InlineData("smtp.example.com", null, null, false)]          // external not allowed
    [InlineData("smtp.example.com", "true", null, false)]        // allowed but plaintext -> refused
    [InlineData("smtp.example.com", "true", "false", false)]
    [InlineData("smtp.example.com", "true", "true", true)]       // allowed + STARTTLS
    public void Delivery_policy(string host, string? allow, string? tls, bool expected) => Assert.Equal(expected, Configured(host, allow, tls));

    [Fact]
    public void Missing_sender_is_not_configured() => Assert.False(Configured("127.0.0.1", from: null));
}
