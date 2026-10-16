namespace MindAttic.Psst.Tests.Time;

using System;
using MindAttic.Psst.Time;
using NUnit.Framework;

/// <summary>
/// Behavior tests for <see cref="DurationParser"/>. These cover the
/// grammar advertised in the parser's XML docs — both the success forms
/// (bare integer, unit-suffixed integer, case-insensitive suffix) and the
/// rejection cases (null/whitespace, negatives, decimals, garbage).
/// </summary>
public class DurationParserTests
{
    [TestCase("30s", 30)]
    [TestCase("0s",  0)]
    [TestCase("30S", 30)]     // case-insensitive suffix
    [TestCase("1800", 1800)]  // bare integer = seconds
    [TestCase(" 30s ", 30)]   // whitespace is trimmed
    public void TryParse_AcceptsSecondForms(string input, long expectedSeconds)
    {
        Assert.That(DurationParser.TryParse(input, out var d), Is.True);
        Assert.That(d, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [TestCase("5m",  5)]
    [TestCase("30m", 30)]
    [TestCase("5M",  5)]
    public void TryParse_AcceptsMinuteForms(string input, long expectedMinutes)
    {
        Assert.That(DurationParser.TryParse(input, out var d), Is.True);
        Assert.That(d, Is.EqualTo(TimeSpan.FromMinutes(expectedMinutes)));
    }

    [TestCase("2h", 2)]
    [TestCase("2H", 2)]
    public void TryParse_AcceptsHourForms(string input, long expectedHours)
    {
        Assert.That(DurationParser.TryParse(input, out var d), Is.True);
        Assert.That(d, Is.EqualTo(TimeSpan.FromHours(expectedHours)));
    }

    [TestCase("1d", 1)]
    [TestCase("7D", 7)]
    public void TryParse_AcceptsDayForms(string input, long expectedDays)
    {
        Assert.That(DurationParser.TryParse(input, out var d), Is.True);
        Assert.That(d, Is.EqualTo(TimeSpan.FromDays(expectedDays)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("abc")]
    [TestCase("30x")]    // unknown suffix
    [TestCase("-30s")]   // negative
    [TestCase("1.5h")]   // decimal not supported
    [TestCase("m")]      // suffix only, no number
    public void TryParse_RejectsMalformed(string? input)
    {
        Assert.That(DurationParser.TryParse(input, out var d), Is.False);
        Assert.That(d, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Parse_ThrowsOnGarbage()
    {
        Assert.Throws<FormatException>(() => DurationParser.Parse("nope"));
    }

    [TestCase(30,        "30s")]
    [TestCase(60,        "1m")]
    [TestCase(5 * 60,    "5m")]
    [TestCase(60 * 60,   "1h")]
    [TestCase(2 * 3600,  "2h")]
    [TestCase(24 * 3600, "1d")]
    [TestCase(90,        "90s")]  // 1m30s → fall back to seconds (not an exact unit)
    [TestCase(0,         "0s")]
    public void Format_RoundTripsAndPicksLargestExactUnit(long seconds, string expected)
    {
        Assert.That(DurationParser.Format(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [TestCase(-30,        "-30s")]
    [TestCase(-5 * 60,    "-5m")]
    [TestCase(-24 * 3600, "-1d")]
    public void Format_NegativeSpansGetLeadingSign(long seconds, string expected)
    {
        Assert.That(DurationParser.Format(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [Test]
    public void Format_ExtremeValues_DoNotThrow()
    {
        // TimeSpan.MinValue negates to a value that overflows long ticks;
        // Format must render it rather than throwing OverflowException.
        var min = DurationParser.Format(TimeSpan.MinValue);
        Assert.That(min, Does.StartWith("-"));
        Assert.That(min, Does.EndWith("s"));

        var max = DurationParser.Format(TimeSpan.MaxValue);
        Assert.That(max, Does.EndWith("s"));
        Assert.That(max, Does.Not.Contain("-"));
    }

    [Test]
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
            Assert.That(DurationParser.TryParse(formatted, out var parsed), Is.True, $"could not re-parse '{formatted}'");
            Assert.That(parsed, Is.EqualTo(t));
        }
    }
}
