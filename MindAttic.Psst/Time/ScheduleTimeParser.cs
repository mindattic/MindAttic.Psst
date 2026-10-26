namespace MindAttic.Psst.Time;

using System;
using System.Globalization;

/// <summary>
/// Resolves the <c>--schedule</c> / <c>--start</c> / <c>--at</c> value to a
/// concrete local <see cref="DateTime"/>, any time in the future (days, months,
/// years ahead).
///
/// <para>Accepted forms (case-insensitive, whitespace trimmed):</para>
/// <list type="bullet">
///   <item><c>yyyy-MM-dd</c> — that date at 09:00</item>
///   <item><c>yyyy-MM-ddTHH:mm</c>, <c>yyyy-MM-dd HH:mm</c> — that exact local time</item>
///   <item><c>+30d</c>, <c>+2h</c>, <c>+45m</c>, <c>+2w</c>, <c>+1y</c> — an offset from now</item>
///   <item>anything <see cref="TimeOfDayParser"/> takes (<c>10:30am</c>, <c>22:30</c>) — next occurrence</item>
/// </list>
/// Seconds always normalize to <c>:00</c>. A dated time that is not in the future is refused.
/// </summary>
public static class ScheduleTimeParser
{
    /// <summary>The hour a bare date fires at.</summary>
    public const int DefaultHour = 9;

    private static readonly string[] DateTimeFormats = { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm", "yyyy-M-d'T'H:mm", "yyyy-M-d H:mm" };
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-M-d" };

    /// <summary>
    /// Try to resolve <paramref name="input"/> relative to <paramref name="now"/>.
    /// On failure <paramref name="error"/> says why and <paramref name="when"/> is
    /// <see cref="DateTime.MinValue"/>.
    /// </summary>
    public static bool TryParse(string? input, DateTime now, out DateTime when, out string? error)
    {
        when = DateTime.MinValue;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "a time is required";
            return false;
        }
        var s = input.Trim();

        if (s[0] == '+') return TryOffset(s, now, out when, out error);

        // Dated forms start with a four-digit year and a dash.
        if (LooksDated(s))
            return TryDated(s, now, out when, out error);

        if (TimeOfDayParser.TryParse(s, now, out when)) return true;

        error = $"'{input}' is not a time: use 10:30am, 22:30, 2027-03-01, 2027-03-01T14:30, or +30d / +2h / +1y";
        return false;
    }

    /// <summary>Strict variant: throws <see cref="FormatException"/> with the reason.</summary>
    public static DateTime Parse(string input, DateTime now) =>
        TryParse(input, now, out var when, out var error) ? when : throw new FormatException(error);

    private static bool LooksDated(string s) =>
        s.Length >= 8 && char.IsDigit(s[0]) && char.IsDigit(s[1]) && char.IsDigit(s[2]) && char.IsDigit(s[3]) && s[4] == '-';

    private static bool TryDated(string s, DateTime now, out DateTime when, out string? error)
    {
        when = DateTime.MinValue;
        error = null;
        DateTime parsed;
        if (DateTime.TryParseExact(s, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            parsed = dt;
        else if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            parsed = d.AddHours(DefaultHour);
        else
        {
            error = $"'{s}' is not a date: use yyyy-MM-dd, yyyy-MM-ddTHH:mm or yyyy-MM-dd HH:mm";
            return false;
        }

        parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
        if (parsed <= now)
        {
            error = $"{parsed:yyyy-MM-dd HH:mm} is not in the future";
            return false;
        }
        when = parsed;
        return true;
    }

    private static bool TryOffset(string s, DateTime now, out DateTime when, out string? error)
    {
        when = DateTime.MinValue;
        error = null;
        var unit = char.ToLowerInvariant(s[^1]);
        if (s.Length < 3 || !int.TryParse(s[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
        {
            error = $"'{s}' is not an offset: use +30d, +2h, +45m, +2w or +1y";
            return false;
        }

        try
        {
            var target = unit switch
            {
                'm' => now.AddMinutes(n),
                'h' => now.AddHours(n),
                'd' => now.AddDays(n),
                'w' => now.AddDays(7.0 * n),
                'y' => now.AddYears(n),
                _ => (DateTime?)null,
            };
            if (target is null)
            {
                error = $"'{s}' has an unknown unit '{s[^1]}': use m, h, d, w or y";
                return false;
            }
            // Drop seconds, then round up to the next whole minute: jobs fire on whole minutes.
            var t = target.Value;
            t = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Local);
            if (t < target.Value) t = t.AddMinutes(1);
            when = t;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = $"'{s}' is too far ahead";
            return false;
        }
    }
}
