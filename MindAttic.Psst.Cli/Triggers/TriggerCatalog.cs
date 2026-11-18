namespace MindAttic.Psst.Cli.Triggers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MindAttic.Psst.Time;

/// <summary>How a trigger decides it is time to announce.</summary>
public enum TriggerKind
{
    /// <summary>True or false right now. Announces when it turns true (and again, for a recurring watch, each time it turns true after being false).</summary>
    Condition,

    /// <summary>A value that is compared with the one seen last time. Announces when it differs.</summary>
    Change,

    /// <summary>A clock. Announces every time its moment comes round, so it is always recurring.</summary>
    Pulse,
}

/// <summary>What a trigger sees of its watch: when it was made and when it last announced.</summary>
public sealed record TriggerContext(DateTime Now, DateTime Created, DateTime? LastFired);

/// <summary>
/// One look at a trigger. <see cref="Ok"/> false means it could not be read (a tool missing, a
/// timeout), which never counts as either answer. <see cref="Holds"/> is the answer for a
/// <see cref="TriggerKind.Condition"/> or <see cref="TriggerKind.Pulse"/>;
/// <see cref="Fingerprint"/> the value for a <see cref="TriggerKind.Change"/>.
/// </summary>
public sealed record TriggerProbe(bool Ok, bool Holds, string? Fingerprint, string Detail)
{
    public static TriggerProbe Is(bool holds, string detail = "") => new(true, holds, null, detail);
    public static TriggerProbe Value(string fingerprint, string detail = "") => new(true, false, fingerprint, detail);
    public static TriggerProbe Failed(string why) => new(false, false, null, why);
}

/// <summary>One named trigger: what it takes, what it means and how it is read.</summary>
public sealed record TriggerDef(
    string Name,
    string[] Aliases,
    string ArgsHelp,
    int MinArgs,
    int MaxArgs,
    TriggerKind Kind,
    string Summary,
    Func<string[], TriggerContext, TriggerProbe> Probe,
    Func<string[], string>? DefaultMessage = null);

/// <summary>
/// Every event <c>psst when</c> can wait for (M.PSST-T41). A trigger is a name with optional
/// arguments, written <c>Name</c> or <c>Name(arg, arg)</c>; names match whatever the case and
/// whatever punctuation separates the words (<c>NoActiveClisDetected</c>, <c>no-active-clis-detected</c>).
/// Anything not named here is covered by the three <c>Command…</c> triggers, which wait on any
/// shell command.
/// </summary>
public static class TriggerCatalog
{
    /// <summary>Longest an outside tool may take before a look counts as failed (ms).</summary>
    internal const int ToolTimeoutMs = 45_000;

    public static IReadOnlyList<TriggerDef> All { get; } = Build();

