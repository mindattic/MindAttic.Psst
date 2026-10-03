namespace MindAttic.Psst.Tests;

using System;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Repo-level guards for the PowerShell hooks Claude Code runs. Windows PowerShell 5.1 reads a
/// BOM-less script as the ANSI code page, so any non-ASCII character in one (an em dash, say)
/// is decoded as mojibake and leaks into the hook output.
/// </summary>
public class RepoScriptsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindAttic.Psst.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MindAttic.Psst.slnx not found above the test output.");
    }

    [Fact]
    public void SessionStartHook_IsPureAscii()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), ".claude", "hooks", "inject-digest.ps1"));
        var bad = Array.FindIndex(bytes, b => b > 0x7F);
        Assert.True(bad < 0, $"non-ASCII byte 0x{(bad < 0 ? 0 : bytes[bad]):X2} at offset {bad}");
    }

    [Fact]
    public void BomLessClaudeScripts_AreAscii()
    {
        var dir = Path.Combine(RepoRoot(), ".claude");
        var offenders = Directory.EnumerateFiles(dir, "*.ps1", SearchOption.AllDirectories)
            .Where(f =>
            {
                var b = File.ReadAllBytes(f);
                var hasBom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
                return !hasBom && b.Any(x => x > 0x7F);
            })
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Empty(offenders);
    }
}
