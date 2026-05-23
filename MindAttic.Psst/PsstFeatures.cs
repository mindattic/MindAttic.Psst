namespace MindAttic.Psst;

/// <summary>
/// Compile-time feature flags for the Psst transport pipeline. Lives in code
/// (not configuration) so toggling requires an intentional code review and
/// rebuild rather than a settings tweak.
/// </summary>
public static class PsstFeatures
{
    /// <summary>
    /// Gate for the Twilio SMS transport. When <c>true</c>, Twilio is
    /// available as a selectable transport via <c>--via twilio</c> /
    /// <c>PSST_VIA=twilio</c> / a per-contact default; when <c>false</c>, the
    /// notifier never instantiates the Twilio client regardless of what the
    /// user requests. Default project transport stays email-to-SMS either way
    /// (see <see cref="PsstVia"/> precedence in <see cref="PsstViaResolver"/>).
    /// <para>
    /// Enabled: the configured Twilio sender number is registered to an A2P
    /// 10DLC Brand + Campaign (Twilio Console → Messaging → Regulatory
    /// Compliance). Until the registration moves to Approved, US carriers
    /// will silently drop messages with error 30034 even though Twilio's
    /// API returns <c>queued</c>. Email-to-SMS fanout bypasses this entirely.
    /// </para>
    /// </summary>
    public const bool TwilioEnabled = true;
}