    /// <summary>Letters and digits only, lower-cased: how names are compared.</summary>
    internal static string Key(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static TriggerDef? Find(string name)
    {
        var key = Key(name);
        return All.FirstOrDefault(t => Key(t.Name) == key || t.Aliases.Any(a => Key(a) == key));
    }

    /// <summary>
    /// Split <c>Name(arg, "arg, with comma")</c> into its name and arguments. Bare <c>Name</c> has none.
    /// </summary>
    public static (string Name, string[] Args, string? Error) Parse(string spec)
    {
        spec = (spec ?? "").Trim();
        var open = spec.IndexOf('(');
        if (open < 0) return (spec, Array.Empty<string>(), null);
        if (!spec.EndsWith(')')) return (spec, Array.Empty<string>(), $"'{spec}' opens a bracket it never closes");
        var name = spec[..open].Trim();
        var inner = spec[(open + 1)..^1];
        var args = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        foreach (var c in inner)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; else current.Append(c); }
            else if (c is '"' or '\'') quote = c;
            else if (c == ',') { args.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        if (quote != '\0') return (name, Array.Empty<string>(), $"'{spec}' has an unclosed quote");
        if (current.ToString().Trim().Length > 0 || args.Count > 0) args.Add(current.ToString().Trim());
        return (name, args.ToArray(), null);
    }

    /// <summary>How a trigger reads back: <c>Name</c> or <c>Name(a, b)</c>.</summary>
    public static string Describe(string name, IReadOnlyList<string> args) =>
        args.Count == 0 ? name : $"{name}({string.Join(", ", args)})";

    private static List<TriggerDef> Build()
    {
        var list = new List<TriggerDef>();
        void Add(string name, string[] aliases, string argsHelp, int min, int max, TriggerKind kind, string summary,
            Func<string[], TriggerContext, TriggerProbe> probe, Func<string[], string>? message = null) =>
            list.Add(new TriggerDef(name, aliases, argsHelp, min, max, kind, summary, probe, message));

        // ── The agents ───────────────────────────────────────────────────────
        Add("NoActiveClisDetected", ["NoActiveClis", "AllClisDone", "AllClisIdle", "ClisDone"], "[includeGoal]", 0, 1, TriggerKind.Condition,
            "No open Claude CLI is working (a turn, a subagent or a background job). CLIs on a /goal are left out unless 'includeGoal' is given.",
            (a, _) => ClisIdle(null, a.Length > 0 && Key(a[0]) == "includegoal"),
            _ => "Every open CLI is done.");
        Add("CliIdle", ["RepoClisIdle", "RepoIdle"], "<repo>", 1, 1, TriggerKind.Condition,
            "No Claude CLI opened on that repo is working.",
            (a, _) => ClisIdle(a[0], true),
            a => $"The {a[0]} CLI is done.");
        Add("TicketClosed", [], "<ticket id>", 1, 1, TriggerKind.Condition,
            "That ticket's status is closed.",
            (a, _) => TicketClosed(a[0]),
            a => $"Ticket {a[0]} is closed.");

        // ── Installs and processes ───────────────────────────────────────────
        Add("OrchestratorRedeployed", ["OrchestratorDeployed", "OrchestratorInstalled"], "", 0, 0, TriggerKind.Change,
            "A new build of MindAttic.Orchestrator was installed.",
            (_, _) => AppFingerprint("MindAttic.Orchestrator"),
            _ => "MindAttic.Orchestrator was redeployed.");
        Add("AppRedeployed", ["AppDeployed", "AppInstalled"], "<app folder name or path>", 1, 1, TriggerKind.Change,
            "A new build of that installed app landed (its folder under C:\\Apps, or any folder or file path).",
            (a, _) => AppFingerprint(a[0]),
            a => $"{a[0]} was redeployed.");
        Add("ProcessStarted", ["ProcessRunning"], "<name or pid>", 1, 1, TriggerKind.Condition,
            "A process with that name (or id) is running.",
            (a, _) => TriggerProbe.Is(ProcessIds(a[0]).Count > 0),
            a => $"{a[0]} is running.");
        Add("ProcessExited", ["ProcessEnded", "ProcessStopped", "ProcessGone"], "<name or pid>", 1, 1, TriggerKind.Condition,
            "No process with that name (or id) is running.",
            (a, _) => TriggerProbe.Is(ProcessIds(a[0]).Count == 0),
            a => $"{a[0]} has exited.");
        Add("ProcessRestarted", [], "<name>", 1, 1, TriggerKind.Change,
            "The set of processes with that name changed (one started, exited or was replaced).",
            (a, _) => TriggerProbe.Value(string.Join(",", ProcessIds(a[0]).OrderBy(i => i))),
            a => $"{a[0]} restarted.");

        // ── Files, disks, repos ──────────────────────────────────────────────
        Add("FileExists", ["FileAppeared", "FileCreated", "PathExists"], "<path>", 1, 1, TriggerKind.Condition,
            "That file or folder exists.",
            (a, _) => TriggerProbe.Is(File.Exists(a[0]) || Directory.Exists(a[0])),
            a => $"{a[0]} exists.");
        Add("FileMissing", ["FileDeleted", "FileGone", "PathMissing"], "<path>", 1, 1, TriggerKind.Condition,
            "That file or folder does not exist.",
            (a, _) => TriggerProbe.Is(!File.Exists(a[0]) && !Directory.Exists(a[0])),
            a => $"{a[0]} is gone.");
        Add("FileChanged", ["FileModified", "FolderChanged", "PathChanged"], "<path>", 1, 1, TriggerKind.Change,
            "That file was written (or, for a folder, something directly in it was added, removed or written).",
            (a, _) => PathFingerprint(a[0]),
            a => $"{a[0]} changed.");
        Add("DiskFreeBelow", ["DiskLow", "LowDisk"], "<drive>, <GB>", 2, 2, TriggerKind.Condition,
            "That drive has less free space than the given number of gigabytes.",
            (a, _) => DiskFree(a[0], a[1], below: true),
            a => $"Drive {a[0]} is under {a[1]} GB free.");
        Add("DiskFreeAbove", [], "<drive>, <GB>", 2, 2, TriggerKind.Condition,
            "That drive has more free space than the given number of gigabytes.",
            (a, _) => DiskFree(a[0], a[1], below: false),
            a => $"Drive {a[0]} is over {a[1]} GB free.");
        Add("RepoCommitted", ["NewCommit", "RepoChanged"], "<repo folder>", 1, 1, TriggerKind.Change,
            "That checkout's HEAD moved (a commit, a pull, a checkout).",
            (a, _) => RepoHead(a[0]),
            a => $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(a[0]))} has a new commit.");

        // ── The network ──────────────────────────────────────────────────────
        Add("HttpUp", ["UrlUp", "SiteUp"], "<url>", 1, 1, TriggerKind.Condition,
            "That address answers with a success status.",
            (a, _) => Http(a[0], wantUp: true),
            a => $"{a[0]} is up.");
        Add("HttpDown", ["UrlDown", "SiteDown"], "<url>", 1, 1, TriggerKind.Condition,
            "That address fails to answer, or answers with an error status.",
            (a, _) => Http(a[0], wantUp: false),
            a => $"{a[0]} is down.");
        Add("HttpChanged", ["UrlChanged", "PageChanged"], "<url>", 1, 1, TriggerKind.Change,
            "The content at that address changed.",
            (a, _) => HttpBody(a[0]),
            a => $"{a[0]} changed.");
        Add("PortOpen", ["PortUp"], "<host>, <port>", 2, 2, TriggerKind.Condition,
            "That TCP port accepts a connection.",
            (a, _) => Port(a[0], a[1], wantOpen: true),
            a => $"{a[0]}:{a[1]} is open.");
        Add("PortClosed", ["PortDown"], "<host>, <port>", 2, 2, TriggerKind.Condition,
            "That TCP port refuses a connection.",
            (a, _) => Port(a[0], a[1], wantOpen: false),
            a => $"{a[0]}:{a[1]} is closed.");
        Add("InternetUp", ["Online"], "", 0, 0, TriggerKind.Condition,
            "This PC can reach the internet.",
            (_, _) => Http("https://www.msftconnecttest.com/connecttest.txt", wantUp: true),
            _ => "The internet is back.");
        Add("InternetDown", ["Offline"], "", 0, 0, TriggerKind.Condition,
            "This PC cannot reach the internet.",
            (_, _) => Http("https://www.msftconnecttest.com/connecttest.txt", wantUp: false),
            _ => "The internet is down.");

        // ── Anything else: a shell command ───────────────────────────────────
        Add("CommandSucceeds", ["CommandOk", "Command"], "<command line>", 1, 1, TriggerKind.Condition,
            "That command line (run by cmd.exe from the workspace root) exits 0.",
            (a, _) => Command(a[0]) is var r && r.Ran ? TriggerProbe.Is(r.Exit == 0, $"exit {r.Exit}") : TriggerProbe.Failed(r.Error),
            a => $"Succeeded: {a[0]}");
        Add("CommandFails", ["CommandFailed"], "<command line>", 1, 1, TriggerKind.Condition,
            "That command line exits with anything but 0.",
            (a, _) => Command(a[0]) is var r && r.Ran ? TriggerProbe.Is(r.Exit != 0, $"exit {r.Exit}") : TriggerProbe.Failed(r.Error),
            a => $"Failed: {a[0]}");
        Add("CommandOutputChanged", ["OutputChanged"], "<command line>", 1, 1, TriggerKind.Change,
            "That command line prints something different from last time.",
            (a, _) => Command(a[0]) is var r && r.Ran ? TriggerProbe.Value(Hash(r.Output), FirstLine(r.Output)) : TriggerProbe.Failed(r.Error),
            a => $"Output changed: {a[0]}");

        // ── The clock (always recurring; a single moment is `psst remind --at`) ─
        Add("Every", ["Interval"], "<30m|2h|1d>", 1, 1, TriggerKind.Pulse,
            "That much time has passed since the watch was made or last announced.",
            (a, c) => DurationParser.TryParse(a[0], out var d) && d >= TimeSpan.FromMinutes(1)
                ? TriggerProbe.Is(c.Now - (c.LastFired ?? c.Created) >= d)
                : TriggerProbe.Failed($"'{a[0]}' is not a duration of a minute or more (30m, 2h, 1d)"));
        Add("Daily", ["EveryDay"], "<time of day>", 1, 1, TriggerKind.Pulse,
            "That time of day has come (once a day).",
            (a, c) => TimeOfDay(a[0], out var t, out var err) ? TriggerProbe.Is(Due(c, c.Now.Date + t)) : TriggerProbe.Failed(err));
        Add("Weekdays", ["EveryWeekday"], "<time of day>", 1, 1, TriggerKind.Pulse,
            "That time of day has come, Monday to Friday.",
            (a, c) => TimeOfDay(a[0], out var t, out var err)
                ? TriggerProbe.Is(c.Now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && Due(c, c.Now.Date + t))
                : TriggerProbe.Failed(err));
        Add("Weekly", ["EveryWeek"], "<day>, <time of day>", 2, 2, TriggerKind.Pulse,
            "That day of the week and time have come (once a week).",
            (a, c) =>
            {
                if (!DayOf(a[0], out var day)) return TriggerProbe.Failed($"'{a[0]}' is not a day of the week");
                return TimeOfDay(a[1], out var t, out var err) ? TriggerProbe.Is(c.Now.DayOfWeek == day && Due(c, c.Now.Date + t)) : TriggerProbe.Failed(err);
            });
        return list;
    }

