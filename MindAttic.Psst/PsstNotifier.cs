namespace MindAttic.Psst;

using MindAttic.Psst.Configuration;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;

/// <summary>
/// Orchestrates the notification pipeline: play the Psst sound, then try
/// each configured SMS transport in priority order (Twilio → email-to-SMS).
/// First successful transport wins; missing transports are skipped without
/// being treated as failures.
/// </summary>
public sealed class PsstNotifier
{
    private readonly PsstConfiguration _config;
    private readonly HttpClient _http;

    public PsstNotifier(PsstConfiguration config, HttpClient? http = null)
    {
        _config = config;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// Play the Psst sound (unless <paramref name="silent"/>) and dispatch
    /// <paramref name="message"/> via the first available SMS transport.
    /// Returns a snapshot of what happened — caller decides how to surface it.
    /// </summary>
    public async Task<NotifyResult> NotifyAsync(
        string message,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        var soundPlayed = false;
        if (!silent)
            soundPlayed = PsstSoundPlayer.Play(waitForCompletion: true);

        var attempts = new List<SmsResult>();
        foreach (var client in BuildClients())
        {
            var result = await client.SendAsync(message, cancellationToken);
            attempts.Add(result);
            if (result.Success) break;
        }

        return new NotifyResult(soundPlayed, attempts);
    }

    private IEnumerable<ISmsClient> BuildClients()
    {
        if (_config.Twilio is not null && !string.IsNullOrWhiteSpace(_config.RecipientPhoneNumber))
            yield return new TwilioSmsClient(_http, _config.Twilio, _config.RecipientPhoneNumber);

        if (_config.Email is not null && !string.IsNullOrWhiteSpace(_config.RecipientEmailSmsAddress))
            yield return new EmailSmsClient(_config.Email, _config.RecipientEmailSmsAddress);
    }
}

/// <summary>Snapshot of one <see cref="PsstNotifier.NotifyAsync"/> call.</summary>
public sealed record NotifyResult(bool SoundPlayed, IReadOnlyList<SmsResult> SmsAttempts)
{
    public bool AnySmsSent => SmsAttempts.Any(a => a.Success);
    public SmsResult? FirstSuccess => SmsAttempts.FirstOrDefault(a => a.Success);
}
