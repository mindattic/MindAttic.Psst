namespace MindAttic.Psst;

using System.Net.Http.Headers;
using System.Reflection;
using MindAttic.Psst.Configuration;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;

/// <summary>
/// Orchestrates the notification pipeline: kicks off the Psst sound and the
/// SMS dispatch concurrently, then dispatches the message through the
/// transport selected by <see cref="PsstVia"/> (email-to-SMS by default;
/// Twilio when explicitly requested and <see cref="PsstFeatures.TwilioEnabled"/>
/// is on). One transport per send — no implicit fallback chain, since the
/// caller has already decided which path to take.
/// </summary>
public sealed class PsstNotifier : IAsyncDisposable
{
    // One process-wide HttpClient. Twilio's hostname/cert is stable enough that
    // the default SocketsHttpHandler lifetime is fine here; we'd only need
    // IHttpClientFactory if we were rotating endpoints frequently.
    //
    // Identify Psst in upstream access logs — Twilio support requests this when
    // chasing carrier-side delivery failures, and the default ".NET/<version>"
    // is unhelpful.
    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // ProductInfoHeaderValue only accepts a strict token grammar for the
        // version — strip anything beyond digits and dots so a +commit-hash
        // suffix on InformationalVersion can't throw at startup.
        var raw = typeof(PsstNotifier).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(PsstNotifier).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var version = new string(raw.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (string.IsNullOrEmpty(version)) version = "0.0.0";
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MindAttic.Psst", version));
        return http;
    });

    private readonly IReadOnlyList<ISmsClient> _clients;
    private readonly Func<CancellationToken, Task<PsstPlayResult>> _playSound;

    public PsstNotifier(PsstConfiguration config, PsstVia via = PsstVia.Email, HttpClient? http = null)
        : this(BuildClients(config, http ?? SharedHttp.Value, via), PsstSoundPlayer.PlayAsync)
    {
    }

    /// <summary>
    /// Test seam: inject the SMS transport chain and the sound-playing
    /// function directly. Not exposed publicly to keep the supported API
    /// surface limited to the configuration-driven constructor.
    /// </summary>
    internal PsstNotifier(
        IEnumerable<ISmsClient> clients,
        Func<CancellationToken, Task<PsstPlayResult>> playSound)
    {
        _clients = clients.ToArray();
        _playSound = playSound;
    }

    /// <summary>
    /// Play the Psst sound (unless <paramref name="silent"/>) and dispatch
    /// <paramref name="message"/> via the first available SMS transport.
    /// Sound and SMS run concurrently — neither blocks the other.
    /// </summary>
    public async Task<NotifyResult> NotifyAsync(
        string message,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        var soundTask = silent
            ? Task.FromResult(PsstPlayResult.Fail("silent"))
            : _playSound(cancellationToken);

        var smsTask = DispatchSmsAsync(message, cancellationToken);

        await Task.WhenAll(soundTask, smsTask);
        return new NotifyResult(silent ? null : soundTask.Result, smsTask.Result);
    }

    private async Task<IReadOnlyList<SmsResult>> DispatchSmsAsync(
        string message, CancellationToken cancellationToken)
    {
        var attempts = new List<SmsResult>();
        foreach (var client in _clients)
        {
            var result = await client.SendAsync(message, cancellationToken);
            attempts.Add(result);
            if (result.Success) break;
        }
        return attempts;
    }

    /// <summary>
    /// Tear down any client that holds an open resource (e.g.
    /// <see cref="EmailSmsClient"/>'s persistent SMTP session). The shared
    /// <see cref="HttpClient"/> is process-wide and intentionally not
    /// disposed here.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            if (client is IAsyncDisposable ad)
            {
                try { await ad.DisposeAsync(); }
                catch { /* best effort — process is usually exiting */ }
            }
        }
    }

    private static IEnumerable<ISmsClient> BuildClients(PsstConfiguration config, HttpClient http, PsstVia via)
    {
        // Exactly one transport per send — the caller has already resolved
        // which one via the --via flag / PSST_VIA env var / per-contact
        // default precedence (see PsstViaResolver). When the requested
        // transport isn't wired up (missing creds, feature-gated off, no
        // recipient), this returns an empty enumerable and DispatchSmsAsync
        // reports "no SMS transport configured" upstream.
        switch (via)
        {
            case PsstVia.Twilio:
                if (PsstFeatures.TwilioEnabled
                    && config.Twilio is not null
                    && !string.IsNullOrWhiteSpace(config.RecipientPhoneNumber))
                    yield return new TwilioSmsClient(http, config.Twilio, config.RecipientPhoneNumber);
                break;

            case PsstVia.Email:
            default:
                // Recipients = explicit `toEmail` (if any) ∪ auto-fanout
                // derived from `to`'s 10-digit form across every known US
                // carrier gateway. Wrong-carrier gateways silently drop;
                // the recipient's real carrier delivers.
                var derived = CarrierGateways.BuildFanout(config.RecipientPhoneNumber);
                var combined = CarrierGateways.Combine(config.RecipientEmailSmsAddress, derived);
                if (config.Email is not null && !string.IsNullOrWhiteSpace(combined))
                    yield return new EmailSmsClient(config.Email, combined);
                break;
        }
    }
}

/// <summary>Snapshot of one <see cref="PsstNotifier.NotifyAsync"/> call.</summary>
public sealed record NotifyResult(PsstPlayResult? Sound, IReadOnlyList<SmsResult> SmsAttempts)
{
    public bool SoundPlayed => Sound?.Success == true;
    public bool AnySmsSent => SmsAttempts.Any(a => a.Success);
    public SmsResult? FirstSuccess => SmsAttempts.FirstOrDefault(a => a.Success);
}
