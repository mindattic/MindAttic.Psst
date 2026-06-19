namespace MindAttic.Psst;

using MindAttic.Psst.Configuration;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;

/// <summary>
/// Orchestrates the notification pipeline: kicks off the Psst sound and the
/// SMS dispatch concurrently, then dispatches the message through the
/// configured email-to-SMS transport. One transport per send — no implicit
/// fallback chain.
/// </summary>
public sealed class PsstNotifier : IAsyncDisposable
{
    private readonly IReadOnlyList<ISmsClient> _clients;
    private readonly Func<CancellationToken, Task<PsstPlayResult>> _playSound;

    public PsstNotifier(PsstConfiguration config, PsstVia via = PsstVia.Email)
        : this(BuildClients(config, via), PsstSoundPlayer.PlayAsync)
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
    /// <see cref="EmailSmsClient"/>'s persistent SMTP session).
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

    private static IEnumerable<ISmsClient> BuildClients(PsstConfiguration config, PsstVia via)
    {
        // Exactly one transport per send — the caller has already resolved
        // which one via the PSST_VIA env var / per-contact default precedence
        // (see PsstViaResolver). When nothing is wired up (missing creds, no
        // recipient), this returns an empty enumerable and DispatchSmsAsync
        // reports "no SMS transport configured" upstream.
        switch (via)
        {
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
