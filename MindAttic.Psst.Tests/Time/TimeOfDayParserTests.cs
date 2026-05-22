namespace MindAttic.Psst.Tests.Time;

using System;
using MindAttic.Psst.Time;
using Xunit;

/// <summary>
/// Behavior tests for <see cref="TimeOfDayParser"/>. The <c>now</c> pivot
/// is fixed in each test so "next occurrence" semantics are deterministic
/// regardless of when the suite runs.
/// </summary>
public class TimeOfDayParserTests
{
    private static readonly DateTime Now = new(2026, 5, 22, 9, 0, 0, DateTimeKind.Local);

    [Theory]
    [InlineData("10:30am", 10, 30)]
    [InlineData("10:30 AM", 10, 30)]
    [InlineData("12:00am",  0,  0)]  // midnight
    [InlineData("12:00pm", 12,  0)]  // noon
    [InlineData("2:30pm",  14, 30)]
    public void TryParse_TwelveHour(string input, int expectedHour, int expectedMinute)
    {
        Assert.True(TimeOfDayParser.TryParse(input, Now, out var when));
        Assert.Equal(expectedHour, when.Hour);
        Assert.Equal(expectedMinute, when.Minute);
        Assert.Equal(DateTimeKind.Local, when.Kind);
    }

    [Theory]
    [InlineData("10:30",  10, 30)]
    [InlineData("22:30",  22, 30)]
    [InlineData("00:00",   0,  0)]
    [InlineData("23:59",  23, 59)]
    public void TryParse_TwentyFourHour(string input, int expectedHour, int expectedMinute)
    {
        Assert.True(TimeOfDayParser.TryParse(input, Now, out var when));
        Assert.Equal(expectedHour, when.Hour);
        Assert.Equal(expectedMinute, when.Minute);
    }

    [Theory]
    [InlineData("10am", 10)]
    [InlineData("2pm",  14)]
    public void TryParse_WholeHourShortcut(string input, int expectedHour)
    {
        Assert.True(TimeOfDayParser.TryParse(input, Now, out var when));
        Assert.Equal(expectedHour, when.Hour);
        Assert.Equal(0, when.Minute);
    }

    [Fact]
    public void TryParse_NextOccurrence_TodayWhenFuture()
    {
        // Now = 09:00, asking for 10:30 → today.
        Assert.True(TimeOfDayParser.TryParse("10:30am", Now, out var when));
        Assert.Equal(Now.Date, when.Date);
        Assert.Equal(10, when.Hour);
    }

    [Fact]
    public void TryParse_NextOccurrence_TomorrowWhenPast()
    {
        // Now = 09:00, asking for 08:30 → tomorrow.
        Assert.True(TimeOfDayParser.TryParse("8:30am", Now, out var when));
        Assert.Equal(Now.Date.AddDays(1), when.Date);
        Assert.Equal(8, when.Hour);
        Assert.Equal(30, when.Minute);
    }

    [Fact]
    public void TryParse_NextOccurrence_TomorrowWhenSameMinute()
    {
        // Now = 09:00, asking for 09:00 → tomorrow (strict ">" rollover).
        Assert.True(TimeOfDayParser.TryParse("9:00am", Now, out var when));
        Assert.Equal(Now.Date.AddDays(1), when.Date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("25:00")]    // 24h out of range
    [InlineData("13:00pm")]  // 12h hour out of range
    [InlineData("0:30am")]   // 12h hour cannot be 0
    [InlineData("10:60am")]  // minute out of range
    [InlineData("am")]       // marker only
    [InlineData(":30")]      // missing hour
    [InlineData("10:")]      // missing minute
    public void TryParse_RejectsMalformed(string? input)
    {
        Assert.False(TimeOfDayParser.TryParse(input, Now, out var when));
        Assert.Equal(DateTime.MinValue, when);
    }

    [Fact]
    public void Parse_ThrowsOnGarbage()
    {
        Assert.Throws<FormatException>(() => TimeOfDayParser.Parse("nope", Now));
    }
}
