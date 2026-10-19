namespace MindAttic.Psst.Cli.Scheduling;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Psst's only door to Windows Task Scheduler. MindAttic.WatchDog is the one
/// thing that registers scheduled tasks (HOUSE-LAW-11), so Psst never calls
/// <c>schtasks</c> itself: it runs WatchDog's
/// <c>watchdog job add / cancel / list --owner Psst</c>, which keeps every
/// reminder at <c>\MindAttic\WatchDog\Psst\&lt;id&gt;</c>, run headless.
/// </summary>
public static class WatchDogClient
{
    /// <summary>The owner every Psst job is filed under.</summary>
    public const string Owner = "Psst";

    /// <summary>The Task Scheduler folder WatchDog keeps Psst's jobs in.</summary>
    public const string TaskFolder = @"\MindAttic\WatchDog\Psst\";

    /// <summary>
    /// Points at <c>watchdog.ps1</c> (or the folder holding it) when it isn't
    /// found next to psst, e.g. for an installed copy outside the workspace.
    /// </summary>
    public const string LocationVariable = "MINDATTIC_WATCHDOG";

    /// <summary>One <c>watchdog job list --json</c> row.</summary>
    public sealed record Job(string Name, string Task, DateTime? NextRun, string Command);

    /// <summary>Outcome of one WatchDog call: exit code and both streams.</summary>
    public sealed record Result(int ExitCode, string StdOut, string StdErr)
    {
        public bool Success => ExitCode == 0;

        /// <summary>WatchDog's own reason on failure, else whatever it printed.</summary>
        public string Error => (string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr).Trim();
    }

    /// <summary>
    /// Runs WatchDog with the given arguments. Replaceable so tests can see
    /// the exact argv without touching the real scheduler.
    /// </summary>
    internal static Func<IReadOnlyList<string>, Task<Result>> Runner { get; set; } = RunWatchDogAsync;

    /// <summary>
    /// Register a one-shot job <paramref name="id"/> that runs
    /// <paramref name="launcherPath"/> at <paramref name="fireTime"/>
    /// (local), taking over <paramref name="replaces"/> if given.
    /// </summary>
    public static Task<Result> AddAsync(string id, DateTime fireTime, string launcherPath, string? replaces = null)
    {
        var args = new List<string>
        {
            "job", "add", id,
            "--owner", Owner,
            "--at", fireTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            "--run", $"cmd.exe /c \"{launcherPath}\"",
            "--description", "Psst scheduled send",
        };
        if (replaces is not null)
        {
            args.Add("--replaces");
            args.Add(replaces);
        }
        return Runner(args);
    }

    /// <summary>Unregister job <paramref name="id"/>.</summary>
    public static Task<Result> CancelAsync(string id) =>
        Runner(new[] { "job", "cancel", id, "--owner", Owner });

    /// <summary>Every job WatchDog holds for Psst.</summary>
    public static async Task<IReadOnlyList<Job>> ListAsync()
    {
        var result = await Runner(new[] { "job", "list", "--owner", Owner, "--json" });
        if (!result.Success)
            throw new InvalidOperationException($"watchdog job list failed: {result.Error}");
        return ParseJobs(result.StdOut);
    }

    /// <summary>Parse the JSON <c>watchdog job list --json</c> prints.</summary>
    internal static IReadOnlyList<Job> ParseJobs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Job>();
        using var doc = JsonDocument.Parse(json);
        // ConvertTo-Json writes a lone row as an object rather than a one-item array.
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        var jobs = new List<Job>(rows.Count);
        foreach (var row in rows)
        {
            var name = Text(row, "name");
            if (name.Length == 0) continue;
            DateTime? nextRun = DateTime.TryParse(Text(row, "nextRun"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;
            jobs.Add(new Job(name, Text(row, "task"), nextRun, Text(row, "command")));
        }
        return jobs;

        static string Text(JsonElement row, string key) =>
            row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    /// <summary>
    /// The line a launcher <c>.cmd</c> runs to cancel its own job once the send
    /// is done. WatchDog leaves a run under way alone when its task is
    /// unregistered, so the launcher can do this from inside the job.
    /// </summary>
    public static string CancelCommandLine(string id) =>
        $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{LocateScript()}\" job cancel {id} --owner {Owner}";

    /// <summary>
    /// Find <c>watchdog.ps1</c>: <see cref="LocationVariable"/> first, then a
    /// <c>MindAttic.WatchDog</c> folder beside one of psst's parent folders
    /// (the workspace layout), then PATH.
    /// </summary>
    public static string LocateScript()
    {
        var configured = Environment.GetEnvironmentVariable(LocationVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var candidate = Directory.Exists(configured) ? Path.Combine(configured, "watchdog.ps1") : configured;
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            throw new FileNotFoundException($"{LocationVariable} is set to '{configured}', but there is no watchdog.ps1 there.");
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MindAttic.WatchDog", "watchdog.ps1");
            if (File.Exists(candidate)) return candidate;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), "watchdog.ps1");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"MindAttic.WatchDog's watchdog.ps1 was not found next to psst or on PATH; set {LocationVariable} to its path.");
    }

    private static async Task<Result> RunWatchDogAsync(IReadOnlyList<string> args)
    {
        string script;
        try { script = LocateScript(); }
        catch (FileNotFoundException ex) { return new Result(1, "", ex.Message); }

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
            psi.ArgumentList.Add(a);
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return new Result(1, "", "failed to launch powershell.exe");
            // Drain both streams concurrently so a full pipe can't deadlock the wait.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return new Result(proc.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return new Result(1, "", $"could not run WatchDog: {ex.Message}");
        }
    }
}
