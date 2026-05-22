namespace MindAttic.Psst;

/// <summary>
/// Compile-time feature flags for the Psst transport pipeline. Lives in code
/// (not configuration) so toggling requires an intentional code review and
/// rebuild rather than a settings tweak.
/// </summary>
public static class PsstFeatures
{
    /// <summary>
    /// Gate for the Twilio SMS transport. Twilio credentials still resolve
    /// from the Vault as normal; this flag just decides whether the
    /// notifier actually instantiates the Twilio client.
    /// <para>
    /// Disabled because the configured Twilio sender number is a US local
    /// 10-digit long code that is not registered to an A2P 10DLC Brand +
    /// Campaign. US carriers silently drop every message from an
    /// unregistered LC with error 30034 — Twilio's API still returns
    /// <c>queued</c>, so the chain happily declares victory while nothing
    /// actually lands on the recipient's phone. Email-to-SMS fanout
    /// bypasses this entirely.
    /// </para>
    /// <para>
    /// Flip back to <c>true</c> after either (a) completing A2P 10DLC
    /// registration in the Twilio Console (Messaging → Regulatory
    /// Compliance) or (b) porting the sender to a Toll-Free number with
    /// Toll-Free Verification approved.
    /// </para>
    /// </summary>
    public const bool TwilioEnabled = false;
}
