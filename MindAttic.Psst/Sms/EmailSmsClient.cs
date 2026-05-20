namespace MindAttic.Psst.Sms;

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MindAttic.Psst.Configuration;

/// <summary>
/// Sends SMS via carrier email-to-SMS gateways (e.g. <c>5555550100@vtext.com</c>
/// for Verizon, <c>@txt.att.net</c> for AT&amp;T). Fallback when Twilio is not
/// configured; reliability varies by carrier and may carry per-day caps.
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
        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(_settings.From));
        mail.To.Add(MailboxAddress.Parse(_toEmailGateway));
        mail.Subject = string.Empty; // many SMS gateways strip or prepend the subject
        mail.Body = new TextPart("plain") { Text = message };

        // 465 is implicit TLS; 587 (and everything else) is STARTTLS.
        var secure = _settings.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secure, cancellationToken);
            await smtp.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
            await smtp.SendAsync(mail, cancellationToken);
            await smtp.DisconnectAsync(quit: true, cancellationToken);
            return new SmsResult(true, TransportName, $"sent to {_toEmailGateway}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // MailKit surfaces a wide tree (SmtpCommandException,
            // AuthenticationException, IOException, …). Lump them all into
            // a transport failure with the original message.
            return new SmsResult(false, TransportName, ex.Message);
        }
    }
}
