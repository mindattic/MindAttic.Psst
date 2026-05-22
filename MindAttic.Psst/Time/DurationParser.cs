namespace MindAttic.Psst.Time;

using System;
using System.Globalization;

/// <summary>
/// Parses short, human-friendly duration strings used by Psst CLI flags
/// such as <c>--interval</c> / <c>--every</c>.
///
/// <para>Grammar (whitespace trimmed, case-insensitive suffix):</para>
/// <code>
///   &lt;duration&gt; := &lt;non-negative-integer&gt; [ s | m | h | d ]
/// </code>
///
/// <para>Examples:</para>
/// <list type="bullet">
///   <item><c>30s</c> → 30 seconds</item>
///   <item><c>5m</c>  → 5 minutes</item>
///   <item><c>2h</c>  → 2 hours</item>
///   <item><c>1d</c>  → 1 day</item>
///   <item><c>1800</c> → 1800 seconds (bare integer = seconds)</item>
/// </list>
///
/// <para>
/// Decimals (<c>1.5h</c>) are rejected — keep CLI durations integer-valued
/// to remove ambiguity and locale concerns. Negative values are rejected.
/// </para>
/// </summary>
public static class DurationParser
{
    /// <summary>
    /// Attempt to parse <paramref name="input"/> into a non-negative
    /// <see cref="TimeSpan"/>. Returns <c>false</c> on any malformed input
    /// (including <c>null</c>, empty, whitespace, decimals, negatives, and
    /// overflow). On failure <paramref name="duration"/> is
    /// <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public static bool TryParse(string? input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        if (s.Length == 0) return false;

        // Split numeric part from suffix. A trailing letter is the unit;
        // a trailing digit means "bare integer, default unit = seconds".
        var lastChar = s[^1];
        TimeSpan unit;
        string numericPart;

        if (char.IsDigit(lastChar))
        {
            unit = TimeSpan.FromSeconds(1);
            numericPart = s;
        }
        else
        {
            unit = char.ToLowerInvariant(lastChar) switch
            {
                's' => TimeSpan.FromSeconds(1),
                'm' => TimeSpan.FromMinutes(1),
                'h' => TimeSpan.FromHours(1),
                'd' => TimeSpan.FromDays(1),
                _   => TimeSpan.Zero, // sentinel for "unknown suffix"
            };
            if (unit == TimeSpan.Zero) return false;
            numericPart = s[..^1];
        }

        if (!long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return false;
        if (n < 0) return false; // NumberStyles.None already rejects '-', belt-and-braces.

        // TimeSpan overflow guard — `unit.Ticks * n` may overflow long for
        // absurd inputs like "999999999d". Caller gets `false` instead of an
        // exception bubbling out.
        try
        {
            duration = TimeSpan.FromTicks(checked(unit.Ticks * n));
            return true;
        }
        catch (OverflowException)
        {
            duration = TimeSpan.Zero;
            return false;
        }
    }

    /// <summary>
    /// Strict variant of <see cref="TryParse"/> — throws
    /// <see cref="FormatException"/> on malformed input. Useful when the
    /// caller has already validated upstream and only the success path is
    /// expected.
    /// </summary>
    public static TimeSpan Parse(string input) =>
        TryParse(input, out var d)
            ? d
            : throw new FormatException(
                $"invalid duration: '{input}'. Expected like '30s', '5m', '2h', '1d', or a bare integer (seconds).");

    /// <summary>
    /// Produce a short, human-readable rendering of <paramref name="t"/>
    /// using the same suffix grammar this parser accepts. Chooses the
    /// largest exact unit when possible, falling back to seconds for
    /// odd-remainder values.
    ///
    /// <para>Examples:</para>
    /// <list type="bullet">
    ///   <item>30 seconds → <c>30s</c></item>
    ///   <item>5 minutes  → <c>5m</c></item>
    ///   <item>90 minutes → <c>5400s</c> (not an exact hour count)</item>
    /// </list>
    /// </summary>
    public static string Format(TimeSpan t)
    {
        if (t.Ticks < 0) return $"-{Format(-t)}";

        // Prefer the largest unit that divides the span evenly so a
        // round-tripped value (Parse → Format) reads naturally.
        var totalSeconds = (long)t.TotalSeconds;
        if (totalSeconds == 0) return "0s";
        if (totalSeconds % 86_400 == 0) return $"{totalSeconds / 86_400}d";
        if (totalSeconds % 3_600  == 0) return $"{totalSeconds / 3_600}h";
        if (totalSeconds % 60     == 0) return $"{totalSeconds / 60}m";
        return $"{totalSeconds}s";
    }
}
