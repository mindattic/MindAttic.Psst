namespace MindAttic.Psst.Tests.Time;

using System;
using MindAttic.Psst.Time;
using Xunit;

/// <summary>
/// Behavior tests for <see cref="DurationParser"/>. These cover the
/// grammar advertised in the parser's XML docs — both the success forms
/// (bare integer, unit-suffixed integer, case-insensitive suffix) and the
/// rejection cases (null/whitespace, negatives, decimals, garbage).
/// </summary>
public class DurationParserTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("0s",  0)]
    [InlineData("30S", 30)]     // case-insensitive suffix
    [InlineData("1800", 1800)]  // bare integer = seconds
    [InlineData(" 30s ", 30)]   // whitespace is trimmed
    public void TryParse_AcceptsSecondForms(string input, long expectedSeconds)
    {
        Assert.True(DurationParser.TryParse(input, out var d));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), d);
    }

    [Theory]
    [InlineData("5m",  5)]
    [InlineData("30m", 30)]
    [InlineData("5M",  5)]
    public void TryParse_AcceptsMinuteForms(string input, long expectedMinutes)
    {
        Assert.True(DurationParser.TryParse(input, out var d));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), d);
    }

    [Theory]
    [InlineData("2h", 2)]
    [InlineData("2H", 2)]
    public void TryParse_AcceptsHourForms(string input, long expectedHours)
    {
        Assert.True(DurationParser.TryParse(input, out var d));
        Assert.Equal(TimeSpan.FromHours(expectedHours), d);
    }

    [Theory]
    [InlineData("1d", 1)]
    [InlineData("7D", 7)]
    public void TryParse_AcceptsDayForms(string input, long expectedDays)
    {
        Assert.True(DurationParser.TryParse(input, out var d));
        Assert.Equal(TimeSpan.FromDays(expectedDays), d);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("30x")]    // unknown suffix
    [InlineData("-30s")]   // negative
    [InlineData("1.5h")]   // decimal not supported
    [InlineData("m")]      // suffix only, no number
    public void TryParse_RejectsMalformed(string? input)
    {
        Assert.False(DurationParser.TryParse(input, out var d));
        Assert.Equal(TimeSpan.Zero, d);
    }

    [Fact]
    public void Parse_ThrowsOnGarbage()
    {
        Assert.Throws<FormatException>(() => DurationParser.Parse("nope"));
    }

    [Theory]
    [InlineData(30,        "30s")]
    [InlineData(60,        "1m")]
    [InlineData(5 * 60,    "5m")]
    [InlineData(60 * 60,   "1h")]
    [InlineData(2 * 3600,  "2h")]
    [InlineData(24 * 3600, "1d")]
    [InlineData(90,        "90s")]  // 1m30s → fall back to seconds (not an exact unit)
    [InlineData(0,         "0s")]
    public void Format_RoundTripsAndPicksLargestExactUnit(long seconds, string expected)
    {
        Assert.Equal(expected, DurationParser.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        // Property-style: anything Format emits must Parse back to the same span.
        TimeSpan[] spans = [
            TimeSpan.FromSeconds(45),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromDays(1),
        ];
        foreach (var t in spans)
        {
            var formatted = DurationParser.Format(t);
            Assert.True(DurationParser.TryParse(formatted, out var parsed),
                $"could not re-parse '{formatted}'");
            Assert.Equal(t, parsed);
        }
    }
}
