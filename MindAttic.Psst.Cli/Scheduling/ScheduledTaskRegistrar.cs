namespace MindAttic.Psst.Cli.Scheduling;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Registers a one-shot Windows Task Scheduler entry that fires
/// <c>psst.exe</c> at a specific local time. Used to back the
/// <c>--schedule</c> / <c>--start</c> CLI flag.
///
/// <para>
/// Implementation note: rather than wrestle with <c>schtasks /TR</c> and
/// the Windows command-line escaping rules for embedded quotes, the
/// registrar writes a small companion <c>.cmd</c> launcher to
/// <c>%LOCALAPPDATA%\MindAttic\Psst\scheduled\&lt;id&gt;.cmd</c> and
/// schedules <em>that file</em>. The launcher invokes
/// <c>psst.exe</c> with the exact argv it received, so we never round-trip
/// the message text through schtasks' fragile quoting layer.
/// </para>
///
/// <para>
/// The scheduled task itself is registered with <c>/SC ONCE … /Z</c>, which
/// asks the scheduler to delete the task after it runs. The launcher
/// <c>.cmd</c> stays behind on disk; it's a few hundred bytes, and leaving
/// it provides a paper trail for users who want to inspect what was
/// scheduled.
/// </para>
/// </summary>
public static class ScheduledTaskRegistrar
{
    // Characters that force an argument to be double-quoted in the launcher
    // .cmd: whitespace and quote (so CommandLineToArgvW keeps the arg intact)
    // plus cmd.exe metacharacters (so cmd treats them literally rather than as
    // redirection/piping/grouping/separators).
    private static readonly char[] CmdSpecialChars = { ' ', '\t', '"', '\n', '\v', '&', '|', '<', '>', '^', '(', ')' };


    /// <summary>
    /// Outcome of a <see cref="RegisterAsync"/> call. Either the task was
    /// created (<see cref="Success"/> = true and <see cref="TaskName"/>
    /// populated), or <see cref="Error"/> describes what went wrong.
    /// </summary>
    public sealed record RegistrationResult(
        bool Success,
        string TaskName,
        string LauncherPath,
        DateTime FireTime,
        string? Error);

    /// <summary>
    /// Human-meaningful info about <em>what</em> a scheduled task will
    /// send. Written alongside the launcher <c>.cmd</c> as a JSON sidecar
    /// so <c>psst scheduled</c> can render a useful listing without
    /// reverse-parsing the command line.
    /// </summary>
    public sealed record SchedulingMetadata(
        string Recipient,
        string Message,
        int Repeat,
        int IntervalSeconds);

    /// <summary>
    /// Create a one-shot scheduled task that runs
    /// <paramref name="psstExePath"/> with <paramref name="psstArgs"/> at
    /// <paramref name="fireTime"/> (local).
    /// </summary>
    /// <param name="psstExePath">
    /// Absolute path to the <c>psst.exe</c> that should run when the task
    /// fires. Normally <c>Environment.ProcessPath</c>.
    /// </param>
    /// <param name="psstArgs">
    /// The argv (excluding the exe itself) that psst should receive. Must
    /// already have <c>--schedule</c> / <c>--start</c> stripped, otherwise
    /// the scheduled invocation would recursively schedule itself.
    /// </param>
    /// <param name="fireTime">
    /// The local wall-clock time at which to fire. Must be in the future;
    /// callers should resolve "next occurrence" semantics before calling.
    /// </param>
    public static async Task<RegistrationResult> RegisterAsync(
        string psstExePath,
        string[] psstArgs,
        DateTime fireTime,
        SchedulingMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(psstExePath))
            return new RegistrationResult(false, "", "", fireTime, "psst exe path was empty");
        if (!File.Exists(psstExePath))
            return new RegistrationResult(false, "", "", fireTime, $"psst exe not found at '{psstExePath}'");
        if (fireTime <= DateTime.Now)
            return new RegistrationResult(false, "", "", fireTime, $"fire time {fireTime:yyyy-MM-dd HH:mm} is not in the future");

        // Unique-ish task and launcher names. The hex slice keeps the task
        // name short enough to fit comfortably in Task Scheduler's UI.
        var id = Guid.NewGuid().ToString("N")[..12];
        var taskName = $"MindAttic.Psst.{id}";

        var launcherPath = await WriteLauncherCmdAsync(id, taskName, psstExePath, psstArgs);

