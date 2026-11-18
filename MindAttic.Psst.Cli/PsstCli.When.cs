namespace MindAttic.Psst.Cli;

using System.Globalization;
using System.Text.Json;
using MindAttic.Psst.Cli.Scheduling;
using MindAttic.Psst.Cli.Triggers;
using MindAttic.Psst.Configuration;
using MindAttic.Psst.Time;

/// <summary>
/// <c>psst when</c>, <c>psst triggers</c> and the tick a Scheduler job runs (M.PSST-T41):
/// announcements tied to an event rather than a time.
/// </summary>
public sealed partial class PsstCli
{
    private const string WhenUsage =
        "usage: psst when <Trigger[(args)]> [--arg <value>]... [--recurring] [--poll <1m|5m>] [--cooldown <10m>]\n" +
        "                 [--expires <when>] [--name <id>] [message...]\n" +
        "       psst triggers                  lists every trigger\n" +
        "       psst triggers check <Trigger[(args)]>   looks once, announces nothing";

    /// <summary>Shortest and longest gap between looks: Scheduler repeats no faster than a minute.</summary>
    private static readonly TimeSpan MinPoll = TimeSpan.FromMinutes(1), MaxPoll = TimeSpan.FromHours(12);

    /// <summary>
    /// <c>psst when &lt;Trigger&gt; [message]</c>: registers a Scheduler job that looks at the
    /// trigger every <c>--poll</c> and announces (sound, text and email) when it happens. Once by
    /// default; <c>--recurring</c> keeps it watching.
    /// </summary>
    private static async Task<int> WhenAsync(string[] args, bool silent)
    {
        string? spec = null, name = null, pollText = null, cooldownText = null, expiresText = null;
        var extra = new List<string>();
        var words = new List<string>();
        bool recurring = false, force = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--arg" or "--poll" or "--cooldown" or "--expires" or "--name")
            {
                if (i + 1 >= args.Length) return WhenFail($"{a} needs a value");
                var v = args[++i];
                switch (a)
                {
                    case "--arg": extra.Add(v); break;
                    case "--poll": pollText = v; break;
                    case "--cooldown": cooldownText = v; break;
                    case "--expires": expiresText = v; break;
                    default: name = v; break;
                }
            }
            else if (a is "--recurring" or "--repeat" or "--always") recurring = true;
            else if (a == "--once") recurring = false;
            else if (a == "--force") force = true;
            else if (spec is null) spec = a;
            else words.Add(a);
        }
        if (spec is null) return WhenFail("a trigger is required");

        var (triggerName, specArgs, parseError) = TriggerCatalog.Parse(spec);
        if (parseError is not null) return WhenFail(parseError);
        var def = TriggerCatalog.Find(triggerName);
        if (def is null) return WhenFail($"no trigger named '{triggerName}' (run `psst triggers` for the list; CommandSucceeds covers anything else)");
        var triggerArgs = specArgs.Concat(extra).ToArray();
        if (triggerArgs.Length < def.MinArgs || triggerArgs.Length > def.MaxArgs)
            return WhenFail($"{def.Name} takes {(def.MaxArgs == 0 ? "no arguments" : def.ArgsHelp)} (got {triggerArgs.Length})");

        var poll = MinPoll;
        if (pollText is not null && (!DurationParser.TryParse(pollText, out poll) || poll < MinPoll || poll > MaxPoll))
            return WhenFail($"--poll '{pollText}' must be between 1m and 12h");
        var cooldown = TimeSpan.Zero;
        if (cooldownText is not null && (!DurationParser.TryParse(cooldownText, out cooldown) || cooldown < TimeSpan.Zero))
            return WhenFail($"--cooldown '{cooldownText}' is not a duration (10m, 2h)");
        DateTime? expires = null;
        if (expiresText is not null)
        {
            if (!ScheduleTimeParser.TryParse(expiresText, DateTime.Now, out var until, out var timeError)) return WhenFail($"--expires {timeError}");
            expires = until;
        }
        if (name is not null && !ScheduledTaskRegistrar.IsValidName(name))
            return WhenFail($"invalid --name '{name}': use letters, digits, '.', '_' or '-' (up to 64, starting with a letter or digit)");

