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
/// <para>
/// The SMTP session is opened lazily on the first <see cref="SendAsync"/>
/// call and held open for the lifetime of this instance — long-running
/// <c>--repeat</c> loops amortize TLS handshakes across sends instead of
/// paying the connect/auth cost every time. If the connection goes idle
/// long enough for the server to drop it, the next send reconnects
/// transparently.
/// </para>
/// </summary>
public sealed class EmailSmsClient : ISmsClient, IAsyncDisposable
{
    private readonly EmailSettings _settings;
    private readonly string _toEmailGateway;
    private SmtpClient? _smtp;

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

        try
        {
            var smtp = await EnsureConnectedAsync(cancellationToken);

            // Parse the sender once. A malformed `From` is a config error, not a
            // per-recipient failure — parsing it here lets it surface through the
            // outer catch as a single clear message instead of being reported
            // identically against every recipient.
            var fromAddress = MailboxAddress.Parse(_settings.From);

            var succeeded = new List<string>();
            var failed = new List<string>();
            foreach (var to in recipients)
            {
                try
                {
                    var mail = new MimeMessage();
                    mail.From.Add(fromAddress);
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
                    // Distinguish a per-address rejection (connection still up —
                    // try the next recipient) from a dropped connection. Once the
                    // socket is gone, every remaining recipient would just throw
                    // the same error; drop the dead session so the next send
                    // reconnects cleanly, and stop hammering it.
                    if (!smtp.IsConnected)
                    {
                        await TryDisconnectAsync();
                        break;
                    }
                }
            }

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
            //
            // Drop the connection so the next send forces a clean reconnect
            // rather than reusing a half-broken session.
            await TryDisconnectAsync();
            return new SmsResult(false, TransportName, ex.Message);
        }
    }

    /// <summary>
    /// Lazily open (or reopen) the SMTP session. Reuses the existing
    /// connection when it's still alive and authenticated; otherwise
    /// drops any stale handle and dials in fresh. Long idle gaps between
    /// repeats are expected and handled transparently.
    /// </summary>
    private async Task<SmtpClient> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_smtp is { IsConnected: true, IsAuthenticated: true })
            return _smtp;

        // Dispose any half-open handle from a prior failed send before
        // reconnecting — leaks here would accumulate one socket per attempt.
        await TryDisconnectAsync();

        var smtp = new SmtpClient
        {
            // OCSP/CRL revocation checks routinely fail on Windows machines behind
            // corporate proxies or with slow upstream responders, surfacing as
            // "The revocation function was unable to check revocation for the
            // certificate." The chain itself still validates against the local
            // trust store — we only skip the freshness check.
            CheckCertificateRevocation = false,
        };

        // 465 is implicit TLS; 587 (and everything else) is STARTTLS.
        var secure = _settings.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secure, cancellationToken);
        await smtp.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
        _smtp = smtp;
        return smtp;
    }

    /// <summary>
    /// Tear down the persistent SMTP session. Safe to call multiple times.
    /// Errors are swallowed — by the time we're disconnecting we don't care
    /// about the why, just that the next send starts clean.
    /// </summary>
    private async Task TryDisconnectAsync()
    {
        var smtp = _smtp;
        _smtp = null;
        if (smtp is null) return;
        try
        {
            if (smtp.IsConnected)
                await smtp.DisconnectAsync(quit: true);
        }
        catch
        {
            // best effort
        }
        finally
        {
            smtp.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await TryDisconnectAsync();

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
