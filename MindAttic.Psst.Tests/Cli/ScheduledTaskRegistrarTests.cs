namespace MindAttic.Psst.Tests.Cli;

using MindAttic.Psst.Cli.Scheduling;
using Xunit;

/// <summary>
/// Covers the launcher-line escaping that protects scheduled message text from
/// cmd.exe's batch processing. The launcher .cmd is parsed by cmd before psst's
/// argv parser runs, so percent-expansion and the metacharacters &amp; | &lt; &gt;
/// ^ ( ) must be neutralized — double quotes alone do NOT stop percent-expansion.
/// </summary>
public class ScheduledTaskRegistrarTests
{
    [Fact]
    public void BuildInvocationLine_DoublesLiteralPercent()
    {
        var line = ScheduledTaskRegistrar.BuildInvocationLine(
            @"C:\tools\psst.exe", new[] { "sms", "jordan", "50% done" });

        // "%" -> "%%" everywhere; cmd reduces "%%" back to a single "%" on a
        // batch line, so psst.exe ends up receiving the literal "50% done".
        Assert.Contains("50%% done", line);
        Assert.DoesNotContain("50% done", line);
    }

    [Fact]
    public void BuildInvocationLine_DoublesEnvVarPercentsSoNothingLeaks()
    {
        var line = ScheduledTaskRegistrar.BuildInvocationLine(
            @"C:\tools\psst.exe", new[] { "sms", "jordan", "path is %PATH% ok" });

        // %%PATH%% reduces to the literal "%PATH%" rather than expanding the
        // environment variable into the outgoing SMS.
        Assert.Contains("%%PATH%%", line);
    }

    [Fact]
    public void QuoteForWindowsCommandLine_QuotesCmdMetacharacters()
    {
        // A bare "a&b" would make cmd run command `a` then command `b`; quoting
        // makes cmd treat the ampersand literally while CommandLineToArgvW still
        // yields a single argument.
        Assert.Equal("\"a&b\"", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a&b"));
        Assert.Equal("\"a|b\"", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a|b"));
        Assert.Equal("\"a>b\"", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a>b"));
        Assert.Equal("\"a(b)\"", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a(b)"));
    }

    [Fact]
    public void QuoteForWindowsCommandLine_PassesPlainTokensVerbatim()
    {
        Assert.Equal("sms", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("sms"));
        Assert.Equal("jordan", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("jordan"));
        Assert.Equal("+15551234567", ScheduledTaskRegistrar.QuoteForWindowsCommandLine("+15551234567"));
    }

    [Fact]
    public void BuildInvocationLine_QuotesExePathAndCombinesWithMetacharArgs()
    {
        var line = ScheduledTaskRegistrar.BuildInvocationLine(
            @"C:\tools\psst.exe", new[] { "sms", "jordan", "a&b" });

        Assert.StartsWith("\"C:\\tools\\psst.exe\"", line);
        Assert.Contains("\"a&b\"", line);
    }
}
