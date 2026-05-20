namespace MindAttic.Psst;

using MindAttic.Psst.Configuration;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;

/// <summary>
/// Orchestrates the notification pipeline: kicks off the Psst sound and the
/// SMS dispatch concurrently, then walks the SMS transports in priority order
/// (Twilio → email-to-SMS) until one succeeds.
/// </summary>
public sealed class PsstNotifier
{
    // One process-wide HttpClient. Twilio's hostname/cert is stable enough that
    // the default SocketsHttpHandler lifetime is fine here; we'd only need
    // IHttpClientFactory if we were rotating endpoints frequently.
    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
        new HttpClient { Timeout = TimeSpan.FromSeconds(15) });

    private readonly IReadOnlyList<ISmsClient> _clients;
    private readonly Func<CancellationToken, Task<PsstPlayResult>> _playSound;

    public PsstNotifier(PsstConfiguration config, HttpClient? http = null)
        : this(BuildClients(config, http ?? SharedHttp.Value), PsstSoundPlayer.PlayAsync)
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

    private static IEnumerable<ISmsClient> BuildClients(PsstConfiguration config, HttpClient http)
    {
        if (config.Twilio is not null && !string.IsNullOrWhiteSpace(config.RecipientPhoneNumber))
            yield return new TwilioSmsClient(http, config.Twilio, config.RecipientPhoneNumber);

        if (config.Email is not null && !string.IsNullOrWhiteSpace(config.RecipientEmailSmsAddress))
            yield return new EmailSmsClient(config.Email, config.RecipientEmailSmsAddress);
    }
}

/// <summary>Snapshot of one <see cref="PsstNotifier.NotifyAsync"/> call.</summary>
public sealed record NotifyResult(PsstPlayResult? Sound, IReadOnlyList<SmsResult> SmsAttempts)
{
    public bool SoundPlayed => Sound?.Success == true;
    public bool AnySmsSent => SmsAttempts.Any(a => a.Success);
    public SmsResult? FirstSuccess => SmsAttempts.FirstOrDefault(a => a.Success);
}
