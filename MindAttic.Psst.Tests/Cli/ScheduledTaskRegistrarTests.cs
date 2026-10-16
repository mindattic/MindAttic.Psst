namespace MindAttic.Psst.Tests.Cli;

using MindAttic.Psst.Cli.Scheduling;
using NUnit.Framework;

/// <summary>
/// Covers the launcher-line escaping that protects scheduled message text from
/// cmd.exe's batch processing. The launcher .cmd is parsed by cmd before psst's
/// argv parser runs, so percent-expansion and the metacharacters &amp; | &lt; &gt;
/// ^ ( ) must be neutralized — double quotes alone do NOT stop percent-expansion.
/// </summary>
public class ScheduledTaskRegistrarTests
{
    [Test]
    public void BuildInvocationLine_DoublesLiteralPercent()
    {
        var line = ScheduledTaskRegistrar.BuildInvocationLine(
            @"C:\tools\psst.exe", new[] { "sms", "jordan", "50% done" });

        // "%" -> "%%" everywhere; cmd reduces "%%" back to a single "%" on a
        // batch line, so psst.exe ends up receiving the literal "50% done".
        Assert.That(line, Does.Contain("50%% done"));
        Assert.That(line, Does.Not.Contain("50% done"));
    }

    [Test]
    public void BuildInvocationLine_DoublesEnvVarPercentsSoNothingLeaks()
    {
        var line = ScheduledTaskRegistrar.BuildInvocationLine(
            @"C:\tools\psst.exe", new[] { "sms", "jordan", "path is %PATH% ok" });

        // %%PATH%% reduces to the literal "%PATH%" rather than expanding the
        // environment variable into the outgoing SMS.
        Assert.That(line, Does.Contain("%%PATH%%"));
    }

    [Test]
    public void QuoteForWindowsCommandLine_QuotesCmdMetacharacters()
    {
        // A bare "a&b" would make cmd run command `a` then command `b`; quoting
        // makes cmd treat the ampersand literally while CommandLineToArgvW still
        // yields a single argument.
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a&b"), Is.EqualTo("\"a&b\""));
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a|b"), Is.EqualTo("\"a|b\""));
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a>b"), Is.EqualTo("\"a>b\""));
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("a(b)"), Is.EqualTo("\"a(b)\""));
    }

    [Test]
    public void QuoteForWindowsCommandLine_PassesPlainTokensVerbatim()
    {
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("sms"), Is.EqualTo("sms"));
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("jordan"), Is.EqualTo("jordan"));
        Assert.That(ScheduledTaskRegistrar.QuoteForWindowsCommandLine("+15551234567"), Is.EqualTo("+15551234567"));
    }

    [Test]
    public void BuildInvocationLine_QuotesExePathAndCombinesWithMetacharArgs()
    {
        var line = ScheduledTaskRegistrar.BuildInvocationLine(
            @"C:\tools\psst.exe", new[] { "sms", "jordan", "a&b" });

        Assert.That(line, Does.StartWith("\"C:\\tools\\psst.exe\""));
        Assert.That(line, Does.Contain("\"a&b\""));
    }
}
