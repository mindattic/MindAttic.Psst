namespace MindAttic.Psst.Sms;

/// <summary>
/// Catalog of US carrier email-to-SMS gateway suffixes. Built into the binary
/// so a notification can fan out across every major carrier even when the
/// recipient's carrier is unknown — the wrong-carrier gateways silently drop,
/// the right one delivers. "Over-send, never miss" is the operating principle:
/// duplicate buzzes are an acceptable price for guaranteed delivery to an
/// arbitrary phone number.
///
/// <para>Reality check: several of these gateways have been formally
/// deprecated by their carriers (T-Mobile announced shutdown of
/// <c>tmomail.net</c> in mid-2025, Sprint folded into T-Mobile years
/// earlier, etc.), but most continue to accept and route mail for some
/// subset of customers during the long tail. Keeping them all in the
/// list maximizes the chance that at least one path lands a message.</para>
///
/// <para>To customize: edit <see cref="UnitedStatesDomains"/> in code rather
/// than threading per-call carrier knowledge through configuration. Power
/// users can add international carriers here.</para>
/// </summary>
public static class CarrierGateways
{
    /// <summary>Known US-carrier email-to-SMS gateway domain suffixes.</summary>
    public static readonly IReadOnlyList<string> UnitedStatesDomains = new[]
    {
        "tmomail.net",                  // T-Mobile (incl. former Sprint)
        "txt.att.net",                  // AT&T (SMS)
        "vtext.com",                    // Verizon (SMS)
        "email.uscc.net",               // US Cellular
        "sms.myboostmobile.com",        // Boost Mobile (T-Mobile MVNO)
        "sms.cricketwireless.net",      // Cricket (AT&T MVNO)
        "mymetropcs.com",               // MetroPCS (T-Mobile MVNO)
        "msg.fi.google.com",            // Google Fi
    };

    /// <summary>
    /// Strip <paramref name="phoneNumber"/> to its 10-digit US form. Accepts
    /// E.164 (<c>+15551234567</c>), 1-prefixed 11-digit, or already-10-digit
    /// input plus the usual punctuation (<c>+</c>, <c>-</c>, <c>(</c>, <c>)</c>,
    /// dots, whitespace). Returns null if the input doesn't normalize to 10
    /// digits, OR if the input contains any letter characters — that way
    /// extension markers like <c>"1 ext 5551234567"</c> don't get silently
    /// reinterpreted as a valid US number.
    /// </summary>
    public static string? NormalizeTo10Digits(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return null;
        // Letters never appear in a real phone-number form. Rejecting up
        // front prevents extension/name/comment text ("ext", "x4", "Bob's
        // cell: 555-…") from sneaking through the digit-stripper.
        if (phoneNumber.Any(char.IsLetter)) return null;
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        return digits.Length == 10 ? digits : null;
    }

    /// <summary>
    /// Build a comma-separated recipient list by combining the digit-only
    /// form of <paramref name="phoneNumber"/> with every known carrier
    /// domain. Returns null if the phone number can't normalize to 10
    /// digits.
    /// </summary>
    public static string? BuildFanout(string? phoneNumber)
    {
        var digits = NormalizeTo10Digits(phoneNumber);
        if (digits is null) return null;
        return string.Join(",", UnitedStatesDomains.Select(d => $"{digits}@{d}"));
    }

    /// <summary>
    /// Union an explicit comma-separated recipient list with an auto-derived
    /// one, deduplicated (case-insensitive). Either side may be null/blank;
    /// the result preserves explicit-first ordering.
    /// </summary>
    public static string? Combine(string? explicitList, string? derivedList)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        foreach (var source in new[] { explicitList, derivedList })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            foreach (var addr in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(addr))
                    output.Add(addr);
            }
        }
        return output.Count == 0 ? null : string.Join(",", output);
    }
}
