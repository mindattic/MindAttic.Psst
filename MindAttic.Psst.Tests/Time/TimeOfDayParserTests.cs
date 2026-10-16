namespace MindAttic.Psst.Tests.Time;

using System;
using MindAttic.Psst.Time;
using NUnit.Framework;

/// <summary>
/// Behavior tests for <see cref="TimeOfDayParser"/>. The <c>now</c> pivot
/// is fixed in each test so "next occurrence" semantics are deterministic
/// regardless of when the suite runs.
/// </summary>
public class TimeOfDayParserTests
{
    private static readonly DateTime Now = new(2026, 5, 22, 9, 0, 0, DateTimeKind.Local);

    [TestCase("10:30am", 10, 30)]
    [TestCase("10:30 AM", 10, 30)]
    [TestCase("12:00am",  0,  0)]  // midnight
    [TestCase("12:00pm", 12,  0)]  // noon
    [TestCase("2:30pm",  14, 30)]
    public void TryParse_TwelveHour(string input, int expectedHour, int expectedMinute)
    {
        Assert.That(TimeOfDayParser.TryParse(input, Now, out var when), Is.True);
        Assert.That(when.Hour, Is.EqualTo(expectedHour));
        Assert.That(when.Minute, Is.EqualTo(expectedMinute));
        Assert.That(when.Kind, Is.EqualTo(DateTimeKind.Local));
    }

    [TestCase("10:30",  10, 30)]
    [TestCase("22:30",  22, 30)]
    [TestCase("00:00",   0,  0)]
    [TestCase("23:59",  23, 59)]
    public void TryParse_TwentyFourHour(string input, int expectedHour, int expectedMinute)
    {
        Assert.That(TimeOfDayParser.TryParse(input, Now, out var when), Is.True);
        Assert.That(when.Hour, Is.EqualTo(expectedHour));
        Assert.That(when.Minute, Is.EqualTo(expectedMinute));
    }

    [TestCase("10am", 10)]
    [TestCase("2pm",  14)]
    public void TryParse_WholeHourShortcut(string input, int expectedHour)
    {
        Assert.That(TimeOfDayParser.TryParse(input, Now, out var when), Is.True);
        Assert.That(when.Hour, Is.EqualTo(expectedHour));
        Assert.That(when.Minute, Is.EqualTo(0));
    }

    [Test]
    public void TryParse_NextOccurrence_TodayWhenFuture()
    {
        // Now = 09:00, asking for 10:30 → today.
        Assert.That(TimeOfDayParser.TryParse("10:30am", Now, out var when), Is.True);
        Assert.That(when.Date, Is.EqualTo(Now.Date));
        Assert.That(when.Hour, Is.EqualTo(10));
    }

    [Test]
    public void TryParse_NextOccurrence_TomorrowWhenPast()
    {
        // Now = 09:00, asking for 08:30 → tomorrow.
        Assert.That(TimeOfDayParser.TryParse("8:30am", Now, out var when), Is.True);
        Assert.That(when.Date, Is.EqualTo(Now.Date.AddDays(1)));
        Assert.That(when.Hour, Is.EqualTo(8));
        Assert.That(when.Minute, Is.EqualTo(30));
    }

    [Test]
    public void TryParse_NextOccurrence_TomorrowWhenSameMinute()
    {
        // Now = 09:00, asking for 09:00 → tomorrow (strict ">" rollover).
        Assert.That(TimeOfDayParser.TryParse("9:00am", Now, out var when), Is.True);
        Assert.That(when.Date, Is.EqualTo(Now.Date.AddDays(1)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nope")]
    [TestCase("25:00")]    // 24h out of range
    [TestCase("13:00pm")]  // 12h hour out of range
    [TestCase("0:30am")]   // 12h hour cannot be 0
    [TestCase("10:60am")]  // minute out of range
    [TestCase("am")]       // marker only
    [TestCase(":30")]      // missing hour
    [TestCase("10:")]      // missing minute
    public void TryParse_RejectsMalformed(string? input)
    {
        Assert.That(TimeOfDayParser.TryParse(input, Now, out var when), Is.False);
        Assert.That(when, Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void Parse_ThrowsOnGarbage()
    {
        Assert.Throws<FormatException>(() => TimeOfDayParser.Parse("nope", Now));
    }
}
