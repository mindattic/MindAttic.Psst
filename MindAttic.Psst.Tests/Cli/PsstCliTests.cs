namespace MindAttic.Psst.Tests.Cli;

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using MindAttic.Psst.Cli;
using Xunit;

public class PsstCliTests
{
    // ---- FormatElapsed: must be culture-invariant ('1.5s', never '1,5s') ----

    [Fact]
    public void FormatElapsed_SubMinute_UsesInvariantDecimalSeparator()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses ',' as the decimal separator — the regression we guard.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.5s", PsstCli.FormatElapsed(TimeSpan.FromSeconds(1.5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    [Fact]
    public void FormatElapsed_OverAMinute_RendersMinutesAndSeconds()
    {
        Assert.Equal("2m05s", PsstCli.FormatElapsed(TimeSpan.FromSeconds(125)));
    }

    // ---- ResolveExecutable: PATH x PATHEXT resolution for .cmd shims ----

    [Fact]
    public void ResolveExecutable_RootedPath_ReturnedVerbatim()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "whatever.exe");
        Assert.Equal(rooted, PsstCli.ResolveExecutable(rooted));
    }

    [Fact]
    public void ResolveExecutable_UnknownBareName_ReturnedUnchanged()
    {
        Assert.Equal("definitely-not-on-path-xyz", PsstCli.ResolveExecutable("definitely-not-on-path-xyz"));
    }

    [Fact]
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
            Assert.Equal(shim, PsstCli.ResolveExecutable("mytool"), ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", priorPath);
            Environment.SetEnvironmentVariable("PATHEXT", priorExt);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
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
            Assert.Equal(exe, PsstCli.ResolveExecutable("dup"), ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", priorPath);
            Environment.SetEnvironmentVariable("PATHEXT", priorExt);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