        // Optional sidecar JSON. Only written when the caller supplies
        // metadata — we don't synthesize one from psstArgs so the
        // registrar stays a pure scheduling primitive.
        if (metadata is not null)
            await WriteSidecarAsync(id, taskName, fireTime, launcherPath, metadata);

        var registerError = RegisterWithSchtasks(taskName, launcherPath, fireTime);
        if (registerError is not null)
            return new RegistrationResult(false, taskName, launcherPath, fireTime, registerError);

        return new RegistrationResult(true, taskName, launcherPath, fireTime, null);
    }

    /// <summary>
    /// Returns the directory where launcher <c>.cmd</c> files and their
    /// JSON sidecars live: <c>%LOCALAPPDATA%\MindAttic\Psst\scheduled</c>.
    /// Centralized here so callers (registrar and lister) agree.
    /// </summary>
    public static string ScheduledDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MindAttic", "Psst", "scheduled");

    /// <summary>
    /// Materialize the launcher <c>.cmd</c> file. The file invokes
    /// <c>psst.exe</c> with the original argv, then deletes the
    /// scheduled task itself and the JSON sidecar so the system doesn't
    /// accumulate stale entries.
    ///
    /// <para>
    /// Self-deletion is done from inside the launcher (rather than via
    /// <c>schtasks /Z</c>) because <c>/Z</c> requires an <c>EndBoundary</c>
    /// that Windows doesn't synthesize from a bare <c>/SC ONCE /ST</c>.
    /// Doing the cleanup here keeps the registration call minimal and
    /// guarantees the cleanup actually runs.
    /// </para>
    /// </summary>
    private static async Task<string> WriteLauncherCmdAsync(
        string id,
        string taskName,
        string psstExePath,
        string[] psstArgs)
    {
        var dir = ScheduledDirectory();
        Directory.CreateDirectory(dir);

        var launcherPath = Path.Combine(dir, $"{id}.cmd");
        var sidecarPath = Path.Combine(dir, $"{id}.json");

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        // Switch the active code page to UTF-8 before the args reach
        // psst.exe. Without this, cmd.exe parses the .cmd file as the
        // OEM code page (typically CP437 / CP1252), turning any non-ASCII
        // character in the message text into mojibake by the time it
        // reaches the SMS payload. The `>nul` suppresses chcp's noisy
        // "Active code page: 65001" status line so the launcher stays
        // quiet under Task Scheduler.
        sb.AppendLine("chcp 65001 >nul");
        // Mark this invocation as scheduler-spawned so psst skips the
        // "implicit --schedule now when --interval is given" logic.
        // Without this, the deferred fire would just register another
        // scheduled task instead of actually running the send loop —
        // recursion that never reaches the SMS path.
        sb.AppendLine("set PSST_FROM_SCHEDULE=1");
        // Run psst with the original argv.
        sb.AppendLine(BuildInvocationLine(psstExePath, psstArgs));
        sb.AppendLine("set PSST_EXIT=%ERRORLEVEL%");
        // Self-cleanup: delete the scheduled task and the JSON sidecar.
        // Errors are swallowed — the cleanup is best-effort and we don't
        // want a missing file to mask the psst exit code.
        sb.AppendLine($"schtasks /Delete /TN \"{taskName}\" /F >nul 2>nul");
        sb.AppendLine($"del /Q \"{sidecarPath}\" >nul 2>nul");
        sb.AppendLine("exit /b %PSST_EXIT%");

        await File.WriteAllTextAsync(launcherPath, sb.ToString(), Encoding.UTF8);
        return launcherPath;
    }

    /// <summary>
    /// Write the sidecar JSON next to the launcher. Carries the
    /// human-meaningful "what" of the scheduled send (recipient, message,
    /// repeat, interval) so <c>psst scheduled</c> can render it without
    /// reverse-engineering the command line.
    /// </summary>
    private static async Task WriteSidecarAsync(
        string id,
        string taskName,
        DateTime fireTime,
        string launcherPath,
        SchedulingMetadata metadata)
    {
        var sidecarPath = Path.Combine(ScheduledDirectory(), $"{id}.json");
        var payload = new
        {
            id,
            taskName,
            fireTime = fireTime.ToString("o"), // ISO 8601 round-trip with local offset
            launcherPath,
            recipient = metadata.Recipient,
            message = metadata.Message,
            repeat = metadata.Repeat,
            intervalSeconds = metadata.IntervalSeconds,
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Shell out to <c>schtasks.exe /Create … /SC ONCE … /Z</c>. Using
    /// <see cref="ProcessStartInfo.ArgumentList"/> hands each argument to
    /// schtasks as a discrete string, sidestepping the manual /command-line
    /// quoting pitfalls that plague every other tutorial on this topic.
    /// Returns <c>null</c> on success or an error string on failure.
    /// </summary>
    private static string? RegisterWithSchtasks(string taskName, string launcherPath, DateTime fireTime)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/Create");
        psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(taskName);
        psi.ArgumentList.Add("/SC"); psi.ArgumentList.Add("ONCE");
        // schtasks parses /SD using the *current user's* Windows short-date
        // pattern, not en-US. Hard-coding MM/dd/yyyy worked on US machines
        // and silently failed (or fired on the wrong day) under dd/MM/yyyy
        // or yyyy-MM-dd locales. Read the actual pattern out of CultureInfo
        // and let DateTime.ToString render it.
        var datePattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        psi.ArgumentList.Add("/SD"); psi.ArgumentList.Add(fireTime.ToString(datePattern, CultureInfo.CurrentCulture));
        psi.ArgumentList.Add("/ST"); psi.ArgumentList.Add(fireTime.ToString("HH:mm", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("/TR"); psi.ArgumentList.Add(launcherPath);
        psi.ArgumentList.Add("/F"); // overwrite if a stale task with the same name exists
        // Note: /Z (auto-delete after run) is intentionally omitted because
        // schtasks rejects it without an explicit /ED EndBoundary on
        // one-shot tasks. The launcher .cmd self-deletes the task and
        // sidecar after firing, achieving the same effect.

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return "failed to launch schtasks.exe";
            // Drain both streams concurrently to avoid the classic
            // pipe-buffer deadlock when stderr fills before stdout drains.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (proc.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return $"schtasks exited {proc.ExitCode}: {detail.Trim()}";
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"could not invoke schtasks.exe: {ex.Message}";
        }
    }

    /// <summary>
    /// Build the line that invokes <c>psst.exe</c> inside the launcher
    /// <c>.cmd</c>. Quotes the exe path and each argument for the
    /// <c>CommandLineToArgvW</c> parser, then doubles every <c>%</c> so
    /// cmd.exe's batch percent-expansion — which runs <em>before</em> psst's
    /// argv parser and which double quotes do <em>not</em> suppress — leaves
    /// literal percents and <c>%VAR%</c> sequences in the message intact
    /// instead of eating them or splicing environment variables into the SMS.
    /// (<c>%%</c> reduces back to a single <c>%</c> on a batch-file line.)
    /// </summary>
    internal static string BuildInvocationLine(string psstExePath, string[] psstArgs)
    {
        var sb = new StringBuilder();
        // Quote the exe path even when it has no spaces — cheap, and removes
        // any ambiguity about where the exe ends and the args begin.
        sb.Append('"').Append(psstExePath).Append('"');
        foreach (var a in psstArgs)
            sb.Append(' ').Append(QuoteForWindowsCommandLine(a));
        return sb.ToString().Replace("%", "%%");
    }

    /// <summary>
    /// Quote a single argument using Windows <c>CommandLineToArgvW</c>
    /// rules: surround with double quotes if needed, escape embedded
    /// quotes as <c>\"</c>, and double-up any trailing backslashes that
    /// precede an embedded quote so they don't combine.
    /// </summary>
    internal static string QuoteForWindowsCommandLine(string arg)
    {
        // Args with nothing the argv parser splits on (whitespace/quotes) and
        // nothing cmd.exe treats specially can be passed through verbatim.
        // The cmd metacharacters & | < > ^ ( ) must force quoting: wrapped in
        // double quotes cmd treats them literally, whereas a bare "a&b" would
        // otherwise be parsed as the command `a` followed by a separate
        // command `b`. (Percent is handled separately by %%-doubling above.)
        if (arg.Length > 0 && arg.IndexOfAny(CmdSpecialChars) < 0)
            return arg;

        var sb = new StringBuilder();
        sb.Append('"');
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // 2N+1 backslashes — N pairs survive, plus one escapes the quote.
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(c);
            }
            backslashes = 0;
        }
        // Trailing backslashes have to be doubled too, because the closing
        // quote we're about to append would otherwise be eaten by them.
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
