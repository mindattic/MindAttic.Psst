namespace MindAttic.Psst.Sms;

using System.Net;
using System.Net.Mail;
using MindAttic.Psst.Configuration;

/// <summary>
/// Sends SMS via carrier email-to-SMS gateways (e.g. <c>5555550100@vtext.com</c>
/// for Verizon, <c>@txt.att.net</c> for AT&amp;T). The fallback path when Twilio
/// is not configured. Reliability varies by carrier and may carry per-day caps.
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
        try
        {
            using var smtp = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password),
            };
            using var mail = new MailMessage(_settings.From, _toEmailGateway)
            {
                Subject = "",          // many SMS gateways strip or prepend the subject
                Body = message,
            };
            await smtp.SendMailAsync(mail, cancellationToken);
            return new SmsResult(true, TransportName, $"sent to {_toEmailGateway}");
        }
        catch (Exception ex)
        {
            return new SmsResult(false, TransportName, ex.Message);
        }
    }
}
