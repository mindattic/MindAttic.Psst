namespace MindAttic.Psst.Sms;

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MindAttic.Psst.Configuration;

/// <summary>
/// Sends SMS via carrier email-to-SMS gateways (e.g. <c>5555550100@vtext.com</c>
/// for Verizon, <c>@txt.att.net</c> for AT&amp;T). Used as the primary
/// transport in environments where A2P 10DLC registration is impractical,
/// since the wrong-carrier gateways silently drop and only the recipient's
/// real carrier actually delivers.
/// <para>
/// The recipient string is parsed as a comma-separated list, so a single
/// notification can fan out across multiple carrier domains in one SMTP
/// session. The send is considered successful when at least one recipient
/// accepts the message.
/// </para>
/// <para>
/// Uses MailKit rather than the obsolete <see cref="System.Net.Mail.SmtpClient"/>.
/// MailKit picks the right TLS mode (STARTTLS on 587, implicit TLS on 465).
/// </para>
/// </summary>
public sealed class EmailSmsClient : ISmsClient
{
    private readonly EmailSettings _settings;
    private readonly string _toEmailGateway;

    public string TransportName => "Email-to-SMS";

    public EmailSmsClient(EmailSettings settings, string toEmailGateway)
    {
        _settings = settings;
        _toEmailGateway = toEmailGateway;
    }

    public async Task<SmsResult> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var recipients = _toEmailGateway
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (recipients.Length == 0)
            return new SmsResult(false, TransportName, "no recipient addresses configured");

        // 465 is implicit TLS; 587 (and everything else) is STARTTLS.
        var secure = _settings.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        using var smtp = new SmtpClient();
        // OCSP/CRL revocation checks routinely fail on Windows machines behind
        // corporate proxies or with slow upstream responders, surfacing as
        // "The revocation function was unable to check revocation for the
        // certificate." The chain itself still validates against the local
        // trust store — we only skip the freshness check.
        smtp.CheckCertificateRevocation = false;

        try
        {
            await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secure, cancellationToken);
            await smtp.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

            var succeeded = new List<string>();
            var failed = new List<string>();
            foreach (var to in recipients)
            {
                try
                {
                    var mail = new MimeMessage();
                    mail.From.Add(MailboxAddress.Parse(_settings.From));
                    mail.To.Add(MailboxAddress.Parse(to));
                    // Deliberately leave Subject unset. Setting it to "" emits an
                    // empty `Subject:` header that several gateways render as
                    // "/ /" dividers around the body; omitting it entirely
                    // produces a cleaner SMS on the receiving phone.
                    mail.Body = new TextPart("plain") { Text = message };
                    await smtp.SendAsync(mail, cancellationToken);
                    succeeded.Add(to);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed.Add($"{to}: {Truncate(ex.Message, 120)}");
                }
            }

            await smtp.DisconnectAsync(quit: true, cancellationToken);

            if (succeeded.Count == 0)
                return new SmsResult(false, TransportName, $"all {recipients.Length} recipients failed — {string.Join("; ", failed)}");

            var detail = $"sent to {succeeded.Count}/{recipients.Length} ({string.Join(", ", succeeded)})";
            if (failed.Count > 0)
                detail += $"; failed: {string.Join("; ", failed)}";
            return new SmsResult(true, TransportName, detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // MailKit surfaces a wide tree (SmtpCommandException,
            // AuthenticationException, IOException, …). Connection-level
            // failures bubble out here; per-recipient failures are aggregated
            // above so one bad gateway doesn't doom the rest.
            return new SmsResult(false, TransportName, ex.Message);
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
