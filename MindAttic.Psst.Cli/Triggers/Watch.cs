namespace MindAttic.Psst.Cli.Triggers;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using MindAttic.Psst.Cli.Scheduling;

/// <summary>
/// One <c>psst when</c> watch (M.PSST-T41): the trigger it waits for, what to announce, and what
/// it has seen so far. Kept as <c>&lt;id&gt;.watch.json</c> beside the launcher its Scheduler job
/// runs every <see cref="PollSeconds"/>.
/// </summary>
public sealed class Watch
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();

    /// <summary>What to announce; empty means the trigger's own sentence.</summary>
    public string Message { get; set; } = "";

    /// <summary>False: announce once, then remove the watch. True: keep watching.</summary>
    public bool Recurring { get; set; }

    public int PollSeconds { get; set; } = 60;

    /// <summary>A recurring watch stays quiet this long after announcing.</summary>
    public int CooldownSeconds { get; set; }

    public bool Silent { get; set; }
    public DateTime Created { get; set; }

    /// <summary>The watch removes itself, unannounced, once this passes.</summary>
    public DateTime? Expires { get; set; }

    /// <summary>For a condition: it has been false since the last announcement, so turning true announces.</summary>
    public bool Armed { get; set; } = true;

    /// <summary>For a change: the value last seen.</summary>
    public string? Fingerprint { get; set; }

    public DateTime? LastFired { get; set; }
    public int FireCount { get; set; }
    public DateTime? LastChecked { get; set; }
    public string? LastDetail { get; set; }
    public string? LastError { get; set; }

    /// <summary><c>Trigger</c> or <c>Trigger(a, b)</c>.</summary>
    public string Describe() => TriggerCatalog.Describe(Trigger, Args);

    /// <summary>The words that go out: the owner's own, else the trigger's sentence.</summary>
    public string Announcement(TriggerDef def, TriggerProbe probe)
    {
        var text = Message.Trim().Length > 0 ? Message.Trim() : def.DefaultMessage?.Invoke(Args) ?? Describe();
        return def.Kind == TriggerKind.Change && probe.Detail.Length > 0 ? $"{text} ({probe.Detail})" : text;
    }

    /// <summary>
    /// Whether this look calls for an announcement. Changes nothing but <see cref="Armed"/> (a
    /// condition seen false re-arms) and a first <see cref="Fingerprint"/>; <see cref="Fired"/>
    /// records the announcement once it has really gone out, so one that failed to send is tried
    /// again at the next look.
    /// </summary>
    public bool ShouldFire(TriggerDef def, TriggerProbe probe, DateTime now)
    {
        if (!probe.Ok) return false;
        bool fire;
        switch (def.Kind)
        {
            case TriggerKind.Condition:
                if (!probe.Holds) { Armed = true; return false; }
                fire = Armed;
                break;
            case TriggerKind.Change:
                if (Fingerprint is null) { Fingerprint = probe.Fingerprint; return false; }
                fire = !string.Equals(Fingerprint, probe.Fingerprint, StringComparison.Ordinal);
                break;
            default:
                fire = probe.Holds;
                break;
        }
        if (fire && Recurring && CooldownSeconds > 0 && LastFired is { } last && now - last < TimeSpan.FromSeconds(CooldownSeconds))
            return false;
        return fire;
    }

    /// <summary>The announcement went out.</summary>
    public void Fired(TriggerDef def, TriggerProbe probe, DateTime now)
    {
        LastFired = now;
        FireCount++;
        if (def.Kind == TriggerKind.Condition) Armed = false;
        if (def.Kind == TriggerKind.Change) Fingerprint = probe.Fingerprint;
    }

    /// <summary>Whether the watch goes on after announcing: a clock always does.</summary>
    public bool KeepsGoing(TriggerDef def) => Recurring || def.Kind == TriggerKind.Pulse;
}

/// <summary>Where watches live on disk, next to the scheduled-send launchers.</summary>
public static class WatchStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathOf(string id) => Path.Combine(ScheduledTaskRegistrar.ScheduledDirectory(), $"{id}.watch.json");

    public static string LauncherOf(string id) => Path.Combine(ScheduledTaskRegistrar.ScheduledDirectory(), $"{id}.cmd");

    public static Watch? Load(string id)
    {
        var path = PathOf(id);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Watch>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Written to a temp file and swapped in, so a crash mid-write cannot leave half a watch.</summary>
    public static void Save(Watch watch)
    {
        Directory.CreateDirectory(ScheduledTaskRegistrar.ScheduledDirectory());
        var path = PathOf(watch.Id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(watch, Json), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(string id)
    {
        foreach (var path in new[] { PathOf(id), LauncherOf(id), PathOf(id) + ".lock" })
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The launcher the Scheduler job runs: one line that has psst look at the watch.</summary>
    public static string WriteLauncher(string id, string psstExePath)
    {
        Directory.CreateDirectory(ScheduledTaskRegistrar.ScheduledDirectory());
        var path = LauncherOf(id);
        var text = "@echo off\r\n" + ScheduledTaskRegistrar.BuildInvocationLine(psstExePath, new[] { "watch-tick", id }) + "\r\n";
        File.WriteAllText(path, text, ScheduledTaskRegistrar.LauncherEncoding);
        return path;
    }
}
