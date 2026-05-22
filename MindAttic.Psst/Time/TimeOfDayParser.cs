namespace MindAttic.Psst.Time;

using System;
using System.Globalization;

/// <summary>
/// Parses wall-clock time strings used by the <c>--schedule</c> / <c>--start</c>
/// flag and resolves them to a concrete <see cref="DateTime"/> in
/// <see cref="DateTimeKind.Local"/>.
///
/// <para>Accepted forms (case-insensitive, whitespace trimmed):</para>
/// <list type="bullet">
///   <item><c>10:30am</c>, <c>10:30 AM</c>, <c>2:30pm</c> — 12-hour with am/pm marker</item>
///   <item><c>10:30</c>, <c>22:30</c>           — 24-hour, no am/pm</item>
///   <item><c>10am</c>, <c>2pm</c>              — whole-hour 12-hour shortcut</item>
/// </list>
///
/// <para>
/// Resolution semantics: the parser computes the <em>next future
/// occurrence</em> of the given wall-clock time. If the time has already
/// passed today, it rolls forward to tomorrow. Seconds always normalize
/// to <c>:00</c>.
/// </para>
/// </summary>
public static class TimeOfDayParser
{
    /// <summary>
    /// Try to parse <paramref name="input"/> as a wall-clock time and
    /// resolve it to the next future local <see cref="DateTime"/> relative
    /// to <paramref name="now"/>. Returns <c>false</c> on any malformed
    /// input; <paramref name="when"/> is <see cref="DateTime.MinValue"/>
    /// on failure.
    /// </summary>
    /// <param name="input">User-supplied time string.</param>
    /// <param name="now">
    /// "Current" local time used as the rollover pivot. Injectable for
    /// deterministic tests; production callers pass <see cref="DateTime.Now"/>.
    /// </param>
    /// <param name="when">
    /// The resolved firing time on success — <see cref="DateTimeKind.Local"/>,
    /// always strictly &gt; <paramref name="now"/>.
    /// </param>
    public static bool TryParse(string? input, DateTime now, out DateTime when)
    {
        when = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim().ToLowerInvariant();
        if (s.Length == 0) return false;

        // Detect and strip the am/pm marker. We use a flag rather than an
        // enum so the "no marker → 24h" path is just `meridiem == null`.
        bool? isPm = null;
        if (s.EndsWith("am", StringComparison.Ordinal))
        {
            isPm = false;
            s = s[..^2].TrimEnd();
        }
        else if (s.EndsWith("pm", StringComparison.Ordinal))
        {
            isPm = true;
            s = s[..^2].TrimEnd();
        }

        if (s.Length == 0) return false;

        // Hour / minute split. Missing colon = whole-hour shortcut ("10am").
        int hour, minute;
        var colonIdx = s.IndexOf(':');
        if (colonIdx < 0)
        {
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out hour))
                return false;
            minute = 0;
        }
        else
        {
            var hourPart = s[..colonIdx];
            var minutePart = s[(colonIdx + 1)..];
            if (!int.TryParse(hourPart, NumberStyles.None, CultureInfo.InvariantCulture, out hour))
                return false;
            if (!int.TryParse(minutePart, NumberStyles.None, CultureInfo.InvariantCulture, out minute))
                return false;
        }

        if (minute is < 0 or > 59) return false;

        // Normalize hour into 24h based on the meridiem (or validate the
        // 24h range when no marker is present).
        if (isPm.HasValue)
        {
            // 12-hour clock: legal hours are 1..12. 12am → 00, 12pm → 12,
            // otherwise add 12 for pm.
            if (hour is < 1 or > 12) return false;
            if (isPm.Value)        hour = hour == 12 ? 12 : hour + 12;
            else /* am */          hour = hour == 12 ? 0  : hour;
        }
        else
        {
            // 24-hour clock.
            if (hour is < 0 or > 23) return false;
        }

        // Build today's candidate, then roll forward to tomorrow if it has
        // already passed. Strict ">" keeps the schedule from firing the
        // very same second we registered it (which would race the schtasks
        // /SC ONCE registration on Windows).
        var todayCandidate = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0, DateTimeKind.Local);
        when = todayCandidate > now
            ? todayCandidate
            : todayCandidate.AddDays(1);
        return true;
    }

    /// <summary>
    /// Strict variant of <see cref="TryParse"/> — throws
    /// <see cref="FormatException"/> on malformed input.
    /// </summary>
    public static DateTime Parse(string input, DateTime now) =>
        TryParse(input, now, out var when)
            ? when
            : throw new FormatException(
                $"invalid time: '{input}'. Expected like '10:30am', '2:30pm', '10:30', '22:30', or '10am'.");
}