    // ── Clock helpers ────────────────────────────────────────────────────────

    /// <summary>A moment today is due once it has passed, the watch existed before it, and it has not been announced since.</summary>
    private static bool Due(TriggerContext c, DateTime moment) =>
        c.Now >= moment && c.Created < moment && (c.LastFired is not { } fired || fired < moment);

    private static bool TimeOfDay(string text, out TimeSpan time, out string error)
    {
        error = "";
        // Parsed against midnight, so the "next occurrence" is always that time today.
        if (TimeOfDayParser.TryParse(text, DateTime.Today.AddSeconds(-1), out var when))
        {
            time = when.TimeOfDay;
            return true;
        }
        time = default;
        error = $"'{text}' is not a time of day (9am, 2:30pm, 14:30)";
        return false;
    }

    private static bool DayOf(string text, out DayOfWeek day)
    {
        foreach (var d in Enum.GetValues<DayOfWeek>())
            if (text.Trim().Length >= 3 && d.ToString().StartsWith(text.Trim(), StringComparison.OrdinalIgnoreCase)) { day = d; return true; }
        day = default;
        return false;
    }

    // ── Agents ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks <c>orchestrator busy</c> (M.ORCH-T1827) whether any CLI is still running work: hosted
    /// CLIs count, a claim or a prompt waiting on the owner does not. <paramref name="repo"/>
    /// narrows it to the CLIs opened on that repo.
    /// </summary>
    private static TriggerProbe ClisIdle(string? repo, bool includeGoal)
    {
        var exe = Tools.Locate("orchestrator", "MINDATTIC_ORCHESTRATOR", @"C:\Apps\MindAttic.Orchestrator\Cli\orchestrator.exe");
        if (exe is null) return TriggerProbe.Failed("orchestrator.exe was not found (MindAttic.Orchestrator's CLI)");
        var args = new List<string> { "busy", "--all", "--running-only", "--self", "0", "--json" };
        if (!includeGoal) args.Add("--skip-goal");
        var run = Tools.Run(exe, args, Tools.WorkspaceRoot());
        if (!run.Ran) return TriggerProbe.Failed(run.Error);
        if (run.Exit is not (0 or 1)) return TriggerProbe.Failed($"orchestrator busy exited {run.Exit}: {FirstLine(run.Output + run.Errors)} (an Orchestrator older than M.ORCH-T1827?)");
        try
        {
            using var doc = JsonDocument.Parse(run.Output);
            var working = new List<string>();
            foreach (var cli in doc.RootElement.GetProperty("Clis").EnumerateArray())
            {
                var activity = cli.GetProperty("Activity").GetString();
                if (activity is not ("Working" or "Unknown")) continue;
                var name = cli.GetProperty("Repo").GetString() ?? "?";
                if (repo is not null && !name.Equals(repo, StringComparison.OrdinalIgnoreCase)) continue;
                working.Add(name);
            }
            return TriggerProbe.Is(working.Count == 0, working.Count == 0 ? "" : "still working: " + string.Join(", ", working.Distinct()));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return TriggerProbe.Failed($"orchestrator busy's answer could not be read: {ex.Message}");
        }
    }

