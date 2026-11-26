using System.Net;
using System.Net.Mail;
using BuildMind.Application.Features.Administration;
using BuildMind.Application.Features.Auth.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// SMTP delivery (configurable; local capture via Mailpit in Development). Fail-safe defaults:
/// <list type="bullet">
/// <item>not configured → <see cref="IsConfigured"/> false and nothing is sent;</item>
/// <item>non-loopback SMTP hosts are refused unless <c>CaptainIntegration:Email:AllowExternalDelivery</c> is true (real
/// external delivery requires an approved provider and sender policy);</item>
/// <item>bodies, tokens and recipients are never logged.</item>
/// </list>
/// </summary>
internal sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    : ISystemEmailSender, IPasswordResetEmailSender
{
    private const string Prefix = "CaptainIntegration:Email:";

    private string? Host => configuration[Prefix + "SmtpHost"];
    private string? From => configuration[Prefix + "FromAddress"];
    private string? PublicBaseUrl => configuration[Prefix + "PublicBaseUrl"];

    private bool Flag(string key) => bool.TryParse(configuration[Prefix + key], out var value) && value;

    public bool IsConfigured
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(From)) return false;
            var loopback = Host is "localhost" || (IPAddress.TryParse(Host, out var ip) && IPAddress.IsLoopback(ip));
            if (loopback) return true;
            // External delivery: explicitly allowed AND encrypted (STARTTLS with platform certificate validation, never disabled).
            return Flag("AllowExternalDelivery") && Flag("UseStartTls");
        }
    }

    public async Task<bool> SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return false;
        try
        {
            using var client = new SmtpClient(Host, int.TryParse(configuration[Prefix + "SmtpPort"], out var port) && port is > 0 and < 65536 ? port : 25)
            {
                EnableSsl = Flag("UseStartTls"),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000,
            };
            var userName = configuration[Prefix + "UserName"];
            var passwordFile = configuration[Prefix + "PasswordFile"];
            if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(passwordFile) && File.Exists(passwordFile))
                client.Credentials = new NetworkCredential(userName, (await File.ReadAllTextAsync(passwordFile, cancellationToken).ConfigureAwait(false)).Trim());
            using var message = new MailMessage(From!, recipient, subject, textBody) { BodyEncoding = System.Text.Encoding.UTF8 };
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException or IOException)
        {
            logger.LogWarning("Email delivery failed ({Type})", exception.GetType().Name);
            return false;
        }
    }

    public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) =>
        SendAsync(email, "BuildMind password reset",
            "A password reset was requested for your BuildMind account.\n\n" +
            $"Reset code: {token}\n" + Link("reset-password", email, token) +
            $"\nThis code expires at {expiresAtUtc:u}. If you did not request it, ignore this message.\n", cancellationToken);

    public string Link(string path, string email, string token) =>
        string.IsNullOrWhiteSpace(PublicBaseUrl) ? ""
            : $"Link: {PublicBaseUrl.TrimEnd('/')}/{path}?email={Uri.EscapeDataString(email)}&token={token}\n";
}
