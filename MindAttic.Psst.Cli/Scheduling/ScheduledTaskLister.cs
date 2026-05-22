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
/// Read-side counterpart to <see cref="ScheduledTaskRegistrar"/>. Lists
/// pending <c>MindAttic.Psst.*</c> entries currently registered with
/// Windows Task Scheduler, enriches each one with the launcher's JSON
/// sidecar (recipient, message, repeat, interval) when available, and
/// supports cancellation by task name.
///
/// <para>
/// Source of truth is the scheduler itself (<c>schtasks /Query</c>) — the
/// sidecar JSON files are only used to add the human-readable metadata.
/// This means already-fired tasks (which Task Scheduler auto-deletes via
/// <c>/Z</c>) will simply not appear, even if their sidecar files are
/// still on disk.
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
    /// Enumerate every currently-scheduled <c>MindAttic.Psst.*</c> task in
    /// the user's root Task Scheduler folder, in ascending NextRunTime
    /// order. Tasks not authored by Psst are filtered out.
    /// </summary>
    public static async Task<IReadOnlyList<PendingTask>> ListAsync()
    {
        var rows = await QuerySchtasksAsync();
        var dir = ScheduledTaskRegistrar.ScheduledDirectory();
        var results = new List<PendingTask>(capacity: rows.Count);

        foreach (var row in rows)
        {
            // schtasks prefixes task names with a "\" (folder root); strip
            // it before doing prefix matching.
            var taskName = row.TaskName.TrimStart('\\');
            if (!taskName.StartsWith("MindAttic.Psst.", StringComparison.Ordinal)) continue;

            var id = taskName["MindAttic.Psst.".Length..];
            var (meta, fireTime) = TryReadSidecar(Path.Combine(dir, $"{id}.json"));
            results.Add(new PendingTask(taskName, row.NextRunTime, row.TaskToRun, meta, fireTime));
        }

        return results
            .OrderBy(t => t.NextRunTime ?? t.FireTimeFromSidecar ?? DateTime.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Cancel a pending task by name. Calls <c>schtasks /Delete /F</c>
    /// and also removes the corresponding launcher <c>.cmd</c> and JSON
    /// sidecar (if present) so the scheduled folder doesn't accumulate
    /// orphaned files. Returns <c>null</c> on success or an error string.
    /// </summary>
    public static string? Cancel(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
            return "task name is required";
        if (!taskName.StartsWith("MindAttic.Psst.", StringComparison.Ordinal))
            return $"refusing to cancel '{taskName}' — only MindAttic.Psst.* tasks are managed by psst";

        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/Delete");
        psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(taskName);
        psi.ArgumentList.Add("/F");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return "failed to launch schtasks.exe";
            proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                return $"schtasks exited {proc.ExitCode}: {stderr.Trim()}";
        }
        catch (Exception ex)
        {
            return $"could not invoke schtasks.exe: {ex.Message}";
        }

        // Best-effort cleanup of the launcher + sidecar. Failures are
        // non-fatal — the user cares whether the task is gone, not
        // whether the paper trail is.
        var id = taskName["MindAttic.Psst.".Length..];
        var dir = ScheduledTaskRegistrar.ScheduledDirectory();
        TryDelete(Path.Combine(dir, $"{id}.cmd"));
        TryDelete(Path.Combine(dir, $"{id}.json"));
        return null;
    }

    /// <summary>
    /// Try to load a sidecar JSON from disk. Missing or malformed
    /// sidecars degrade gracefully — the caller just gets the bare
    /// schtasks row.
    /// </summary>
    private static (ScheduledTaskRegistrar.SchedulingMetadata? Metadata, DateTime? FireTime)
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

    /// <summary>
    /// One row from <c>schtasks /Query /FO CSV /V</c>. Only the columns
    /// we actually use are captured.
    /// </summary>
    private sealed record SchtasksRow(string TaskName, DateTime? NextRunTime, string TaskToRun);

    /// <summary>
    /// Shell out to <c>schtasks /Query /FO CSV /V</c> and parse the rows.
    /// CSV is locale-formatted (date format follows the user's culture),
    /// so we parse <c>Next Run Time</c> with <see cref="CultureInfo.CurrentCulture"/>
    /// and fall through to null on failure.
    /// </summary>
    private static async Task<IReadOnlyList<SchtasksRow>> QuerySchtasksAsync()
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("/Query");
        psi.ArgumentList.Add("/FO"); psi.ArgumentList.Add("CSV");
        psi.ArgumentList.Add("/V"); // verbose — needed for the "Task To Run" column
        psi.ArgumentList.Add("/NH"); // no header row (we hard-code column indices)

        using var proc = Process.Start(psi);
        if (proc is null) return Array.Empty<SchtasksRow>();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0) return Array.Empty<SchtasksRow>();

        var rows = new List<SchtasksRow>();
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim('\r');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var fields = ParseCsvLine(trimmed);
            // Column layout in /V mode (verified against Windows 10/11):
            //   0 HostName, 1 TaskName, 2 Next Run Time, 3 Status,
            //   4 Logon Mode, 5 Last Run Time, 6 Last Result, 7 Author,
            //   8 Task To Run, ...
            if (fields.Count < 9) continue;
            var taskName = fields[1];
            var taskToRun = fields[8];
            DateTime? nextRun = null;
            if (DateTime.TryParse(fields[2], CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
                nextRun = parsed;
            rows.Add(new SchtasksRow(taskName, nextRun, taskToRun));
        }
        return rows;
    }

    /// <summary>
    /// Minimal CSV line parser handling double-quoted fields with
    /// doubled <c>""</c> as an embedded quote. Sufficient for the
    /// schtasks output format; not a general-purpose CSV reader.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