    private static TriggerProbe TicketClosed(string id)
    {
        var exe = Tools.Locate("tickets", "MINDATTIC_TICKETS", null);
        if (exe is null) return TriggerProbe.Failed("tickets.exe was not found (MindAttic.Tickets' CLI)");
        var run = Tools.Run(exe, ["ticket", "show", id, "--json"], Tools.WorkspaceRoot());
        if (!run.Ran) return TriggerProbe.Failed(run.Error);
        if (run.Exit != 0) return TriggerProbe.Failed($"tickets ticket show {id} exited {run.Exit}: {FirstLine(run.Output + run.Errors)}");
        try
        {
            using var doc = JsonDocument.Parse(run.Output);
            var status = doc.RootElement.GetProperty("status").GetString() ?? "";
            return TriggerProbe.Is(status.Equals("closed", StringComparison.OrdinalIgnoreCase), $"status {status}");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return TriggerProbe.Failed($"the ticket's JSON could not be read: {ex.Message}");
        }
    }

    // ── Installs, files, disks, repos ────────────────────────────────────────

    /// <summary>Where apps are installed: <c>MINDATTIC_APPS</c>, else <c>C:\Apps</c>.</summary>
    internal static string AppsRoot() =>
        Environment.GetEnvironmentVariable("MINDATTIC_APPS") is { Length: > 0 } root ? root : @"C:\Apps";

