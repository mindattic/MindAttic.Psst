namespace MindAttic.Psst.Tests.Cli;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using MindAttic.Psst.Cli;
using NUnit.Framework;

public class PsstCliTests
{
    // ---- FormatElapsed: must be culture-invariant ('1.5s', never '1,5s') ----

    [Test]
    public void FormatElapsed_SubMinute_UsesInvariantDecimalSeparator()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses ',' as the decimal separator — the regression we guard.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.That(PsstCli.FormatElapsed(TimeSpan.FromSeconds(1.5)), Is.EqualTo("1.5s"));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    [Test]
    public void FormatElapsed_OverAMinute_RendersMinutesAndSeconds()
    {
        Assert.That(PsstCli.FormatElapsed(TimeSpan.FromSeconds(125)), Is.EqualTo("2m05s"));
    }

    // ---- ResolveExecutable: PATH x PATHEXT resolution for .cmd shims ----

    [Test]
    public void ResolveExecutable_RootedPath_ReturnedVerbatim()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "whatever.exe");
        Assert.That(PsstCli.ResolveExecutable(rooted), Is.EqualTo(rooted));
    }

    [Test]
    public void ResolveExecutable_UnknownBareName_ReturnedUnchanged()
    {
        Assert.That(PsstCli.ResolveExecutable("definitely-not-on-path-xyz"), Is.EqualTo("definitely-not-on-path-xyz"));
    }

    [Test]
    public void ResolveExecutable_BareName_ResolvesToCmdShimViaPathExt()
    {
        // A bare "npm"-style name whose only on-disk form is a .cmd shim must
        // resolve to the full .cmd path (the npm/yarn/tsc breakage).
        var dir = Path.Combine(Path.GetTempPath(), $"psst-resolve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "mytool.cmd");
        File.WriteAllText(shim, "@echo off\r\nexit /b 0\r\n");

        var priorPath = Environment.GetEnvironmentVariable("PATH");
        var priorExt = Environment.GetEnvironmentVariable("PATHEXT");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + (priorPath ?? ""));
            Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT;.CMD");

            // PATHEXT contributes the extension casing (".CMD"); Windows paths
            // are case-insensitive, so compare accordingly.
            Assert.That(PsstCli.ResolveExecutable("mytool"), Is.EqualTo(shim).IgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", priorPath);
            Environment.SetEnvironmentVariable("PATHEXT", priorExt);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void ResolveExecutable_PrefersExeOverCmdWhenBothPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"psst-resolve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dup.cmd"), "@echo off\r\n");
        var exe = Path.Combine(dir, "dup.exe");
        File.WriteAllText(exe, "stub");

        var priorPath = Environment.GetEnvironmentVariable("PATH");
        var priorExt = Environment.GetEnvironmentVariable("PATHEXT");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + (priorPath ?? ""));
            Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT;.CMD");

            // .EXE precedes .CMD in PATHEXT, so the exe wins.
            Assert.That(PsstCli.ResolveExecutable("dup"), Is.EqualTo(exe).IgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", priorPath);
            Environment.SetEnvironmentVariable("PATHEXT", priorExt);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---- usage text: credential sources must be the real lookup chain ----

    [Test]
    public async Task Usage_ListsTheRealCredentialChain_NotUserSecrets()
    {
        var prior = Console.Out;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            Assert.That(await new PsstCli().RunAsync(new[] { "--help" }), Is.EqualTo(0));
        }
        finally
        {
            Console.SetOut(prior);
        }

        var text = sw.ToString();
        Assert.That(text, Does.Not.Contain("User Secrets").IgnoreCase);
        var order = new[] { "vault files", "appsettings.json", @"Psst\settings.json", "env vars" }
            .Select(s => text.IndexOf(s, StringComparison.Ordinal)).ToArray();
        Assert.That(order, Is.All.GreaterThanOrEqualTo(0));
        Assert.That(order, Is.EqualTo(order.OrderBy(i => i))); // lowest -> highest precedence, as BuildConfiguration adds them
        Assert.That(text, Does.Contain("MindAttic:Vault:Notifications"));
    }
}
