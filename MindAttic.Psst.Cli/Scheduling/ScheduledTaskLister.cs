namespace MindAttic.Psst.Cli.Scheduling;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Read-side counterpart to <see cref="ScheduledTaskRegistrar"/>. Lists
/// pending <c>\MindAttic\Scheduler\Psst\*</c> jobs through Scheduler
/// (<c>scheduler job list --owner Psst</c>), enriches each one with the
/// launcher's JSON sidecar (recipient, message, repeat, interval) when
/// available, and supports cancellation by task name.
///
/// <para>
/// Source of truth is Scheduler's job list — the sidecar JSON files are only
/// used to add the human-readable metadata. A job whose launcher already
/// fired cancels itself, so it simply will not appear, even if its sidecar
/// file is still on disk.
/// </para>
/// </summary>
public static class ScheduledTaskLister
{
    /// <summary>Single pending entry returned by <see cref="ListAsync"/>.</summary>
    public sealed record PendingTask(
        string TaskName,
        DateTime? NextRunTime,
        string LauncherPath,
        ScheduledTaskRegistrar.SchedulingMetadata? Metadata,
        DateTime? FireTimeFromSidecar);

    /// <summary>
    /// Enumerate every Psst job Scheduler holds, in ascending NextRunTime order.
    /// </summary>
    public static async Task<IReadOnlyList<PendingTask>> ListAsync()
    {
        var jobs = await SchedulerClient.ListAsync();
        var dir = ScheduledTaskRegistrar.ScheduledDirectory();
        var results = new List<PendingTask>(capacity: jobs.Count);

        foreach (var job in jobs)
        {
            var (meta, fireTime) = TryReadSidecar(Path.Combine(dir, $"{job.Name}.json"));
            var taskName = string.IsNullOrEmpty(job.Task) ? SchedulerClient.TaskFolder + job.Name : job.Task;
            results.Add(new PendingTask(taskName, job.NextRun, job.Command, meta, fireTime));
        }

        return results
            .OrderBy(t => t.NextRunTime ?? t.FireTimeFromSidecar ?? DateTime.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Cancel a pending job by task name (as <c>psst scheduled</c> prints it)
    /// or by its bare id, through <c>scheduler job cancel</c>, and remove the
    /// corresponding launcher <c>.cmd</c> and JSON sidecar (if present) so
    /// the scheduled folder doesn't accumulate orphaned files. Returns
    /// <c>null</c> on success or an error string.
    /// </summary>
    public static async Task<string?> CancelAsync(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
            return "task name is required";
        var id = JobId(taskName);
        if (id is null)
            return $"refusing to cancel '{taskName}' — only {SchedulerClient.TaskFolder}* jobs are managed by psst";

        var result = await SchedulerClient.CancelAsync(id);
        if (!result.Success)
            return result.Error;

        // Best-effort cleanup of the launcher + sidecar. Failures are
        // non-fatal — the user cares whether the job is gone, not
        // whether the paper trail is.
        var dir = ScheduledTaskRegistrar.ScheduledDirectory();
        TryDelete(Path.Combine(dir, $"{id}.cmd"));
        TryDelete(Path.Combine(dir, $"{id}.json"));
        return null;
    }

    /// <summary>
    /// The job id in <paramref name="taskName"/>: a bare id, or a name under
    /// <see cref="SchedulerClient.TaskFolder"/> (with or without the leading
    /// backslash). Null for anything else.
    /// </summary>
    internal static string? JobId(string taskName)
    {
        var name = taskName.Trim();
        var folder = SchedulerClient.TaskFolder;
        if (name.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            name = name[folder.Length..];
        else if (name.StartsWith(folder.TrimStart('\\'), StringComparison.OrdinalIgnoreCase))
            name = name[(folder.Length - 1)..];
        return name.Length > 0 && name.IndexOf('\\') < 0 ? name : null;
    }

    /// <summary>
    /// Try to load a sidecar JSON from disk. Missing or malformed
    /// sidecars degrade gracefully — the caller just gets the bare
    /// Scheduler row.
    /// </summary>
    internal static (ScheduledTaskRegistrar.SchedulingMetadata? Metadata, DateTime? FireTime)
        TryReadSidecar(string path)
    {
        if (!File.Exists(path)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var recipient = root.TryGetProperty("recipient", out var r) ? r.GetString() ?? "" : "";
            var message   = root.TryGetProperty("message",   out var m) ? m.GetString() ?? "" : "";
            var repeat    = root.TryGetProperty("repeat",    out var rep) && rep.TryGetInt32(out var n) ? n : 1;
            var interval  = root.TryGetProperty("intervalSeconds", out var iv) && iv.TryGetInt32(out var s) ? s : 0;
            DateTime? fireTime = null;
            if (root.TryGetProperty("fireTime", out var ft) && DateTime.TryParse(
                    ft.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                fireTime = parsed.ToLocalTime();
            }
            return (new ScheduledTaskRegistrar.SchedulingMetadata(recipient, message, repeat, interval), fireTime);
        }
        catch
        {
            return (null, null);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