    private static TriggerProbe AppFingerprint(string app)
    {
        var path = File.Exists(app) || Directory.Exists(app) ? app : Path.Combine(AppsRoot(), app);
        if (!File.Exists(path) && !Directory.Exists(path) && Directory.Exists(Path.Combine(AppsRoot(), "MindAttic." + app)))
            path = Path.Combine(AppsRoot(), "MindAttic." + app);
        if (File.Exists(path)) return PathFingerprint(path);
        if (!Directory.Exists(path)) return TriggerProbe.Failed($"no installed app '{app}' under {AppsRoot()}, and no such path");
        try
        {
            // The programs themselves, not logs or settings the app writes while it runs.
            // Some apps keep their binaries a folder or two down (C:\Apps\Prose\Hub\...), so look below too.
            var newest = new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.LastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
            if (newest == DateTime.MinValue) return TriggerProbe.Failed($"no program files under {path}");
            return TriggerProbe.Value(newest.Ticks.ToString(CultureInfo.InvariantCulture), $"built {newest.ToLocalTime():yyyy-MM-dd HH:mm}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return TriggerProbe.Failed(ex.Message); }
    }

    private static TriggerProbe PathFingerprint(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var f = new FileInfo(path);
                return TriggerProbe.Value($"{f.LastWriteTimeUtc.Ticks}:{f.Length}");
            }
            if (Directory.Exists(path))
            {
                var sb = new StringBuilder();
                foreach (var e in new DirectoryInfo(path).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    sb.Append(e.Name).Append('|').Append(e.LastWriteTimeUtc.Ticks).Append(';');
                return TriggerProbe.Value(Hash(sb.ToString()));
            }
            return TriggerProbe.Value("missing");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return TriggerProbe.Failed(ex.Message); }
    }

    private static TriggerProbe DiskFree(string drive, string gbText, bool below)
    {
        if (!double.TryParse(gbText, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) || gb < 0)
            return TriggerProbe.Failed($"'{gbText}' is not a number of gigabytes");
        try
        {
            var info = new DriveInfo(drive.Trim().TrimEnd(':', '\\', '/')[..1]);
            var free = info.AvailableFreeSpace / 1024d / 1024d / 1024d;
            return TriggerProbe.Is(below ? free < gb : free > gb, string.Create(CultureInfo.InvariantCulture, $"{free:0.0} GB free"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return TriggerProbe.Failed(ex.Message); }
    }

    private static TriggerProbe RepoHead(string folder)
    {
        // A bare repo name means the one in the workspace, wherever psst happens to be run from.
        if (!Path.IsPathRooted(folder) && Directory.Exists(Path.Combine(Tools.WorkspaceRoot(), folder)))
            folder = Path.Combine(Tools.WorkspaceRoot(), folder);
        if (!Directory.Exists(folder)) return TriggerProbe.Failed($"no folder '{folder}'");
        var git = Tools.Locate("git", null, null);
        if (git is null) return TriggerProbe.Failed("git was not found");
        var run = Tools.Run(git, ["-C", folder, "rev-parse", "HEAD"], folder);
        if (!run.Ran) return TriggerProbe.Failed(run.Error);
        return run.Exit == 0 ? TriggerProbe.Value(run.Output.Trim(), run.Output.Trim()[..Math.Min(8, run.Output.Trim().Length)]) : TriggerProbe.Failed(FirstLine(run.Errors));
    }

    private static List<int> ProcessIds(string nameOrPid)
    {
        var ids = new List<int>();
        if (int.TryParse(nameOrPid, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            try { using var p = Process.GetProcessById(pid); if (!p.HasExited) ids.Add(pid); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
            return ids;
        }
        var name = nameOrPid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? nameOrPid[..^4] : nameOrPid;
        foreach (var p in Process.GetProcessesByName(name))
            using (p) ids.Add(p.Id);
        return ids;
    }

    // ── Network ──────────────────────────────────────────────────────────────

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static TriggerProbe Http(string url, bool wantUp)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return TriggerProbe.Failed($"'{url}' is not an http(s) address");
        try
        {
            using var response = Client.Send(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead);
            var up = (int)response.StatusCode < 400;
            return TriggerProbe.Is(up == wantUp, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return TriggerProbe.Is(!wantUp, "no answer");
        }
    }

    private static TriggerProbe HttpBody(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return TriggerProbe.Failed($"'{url}' is not an http(s) address");
        try
        {
            using var response = Client.Send(new HttpRequestMessage(HttpMethod.Get, uri));
            using var reader = new StreamReader(response.Content.ReadAsStream());
            return TriggerProbe.Value((int)response.StatusCode + ":" + Hash(reader.ReadToEnd()), $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            // No answer is not a change of content: wait for the next look.
            return TriggerProbe.Failed($"no answer from {url}: {ex.Message}");
        }
    }

    private static TriggerProbe Port(string host, string portText, bool wantOpen)
    {
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            return TriggerProbe.Failed($"'{portText}' is not a port number");
        try
        {
            using var client = new TcpClient();
            var open = client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(5)) && client.Connected;
            return TriggerProbe.Is(open == wantOpen);
        }
        catch (Exception ex) when (ex is SocketException or AggregateException or IOException)
        {
            return TriggerProbe.Is(!wantOpen);
        }
    }

    // ── Shell ────────────────────────────────────────────────────────────────

    private static Tools.RunResult Command(string commandLine)
    {
        var cmd = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } c ? c : "cmd.exe";
        return Tools.RunRaw(cmd, "/d /s /c \"" + commandLine + "\"", Tools.WorkspaceRoot());
    }

    internal static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    internal static string FirstLine(string text)
    {
        var line = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return line.Length <= 120 ? line : line[..119] + "…";
    }
}

/// <summary>Finding and running the outside tools triggers ask.</summary>
internal static class Tools
{
    public sealed record RunResult(bool Ran, int Exit, string Output, string Errors, string Error);

    /// <summary>
    /// The workspace root: the first folder above psst that holds <c>MindAttic.Scheduler</c>
    /// (the same anchor <c>SchedulerClient</c> uses), else the user's profile.
    /// </summary>
    public static string WorkspaceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "MindAttic.Scheduler"))) return dir.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>An exe by its override variable, then PATH (× PATHEXT), then a known install path.</summary>
    public static string? Locate(string name, string? variable, string? fallback)
    {
        if (variable is not null && Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured && File.Exists(configured))
            return configured;
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var ext in exts)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name + ext); }
                catch (ArgumentException) { continue; }
                if (File.Exists(candidate)) return candidate;
            }
        return fallback is not null && File.Exists(fallback) ? fallback : null;
    }

    public static RunResult Run(string exe, IReadOnlyList<string> args, string workdir)
    {
        var psi = Start(exe, workdir);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Run(psi);
    }

    /// <summary>Runs with one ready-made argument string (cmd.exe's own quoting rules).</summary>
    public static RunResult RunRaw(string exe, string arguments, string workdir)
    {
        var psi = Start(exe, workdir);
        psi.Arguments = arguments;
        return Run(psi);
    }

    private static ProcessStartInfo Start(string exe, string workdir) => new(exe)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        WorkingDirectory = Directory.Exists(workdir) ? workdir : Environment.CurrentDirectory,
    };

    private static RunResult Run(ProcessStartInfo psi)
    {
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return new RunResult(false, -1, "", "", $"could not start {psi.FileName}");
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(TriggerCatalog.ToolTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return new RunResult(false, -1, "", "", $"{Path.GetFileName(psi.FileName)} took longer than {TriggerCatalog.ToolTimeoutMs / 1000}s");
            }
            proc.WaitForExit();
            return new RunResult(true, proc.ExitCode, stdout.Result, stderr.Result, "");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            return new RunResult(false, -1, "", "", $"could not run {Path.GetFileName(psi.FileName)}: {ex.Message}");
        }
    }
}