        var now = DateTime.Now;
        var watch = new Watch
        {
            Id = name ?? Guid.NewGuid().ToString("N")[..12],
            Trigger = def.Name,
            Args = triggerArgs,
            // One line: it is read back from JSON, but a text message is one line anyway.
            Message = string.Join(' ', string.Join(' ', words).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim(),
            Recurring = recurring || def.Kind == TriggerKind.Pulse,
            PollSeconds = (int)poll.TotalSeconds,
            CooldownSeconds = (int)Math.Min(cooldown.TotalSeconds, int.MaxValue),
            Silent = silent,
            Created = now,
            Expires = expires,
        };

        // Look once now: a trigger that cannot be read (a missing tool, a bad argument) is refused
        // here rather than failing quietly every minute, and a change trigger gets its starting value.
        var probe = def.Probe(triggerArgs, new TriggerContext(now, now, null));
        if (!probe.Ok && !force)
        {
            Console.Error.WriteLine($"error: {watch.Describe()} cannot be read right now — {probe.Detail}");
            Console.Error.WriteLine("       (--force registers it anyway and keeps trying)");
            return 1;
        }
        if (probe.Ok && def.Kind == TriggerKind.Change) watch.Fingerprint = probe.Fingerprint;

        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("could not resolve current psst.exe path");
        WatchStore.Save(watch);
        var launcher = WatchStore.WriteLauncher(watch.Id, exePath);
        var added = await SchedulerClient.AddEveryAsync(watch.Id, poll, launcher, $"Psst watch: {watch.Describe()}");
        if (!added.Success)
        {
            WatchStore.Delete(watch.Id);
            Console.Error.WriteLine($"error: could not schedule the watch — {added.Error}");
            return 1;
        }

        Console.WriteLine($"→ when {watch.Describe()}: {watch.Announcement(def, TriggerProbe.Is(true))}");
        Console.WriteLine($"  ⏱ looked at every {DurationParser.Format(poll)}; {(watch.Recurring ? "announces every time it happens" : "announces once, then removes itself")} (id: {watch.Id})");
        if (def.Kind == TriggerKind.Condition && probe is { Ok: true, Holds: true })
            Console.WriteLine("  · it already holds, so the first look announces it");
        if (expires is { } e) Console.WriteLine($"  · gives up at {e:yyyy-MM-dd HH:mm}");
        Console.WriteLine($"  · cancel with: psst scheduled cancel {watch.Id}");
        return 0;

        static int WhenFail(string err)
        {
            Console.Error.WriteLine($"error: {err}");
            Console.Error.WriteLine(WhenUsage);
            return 1;
        }
    }

    /// <summary><c>psst triggers [--json]</c> lists the catalog; <c>psst triggers check &lt;spec&gt;</c> looks once.</summary>
    private static int Triggers(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("check", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2) { Console.Error.WriteLine(WhenUsage); return 1; }
            var (triggerName, triggerArgs, parseError) = TriggerCatalog.Parse(args[1]);
            triggerArgs = triggerArgs.Concat(args.Skip(2)).ToArray();
            var def = parseError is null ? TriggerCatalog.Find(triggerName) : null;
            if (def is null) { Console.Error.WriteLine($"error: {parseError ?? $"no trigger named '{triggerName}'"}"); return 1; }
            if (triggerArgs.Length < def.MinArgs || triggerArgs.Length > def.MaxArgs)
            {
                Console.Error.WriteLine($"error: {def.Name} takes {(def.MaxArgs == 0 ? "no arguments" : def.ArgsHelp)}");
                return 1;
            }
            var now = DateTime.Now;
            var probe = def.Probe(triggerArgs, new TriggerContext(now, now, null));
            var answer = !probe.Ok ? "cannot be read" : def.Kind == TriggerKind.Change ? $"value {probe.Fingerprint}" : probe.Holds ? "holds" : "does not hold";
            Console.WriteLine($"{TriggerCatalog.Describe(def.Name, triggerArgs)}: {answer}{(probe.Detail.Length > 0 ? $" — {probe.Detail}" : "")}");
            return probe.Ok ? 0 : 1;
        }

        if (args.Contains("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(TriggerCatalog.All.Select(t => new
            {
                name = t.Name, aliases = t.Aliases, args = t.ArgsHelp, kind = t.Kind.ToString(), summary = t.Summary,
            }), new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine("Triggers for `psst when <Trigger[(args)]> [message]`:");
        Console.WriteLine();
        foreach (var t in TriggerCatalog.All)
        {
            var kind = t.Kind switch { TriggerKind.Change => "on change", TriggerKind.Pulse => "clock", _ => "when true" };
            Console.WriteLine($"  {(t.ArgsHelp.Length > 0 ? $"{t.Name}({t.ArgsHelp})" : t.Name)}   [{kind}]");
            Console.WriteLine($"      {t.Summary}");
        }
        Console.WriteLine();
        Console.WriteLine("Names match whatever the case or punctuation. Once by default; --recurring keeps watching.");
        Console.WriteLine("A single moment in time is `psst remind --at <when> <message>`.");
        return 0;
    }

