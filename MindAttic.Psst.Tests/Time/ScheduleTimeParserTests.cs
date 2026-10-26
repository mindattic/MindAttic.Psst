namespace MindAttic.Psst.Tests.Time;

using System;
using MindAttic.Psst.Time;
using NUnit.Framework;

/// <summary>
/// Behavior tests for <see cref="ScheduleTimeParser"/>: absolute dates, relative
/// offsets (including years ahead), time-of-day passthrough, and refusal of
/// past or malformed input. <c>now</c> is fixed so results are deterministic.
/// </summary>
public class ScheduleTimeParserTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 15, 30, DateTimeKind.Local);

    [TestCase("2027-03-01", 2027, 3, 1, 9, 0)]
    [TestCase("2027-03-01T14:30", 2027, 3, 1, 14, 30)]
    [TestCase("2027-03-01 14:30", 2027, 3, 1, 14, 30)]
    [TestCase("2031-12-31T23:59", 2031, 12, 31, 23, 59)]
    [TestCase("  2027-03-01  ", 2027, 3, 1, 9, 0)]
    public void Absolute_ResolvesToThatLocalTime(string input, int y, int mo, int d, int h, int mi)
    {
        Assert.That(ScheduleTimeParser.TryParse(input, Now, out var when, out var error), Is.True, error);
        Assert.That(when, Is.EqualTo(new DateTime(y, mo, d, h, mi, 0)));
        Assert.That(when.Kind, Is.EqualTo(DateTimeKind.Local));
    }

    [TestCase("+30d", "2026-11-25 09:16")]
    [TestCase("+2h", "2026-10-26 11:16")]
    [TestCase("+45m", "2026-10-26 10:01")]
    [TestCase("+2w", "2026-11-09 09:16")]
    [TestCase("+1y", "2027-10-26 09:16")]
    [TestCase("+5Y", "2031-10-26 09:16")]
    public void Relative_AddsTheOffsetAndRoundsUpToAWholeMinute(string input, string expected)
    {
        Assert.That(ScheduleTimeParser.TryParse(input, Now, out var when, out var error), Is.True, error);
        Assert.That(when.ToString("yyyy-MM-dd HH:mm"), Is.EqualTo(expected));
        Assert.That(when.Second, Is.EqualTo(0));
    }

    [TestCase("10:30am")]
    [TestCase("22:30")]
    [TestCase("2pm")]
    public void TimeOfDay_StillWorksAndMeansNextOccurrence(string input)
    {
        Assert.That(ScheduleTimeParser.TryParse(input, Now, out var when, out _), Is.True);
        Assert.That(when, Is.GreaterThan(Now));
        Assert.That(when - Now, Is.LessThanOrEqualTo(TimeSpan.FromDays(1)));
    }

    [TestCase("2026-10-25")]
    [TestCase("2026-10-26")]            // today at 09:00 has passed
    [TestCase("2026-10-26T09:15")]       // earlier this minute
    [TestCase("2020-01-01T00:00")]
    public void PastDatedTimes_AreRefused(string input)
    {
        Assert.That(ScheduleTimeParser.TryParse(input, Now, out var when, out var error), Is.False);
        Assert.That(error, Does.Contain("not in the future"));
        Assert.That(when, Is.EqualTo(DateTime.MinValue));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("soon")]
    [TestCase("2027-13-01")]
    [TestCase("2027-02-30")]
    [TestCase("2027-03-01T25:00")]
    [TestCase("+0d")]
    [TestCase("+d")]
    [TestCase("+-3d")]
    [TestCase("+30x")]
    [TestCase("+99999999y")]
    public void Malformed_IsRefusedWithAReason(string input)
    {
        Assert.That(ScheduleTimeParser.TryParse(input, Now, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void Parse_ThrowsFormatExceptionOnPast()
    {
        Assert.Throws<FormatException>(() => ScheduleTimeParser.Parse("2020-01-01", Now));
    }
}
