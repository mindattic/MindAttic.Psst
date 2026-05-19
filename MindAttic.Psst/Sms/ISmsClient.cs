namespace MindAttic.Psst.Sms;

/// <summary>
/// Single-shot SMS dispatcher. Implementations are constructed already wired
/// to their credentials; the notifier picks the first one that succeeds.
/// </summary>
public interface ISmsClient
{
    /// <summary>Human-readable transport name for logging ("Twilio", "Email-to-SMS").</summary>
    string TransportName { get; }

    /// <summary>Send <paramref name="message"/> to the configured recipient.</summary>
    Task<SmsResult> SendAsync(string message, CancellationToken cancellationToken = default);
}

public sealed record SmsResult(bool Success, string Transport, string? Detail);
