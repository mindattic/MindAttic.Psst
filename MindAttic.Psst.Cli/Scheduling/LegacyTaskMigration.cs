namespace MindAttic.Psst.Cli.Scheduling;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// One-time move of reminders Psst scheduled before WatchDog took over task
/// registration. Those were registered by Psst itself at
/// <c>\MindAttic\Psst\MindAttic.Psst.&lt;id&gt;</c> and their launchers delete
/// themselves with <c>schtasks</c>. On the first run after the switch, each
/// one still due is re-created through <c>watchdog job add --replaces</c>
/// (which removes the old task, and its folder once empty) with its launcher
/// rewritten to cancel its WatchDog job instead. A marker file records that
/// the move is done, so later runs skip it.
/// </summary>
public static class LegacyTaskMigration
{
    private const string MarkerFileName = ".moved-to-watchdog";
    private const string LegacyTaskPrefix = @"\MindAttic\Psst\MindAttic.Psst.";

    /// <summary>
    /// Move legacy reminders once. Never throws: a failure is reported on
    /// stderr and the move is retried on the next run.
    /// </summary>
    public static async Task RunOnceAsync(string? directory = null)
    {
        var dir = directory ?? ScheduledTaskRegistrar.ScheduledDirectory();
        var marker = Path.Combine(dir, MarkerFileName);
        try
        {
            if (File.Exists(marker)) return;
            var failures = await MigrateAsync(dir, DateTime.Now);
            if (failures.Count == 0)
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(marker, $"Psst reminders moved to MindAttic.WatchDog {DateTime.Now:o}\n");
                return;
            }
            foreach (var f in failures)
                Console.Error.WriteLine($"psst: could not move scheduled send {f} to WatchDog; will retry next run");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"psst: could not move scheduled sends to WatchDog ({ex.Message}); will retry next run");
        }
    }

    /// <summary>
    /// Re-create every sidecar-described reminder still due after
    /// <paramref name="now"/> through WatchDog. Returns the ids that failed.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> MigrateAsync(string dir, DateTime now)
    {
        var failures = new List<string>();
        if (!Directory.Exists(dir)) return failures;

        foreach (var sidecar in Directory.EnumerateFiles(dir, "*.json"))
        {
            if (!TryReadLegacy(sidecar, out var id, out var fireTime, out var launcherPath)) continue;
            if (fireTime <= now || !File.Exists(launcherPath)) continue;

            try
            {
                await RewriteLauncherAsync(launcherPath, id);
                var added = await WatchDogClient.AddAsync(id, fireTime, launcherPath, replaces: LegacyTaskPrefix + id);
                if (!added.Success)
                {
                    failures.Add($"{id} ({added.Error})");
                    continue;
                }
                await RewriteSidecarTaskNameAsync(sidecar, WatchDogClient.TaskFolder + id);
            }
            catch (Exception ex)
            {
                failures.Add($"{id} ({ex.Message})");
            }
        }
        return failures;
    }

    /// <summary>
    /// Read a sidecar Psst wrote for a task it registered itself. Sidecars
    /// already pointing at WatchDog, or missing a field, are skipped.
    /// </summary>
    private static bool TryReadLegacy(string path, out string id, out DateTime fireTime, out string launcherPath)
    {
        id = ""; fireTime = default; launcherPath = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var taskName = root.TryGetProperty("taskName", out var tn) ? tn.GetString() ?? "" : "";
            if (!("\\" + taskName.TrimStart('\\')).StartsWith(LegacyTaskPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            launcherPath = root.TryGetProperty("launcherPath", out var lp) ? lp.GetString() ?? "" : "";
            var (_, fire) = ScheduledTaskLister.TryReadSidecar(path);
            if (id.Length == 0 || launcherPath.Length == 0 || fire is null) return false;
            fireTime = fire.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Swap the launcher's <c>schtasks /Delete</c> self-cleanup line for the
    /// WatchDog cancel of its new job; every other line stays as it was.
    /// </summary>
    internal static async Task RewriteLauncherAsync(string launcherPath, string id)
    {
        var cancel = ScheduledTaskRegistrar.SelfCancelLine(WatchDogClient.CancelCommandLine(id));
        var lines = (await File.ReadAllLinesAsync(launcherPath))
            .Select(l => l.TrimStart().StartsWith("schtasks /Delete", StringComparison.OrdinalIgnoreCase) ? cancel : l);
        await File.WriteAllTextAsync(launcherPath, string.Join("\r\n", lines) + "\r\n", Encoding.UTF8);
    }

    private static async Task RewriteSidecarTaskNameAsync(string sidecarPath, string taskName)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(sidecarPath))!.AsObject();
        node["taskName"] = taskName;
        await File.WriteAllTextAsync(sidecarPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }
}