    /// <summary>
    /// <c>psst watch-tick &lt;id&gt;</c>: what a watch's Scheduler job runs. Looks at the trigger,
    /// announces if it is time, and removes a one-time watch once its announcement has gone out.
    /// </summary>
    private static async Task<int> WatchTickAsync(string[] args, PsstConfiguration config)
    {
        if (args.Length != 1 || !ScheduledTaskRegistrar.IsValidName(args[0]))
        {
            Console.Error.WriteLine("usage: psst watch-tick <id>   (run by a watch's Scheduler job)");
            return 1;
        }
        var id = args[0];

        // One look at a time: a slow look must not be overtaken by the next one.
        FileStream? guard;
        try
        {
            Directory.CreateDirectory(ScheduledTaskRegistrar.ScheduledDirectory());
            guard = new FileStream(WatchStore.PathOf(id) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return 0; }

        using (guard)
        {
            var watch = WatchStore.Load(id);
            if (watch is null)
            {
                // The watch file is gone (or unreadable): nothing to look at, so the job goes too.
                await SchedulerClient.CancelAsync(id);
                Console.Error.WriteLine($"watch '{id}' has no readable file; its job was cancelled");
                guard.Dispose();
                WatchStore.Delete(id);
                return 1;
            }

            var now = DateTime.Now;
            if (watch.Expires is { } expires && now >= expires)
            {
                await SchedulerClient.CancelAsync(id);
                guard.Dispose();
                WatchStore.Delete(id);
                Console.WriteLine($"watch '{id}' expired unannounced");
                return 0;
            }

            var def = TriggerCatalog.Find(watch.Trigger);
            var probe = def is null
                ? TriggerProbe.Failed($"no trigger named '{watch.Trigger}' in this psst")
                : def.Probe(watch.Args, new TriggerContext(now, watch.Created, watch.LastFired));
            watch.LastChecked = now;
            watch.LastDetail = probe.Ok ? probe.Detail : watch.LastDetail;
            watch.LastError = probe.Ok ? null : probe.Detail;

            if (def is not null && watch.ShouldFire(def, probe, now))
            {
                var result = await Notify(config, watch.Announcement(def, probe), watch.Silent);
                // Unsent (SMTP down, say): left as it was, so the next look tries again.
                if (result.AnySmsSent)
                {
                    watch.Fired(def, probe, now);
                    if (!watch.KeepsGoing(def))
                    {
                        await SchedulerClient.CancelAsync(id);
                        guard.Dispose();
                        WatchStore.Delete(id);
                        return 0;
                    }
                }
                else watch.LastError = "not sent, trying again at the next look: " +
                    (result.SmsAttempts.Count > 0 ? string.Join("; ", result.SmsAttempts.Select(a => a.Detail.ReplaceLineEndings(" "))) : "no transport is configured (psst ping)");
            }
            else if (!probe.Ok) Console.Error.WriteLine($"{watch.Describe()}: {probe.Detail}");

            WatchStore.Save(watch);
        }
        return 0;
    }

    /// <summary>The lines <c>psst scheduled</c> prints under a job that is a watch; empty when it is not one.</summary>
    internal static IReadOnlyList<string> WatchLines(string taskName)
    {
        var id = ScheduledTaskLister.JobId(taskName);
        var watch = id is null ? null : WatchStore.Load(id);
        if (watch is null) return Array.Empty<string>();
        var lines = new List<string>
        {
            $"     ⚑ when {watch.Describe()}{(watch.Message.Length > 0 ? $": \"{Truncate(watch.Message, 60)}\"" : "")}",
            $"     ↻ looked at every {DurationParser.Format(TimeSpan.FromSeconds(watch.PollSeconds))}; " +
                (watch.Recurring ? $"recurring, announced {watch.FireCount.ToString(CultureInfo.InvariantCulture)} time(s)" : "once"),
        };
        if (watch.LastError is { Length: > 0 } error) lines.Add($"     ! {Truncate(error, 100)}");
        else if (watch.LastDetail is { Length: > 0 } detail) lines.Add($"     · last look: {Truncate(detail, 100)}");
        return lines;
    }
}
