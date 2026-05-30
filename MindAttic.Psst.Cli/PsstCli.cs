namespace MindAttic.Psst.Cli;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using MindAttic.Psst;
using MindAttic.Psst.Cli.Scheduling;
using MindAttic.Psst.Configuration;
using MindAttic.Psst.Contacts;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;
using MindAttic.Psst.Time;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.Paths;

/// <summary>
/// Top-level CLI for the <c>psst</c> tool. Wraps a command line, runs it to
/// completion, then plays the ICQ Psst sound and fires an SMS through the
/// first available transport (Twilio, then email-to-SMS).
///
/// <para>Usage:</para>
/// <code>
///   psst [--silent] -- &lt;command&gt; [args...]
///                                       Run the command, notify on exit.
///   psst [--silent] test [message]      Fire a test notification immediately.
///   psst ping                           Show what's configured + what would fire.
///   psst sound                          Play just the Psst sound (sanity check).
///   psst contacts [list|add|rm]         Manage the contact book.
///   psst sms [flags] &lt;to&gt; &lt;message...&gt;   Send a one-off SMS.
/// </code>
///
/// <para>
/// The <c>sms</c> subcommand also supports repetition and scheduling via
/// <c>--repeat</c>, <c>--interval</c> (alias <c>--every</c>), and
/// <c>--schedule</c> (alias <c>--start</c>). See
/// <see cref="SmsAsync"/> and <see cref="ParseSmsFlags"/>.
/// </para>
/// </summary>
public sealed class PsstCli
{
    public async Task<int> RunAsync(string[] args)
    {
        // Make the ✓/✗/♪ glyphs render correctly in legacy code-page consoles.
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected */ }

        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        // Strip --silent from anywhere among the leading args. Historically
        // we only accepted it as the very first token; users who typed
        // `psst test --silent "msg"` got it silently swallowed into the
        // message body. Stop short of the `--` argv divider so wrapped
        // commands receive their own `--silent` (if any) intact.
        var silent = false;
        var filtered = new List<string>(args.Length);
        var sawArgvDivider = false;
        foreach (var a in args)
        {
            if (sawArgvDivider) { filtered.Add(a); continue; }
            if (a == "--")     { sawArgvDivider = true; filtered.Add(a); continue; }
            if (a == "--silent") { silent = true; continue; }
            filtered.Add(a);
        }
        args = filtered.ToArray();

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var configuration = BuildConfiguration();
        var psstConfig = PsstConfiguration.Load(configuration);

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "--"                       => await WrapAsync(args.Skip(1).ToArray(), psstConfig, silent),
                "test"                     => await TestAsync(args.Skip(1).ToArray(), psstConfig, silent),
                "ping"                     => Ping(psstConfig),
                "sound"                    => await SoundAsync(),
                "contacts"                 => Contacts(args.Skip(1).ToArray()),
                "sms"                      => await SmsAsync(args.Skip(1).ToArray(), psstConfig),
                "scheduled" or "pending"   => await ScheduledAsync(args.Skip(1).ToArray()),
                _                          => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Build the canonical MindAttic.Vault configuration chain. Sources, lowest
    /// precedence first (later wins):
    /// <list type="number">
    ///   <item><description>
    ///     <c>AddMindAtticVaultFiles()</c> — legacy <c>%APPDATA%/MindAttic/&lt;bucket&gt;/providers.json</c>
    ///     (LLM, Brokers, etc.). A no-op for Psst-only users; lets shared family secrets flow in if present.
    ///   </description></item>
    ///   <item><description><c>./appsettings.json</c> from the working directory (optional, legacy).</description></item>
    ///   <item><description><c>%APPDATA%/MindAttic/Psst/settings.json</c> — primary Psst config, lives outside the repo.</description></item>
    ///   <item><description>Environment variables — final override, useful for CI / containers.</description></item>
    /// </list>
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder();

        // 1. Vault file source (legacy %APPDATA%\MindAttic\<bucket>\providers.json).
        builder.AddMindAtticVaultFiles();

        // 2. Legacy CWD appsettings.json.
        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        // 3. Primary settings.json under %APPDATA%\MindAttic\Psst.
        builder.AddJsonFile(PsstConfigurationSources.GetSettingsPath(), optional: true, reloadOnChange: false);

        // 4. Environment variables (highest priority).
        builder.AddEnvironmentVariables();

        return builder.Build();
    }

    /// <summary>Run the wrapped command, notify regardless of exit code, return the child's code.</summary>
    private static async Task<int> WrapAsync(string[] commandArgs, PsstConfiguration config, bool silent)
    {
        if (commandArgs.Length == 0)
        {
            Console.Error.WriteLine("usage: psst -- <command> [args...]");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            // Resolve through PATH/PATHEXT first. With UseShellExecute=false,
            // Process.Start only auto-appends ".exe", so a bare "npm"/"yarn"/
            // "tsc" (which ship as .cmd shims on Windows) fails with "cannot
            // find the file". Handing CreateProcess the full path — including
            // the .cmd extension — lets it launch them.
            FileName = ResolveExecutable(commandArgs[0]),
            UseShellExecute = false,
        };
        // ArgumentList escapes per-arg; avoids the quoting bugs that come from
        // hand-building a command line out of space-joined strings.
        for (var i = 1; i < commandArgs.Length; i++)
            psi.ArgumentList.Add(commandArgs[i]);

        // Suppress the default Ctrl-C terminate-immediately handler so we
        // stay alive long enough to notify when the user cancels. The child
        // process is in the same console process group and receives the same
        // Ctrl-C event, so it'll exit on its own; we just need to keep
        // running until its WaitForExitAsync returns.
        ConsoleCancelEventHandler cancelHandler = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += cancelHandler;

        var sw = Stopwatch.StartNew();
        int exitCode;
        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"failed to start: {psi.FileName}");
            await proc.WaitForExitAsync();
            exitCode = proc.ExitCode;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.CancelKeyPress -= cancelHandler;
            var failMsg = $"psst: '{psi.FileName}' failed to start — {ex.Message}";
            await Notify(config, failMsg, silent);
            Console.Error.WriteLine(failMsg);
            return 2;
        }
        sw.Stop();
        Console.CancelKeyPress -= cancelHandler;

        var status = exitCode == 0 ? "OK" : $"FAIL ({DescribeExitCode(exitCode)})";
        var elapsed = FormatElapsed(sw.Elapsed);
        var message = $"psst: {psi.FileName} — {status} in {elapsed}";

        await Notify(config, message, silent);
        return exitCode;
    }

    /// <summary>
    /// Map well-known Windows NTSTATUS exit codes to readable labels. Falls
    /// back to <c>exit N</c> for unknown codes. Keeps the auto-notification
    /// from showing things like <c>FAIL (exit -1073741510)</c> when the
    /// user just hit Ctrl-C.
    /// </summary>
    private static string DescribeExitCode(int exitCode) => exitCode switch
    {
        unchecked((int)0xC000013A) => "Ctrl-C",            // STATUS_CONTROL_C_EXIT
        unchecked((int)0xC0000005) => "access violation",  // STATUS_ACCESS_VIOLATION
        unchecked((int)0xC000001D) => "illegal instruction",
        unchecked((int)0xC0000094) => "divide by zero",
        unchecked((int)0xC00000FD) => "stack overflow",
        unchecked((int)0xC0000409) => "stack buffer overrun",
        unchecked((int)0xC0000374) => "heap corruption",
        _                          => $"exit {exitCode}",
    };

    /// <summary>Fire a notification right now with a fixed or user-supplied message.</summary>
    private static async Task<int> TestAsync(string[] args, PsstConfiguration config, bool silent)
    {
        var message = args.Length > 0
            ? string.Join(' ', args)
            : "psst: test notification from MindAttic.Psst";
        await Notify(config, message, silent);
        return 0;
    }

    /// <summary>Print what transports would fire without sending anything.</summary>
    private static int Ping(PsstConfiguration config)
    {
        var derivedFanout = CarrierGateways.BuildFanout(config.RecipientPhoneNumber);
        var effectiveEmailRecipients = CarrierGateways.Combine(config.RecipientEmailSmsAddress, derivedFanout);

        var twilioStatus = config.Twilio is null
            ? "not configured"
            : (PsstFeatures.TwilioEnabled
                ? $"configured ({config.Twilio.From} → {config.RecipientPhoneNumber ?? "<missing to>"})"
                : $"configured but DISABLED via PsstFeatures.TwilioEnabled — {config.Twilio.From} → {config.RecipientPhoneNumber ?? "<missing to>"}");
        Console.WriteLine("MindAttic.Psst — config check");
        Console.WriteLine($"  Twilio:        {twilioStatus}");
        Console.WriteLine($"  Email-to-SMS:  {(config.Email is null ? "not configured" : "configured (" + config.Email.From + ")")}");
        Console.WriteLine($"  Recipient #:   {config.RecipientPhoneNumber ?? "<unset>"}");
        Console.WriteLine($"  Recipient @ explicit:  {config.RecipientEmailSmsAddress ?? "<unset>"}");
        if (effectiveEmailRecipients is not null)
        {
            var count = effectiveEmailRecipients.Split(',').Length;
            Console.WriteLine($"  Effective fanout ({count} gateway{(count == 1 ? "" : "s")}):");
            foreach (var addr in effectiveEmailRecipients.Split(','))
                Console.WriteLine($"    · {addr}");
        }

        var settingsPath = PsstConfigurationSources.GetSettingsPath();
        Console.WriteLine();
        Console.WriteLine("Sources (lowest → highest precedence):");
        Console.WriteLine($"  · vault files:   {VaultPaths.RoamingRoot}{Path.DirectorySeparatorChar}<LLM|Brokers>{Path.DirectorySeparatorChar}providers.json");
        Console.WriteLine($"  {SourceMark(File.Exists("appsettings.json"))} appsettings:    .{Path.DirectorySeparatorChar}appsettings.json");
        Console.WriteLine($"  {SourceMark(File.Exists(settingsPath))} settings.json:  {settingsPath}");
        Console.WriteLine($"  · env vars:      MindAttic__Vault__Notifications__*");

        if (config.Errors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Diagnostics:");
            foreach (var err in config.Errors)
                Console.WriteLine($"  ! {err}");
        }

        Console.WriteLine();
        if (!config.HasAnySmsTransport)
        {
            Console.WriteLine("No SMS transports configured. Pick one approach:");
            Console.WriteLine($"  • Create the canonical Notifications credential file:");
            Console.WriteLine($"      {Path.Combine(VaultPaths.RoamingRoot, "Notifications", "providers.json")}");
            Console.WriteLine("      {");
            Console.WriteLine("        \"twilio\": {");
            Console.WriteLine("          \"accountSid\": \"AC...\", \"authToken\": \"...\", \"from\": \"+15555550100\"");
            Console.WriteLine("        },");
            Console.WriteLine("        \"email\": {");
            Console.WriteLine("          \"smtpHost\": \"smtp.example.com\", \"smtpPort\": 587,");
            Console.WriteLine("          \"username\": \"user\", \"password\": \"***\", \"from\": \"psst@example.com\"");
            Console.WriteLine("        },");
            Console.WriteLine("        \"to\": \"+15555550101\"");
            Console.WriteLine("      }");
            Console.WriteLine($"  • Or place the same settings in {settingsPath} under MindAttic:Vault:Notifications.");
        }
        return 0;

        static string SourceMark(bool found) => found ? "✓" : "·";
    }

    /// <summary>Play the Psst sound and exit. No SMS.</summary>
    private static async Task<int> SoundAsync()
    {
        var result = await PsstSoundPlayer.PlayAsync();
        if (result.Success)
        {
            Console.WriteLine($"played via {result.Transport}.");
            return 0;
        }
        Console.Error.WriteLine($"could not play: {result.Error}");
        return 1;
    }

    /// <summary>Top-level dispatch for the <c>contacts</c> subcommand.</summary>
    private static int Contacts(string[] args)
    {
        var sub = args.Length == 0 ? "list" : args[0].ToLowerInvariant();
        return sub switch
        {
            "list" or "ls"               => ContactsList(),
            "add"                        => ContactsAdd(args.Skip(1).ToArray()),
            "rm" or "remove" or "del"    => ContactsRemove(args.Skip(1).ToArray()),
            _                            => ContactsUsage(sub),
        };
    }

    private static int ContactsList()
    {
        var load = ContactStore.TryLoad();
        var book = load.Book;
        var path = ContactStore.GetPath();
        if (load.Error is not null)
            Console.Error.WriteLine($"warning: {load.Error}");
        Console.WriteLine($"Contact book ({path}):");
        if (book.Contacts.Count == 0)
        {
            Console.WriteLine("  (empty)");
            Console.WriteLine();
            Console.WriteLine("Add one with:  psst contacts add <name> <phone>");
            return 0;
        }
        var nameWidth = book.Contacts.Max(c => c.Name.Length);
        var phoneWidth = book.Contacts.Max(c => c.Phone.Length);
        foreach (var c in book.Contacts)
        {
            var viaSuffix = c.DefaultVia is { } dv ? $"  [via {PsstViaResolver.Format(dv)}]" : "";
            Console.WriteLine($"  {c.Name.PadRight(nameWidth)}  {c.Phone.PadRight(phoneWidth)}{viaSuffix}");
        }
        return 0;
    }

    private static int ContactsAdd(string[] args)
    {
        // Pull --via out of argv before positional handling. Anywhere among
        // the args is fine; this matches the `sms` flag-placement convention.
        string? viaArg = null;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--via")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--via needs a value (twilio | email)");
                    return 1;
                }
                if (!PsstViaResolver.TryParse(args[i + 1], out _))
                {
                    Console.Error.WriteLine($"--via got '{args[i + 1]}' — expected 'twilio' or 'email'");
                    return 1;
                }
                viaArg = args[i + 1];
                i++;
                continue;
            }
            positional.Add(args[i]);
        }
        if (positional.Count != 2)
        {
            Console.Error.WriteLine("usage: psst contacts add [--via twilio|email] <name> <phone>");
            return 1;
        }

        var (requestedName, phone) = (positional[0], positional[1]);
        PsstVia? defaultVia = null;
        if (viaArg is not null && PsstViaResolver.TryParse(viaArg, out var parsedVia))
            defaultVia = parsedVia;

        var addLoad = ContactStore.TryLoad();
        if (addLoad.Error is not null)
        {
            Console.Error.WriteLine($"error: {addLoad.Error}");
            Console.Error.WriteLine("Refusing to add — fix or remove contacts.json first to avoid clobbering existing entries.");
            return 1;
        }
        var book = addLoad.Book;

        // Case-insensitive collision check: if "Ryan" exists and the user
        // adds "ryan", auto-suffix to the first available "<name>N" (N≥2)
        // and warn. Matches the convention the user wanted: ryan, ryan2,
        // ryan3, … — first instance unsuffixed.
        var finalName = NextAvailableContactName(book, requestedName);
        if (!finalName.Equals(requestedName, StringComparison.Ordinal))
            Console.Error.WriteLine($"warning: '{requestedName}' already exists (case-insensitive); saving as '{finalName}'");

        try
        {
            var added = book.WithAdded(new Contact(finalName, phone, defaultVia));
            ContactStore.Save(added);
            var viaSuffix = defaultVia is { } dv ? $" [via {PsstViaResolver.Format(dv)}]" : "";
            Console.WriteLine($"added '{finalName}' → {phone}{viaSuffix}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Walk numeric suffixes starting at 2 and return the first
    /// <c>baseName + N</c> that isn't already taken (case-insensitive).
    /// Returns <paramref name="baseName"/> unchanged when no collision
    /// exists.
    /// </summary>
    private static string NextAvailableContactName(ContactBook book, string baseName)
    {
        bool Exists(string name) =>
            book.Contacts.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (!Exists(baseName)) return baseName;
        var n = 2;
        while (Exists(baseName + n)) n++;
        return baseName + n;
    }

    private static int ContactsRemove(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: psst contacts rm <name>");
            return 1;
        }
        var rmLoad = ContactStore.TryLoad();
        if (rmLoad.Error is not null)
        {
            Console.Error.WriteLine($"error: {rmLoad.Error}");
            return 1;
        }
        var book = rmLoad.Book;
        var updated = book.WithoutContact(args[0]);
        if (updated is null)
        {
            Console.Error.WriteLine($"no contact named '{args[0]}'");
            return 1;
        }
        ContactStore.Save(updated);
        Console.WriteLine($"removed '{args[0]}'");
        return 0;
    }

    private static int ContactsUsage(string sub)
    {
        Console.Error.WriteLine($"unknown contacts subcommand: {sub}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  psst contacts [list]                                 List all contacts");
        Console.Error.WriteLine("  psst contacts add [--via twilio|email] <name> <phone>  Add a contact");
        Console.Error.WriteLine("  psst contacts rm <name>                              Remove a contact");
        return 1;
    }

    /// <summary>
    /// One-line usage hint reused by every error path in <see cref="SmsAsync"/>
    /// and <see cref="ParseSmsFlags"/>. Kept in one place so the canonical
    /// flag spelling never drifts between the parser and the help text.
    /// </summary>
    private const string SmsUsage =
        "usage: psst sms [--via twilio|email] [--repeat N] [--interval|--every <30s|5m|2h|1d>] " +
        "[--schedule|--start <10:30am>] <name-or-phone> <message...>";

    /// <summary>
    /// Result of parsing the <c>sms</c> subcommand's argv. Carries both the
    /// extracted flag values and two derived argv lists:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="Positional"/> — non-flag args in original order. The
    ///     first is the recipient, the rest are the message words.
    ///   </item>
    ///   <item>
    ///     <see cref="ArgsWithoutSchedule"/> — every original arg
    ///     <em>except</em> the <c>--schedule</c> / <c>--start</c> pair.
    ///     Used when registering a scheduled task, so the deferred
    ///     invocation runs the send path instead of recursively scheduling
    ///     itself.
    ///   </item>
    /// </list>
    /// On parse failure <see cref="Error"/> is populated and all other
    /// fields hold sentinel values.
    /// </summary>
    private sealed record ParsedSmsFlags(
        string[] Positional,
        int Repeat,
        TimeSpan Interval,
        DateTime? ScheduledFor,
        string? Via,
        string[] ArgsWithoutSchedule,
        string? Error);

    /// <summary>
    /// Send a one-off SMS to a contact (by name) or an arbitrary US phone
    /// number. Picks one transport per send (no fallback chain) per
    /// <see cref="PsstViaResolver"/>: <c>--via</c> &gt; <c>PSST_VIA</c> env
    /// var &gt; the contact's <see cref="Contact.DefaultVia"/> &gt; project
    /// default (email-to-SMS). No audio cue.
    ///
    /// <para>Supported flags (parsed by <see cref="ParseSmsFlags"/>):</para>
    /// <list type="bullet">
    ///   <item><c>--via twilio|email</c> — explicit transport for this send. Overrides every lower-precedence source.</item>
    ///   <item><c>--repeat N</c> — send the message <c>N</c> times total. Default <c>1</c>.</item>
    ///   <item><c>--interval D</c> / <c>--every D</c> — delay between sends. Required when <c>--repeat &gt; 1</c>.</item>
    ///   <item><c>--schedule T</c> / <c>--start T</c> — defer the first send to local time <c>T</c> (next occurrence) via Windows Task Scheduler. Combines with <c>--repeat</c>/<c>--interval</c>: the cadence loop runs starting from the scheduled fire time.</item>
    /// </list>
    /// </summary>
    private static async Task<int> SmsAsync(string[] args, PsstConfiguration config)
    {
        var parse = ParseSmsFlags(args);
        if (parse.Error is not null)
        {
            Console.Error.WriteLine($"error: {parse.Error}");
            Console.Error.WriteLine(SmsUsage);
            return 1;
        }

        if (parse.Positional.Length < 2)
        {
            Console.Error.WriteLine(SmsUsage);
            return 1;
        }

        var recipient = parse.Positional[0];
        var message = string.Join(' ', parse.Positional.Skip(1));

        // Recipient resolution: contact-book lookup first (case-insensitive),
        // falling back to bare US phone-number parsing. Anything that isn't
        // either is a hard error — we don't want to silently send to a typo.
        var smsLoad = ContactStore.TryLoad();
        if (smsLoad.Error is not null)
            Console.Error.WriteLine($"warning: {smsLoad.Error}");
        var book = smsLoad.Book;
        var contact = book.Find(recipient);

        string phone;
        string label;
        if (contact is not null)
        {
            phone = contact.Phone;
            label = $"{contact.Name} ({contact.Phone})";
        }
        else
        {
            var digits = CarrierGateways.NormalizeTo10Digits(recipient);
            if (digits is null)
            {
                Console.Error.WriteLine($"'{recipient}' is not a known contact and doesn't look like a US phone number.");
                Console.Error.WriteLine("Add a contact with:  psst contacts add <name> <phone>");
                return 1;
            }
            phone = "+1" + digits;
            label = phone;
        }

        // Resolve which transport this send uses. Precedence (highest →
        // lowest): --via flag > PSST_VIA env var > contact's DefaultVia >
        // project default (email). The chosen transport is the *only* one
        // attempted — no fallback chain.
        var via = PsstViaResolver.Resolve(
            cliFlagValue:    parse.Via,
            contactDefault:  contact?.DefaultVia is { } cdv ? PsstViaResolver.Format(cdv) : null);

        if (via == PsstVia.Email && config.Email is null)
        {
            Console.Error.WriteLine("email transport is not configured — can't send via email.");
            Console.Error.WriteLine("Try `--via twilio`, or run `psst ping` to see what's wired up.");
            return 1;
        }
        if (via == PsstVia.Twilio && (!PsstFeatures.TwilioEnabled || config.Twilio is null))
        {
            Console.Error.WriteLine(PsstFeatures.TwilioEnabled
                ? "twilio transport is not configured — can't send via twilio."
                : "twilio transport is disabled at compile time (PsstFeatures.TwilioEnabled = false).");
            Console.Error.WriteLine("Try `--via email`, or run `psst ping` to see what's wired up.");
            return 1;
        }

        // --schedule / --start path: hand off to Windows Task Scheduler and
        // exit. The scheduled invocation will run the send-and-loop path
        // below (because ArgsWithoutSchedule has the schedule flag stripped).
        if (parse.ScheduledFor.HasValue)
        {
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("could not resolve current psst.exe path");
            var scheduledArgs = new List<string> { "sms" };
            scheduledArgs.AddRange(parse.ArgsWithoutSchedule);

            // Stash human-readable metadata next to the launcher so
            // `psst scheduled` can list the pending send without
            // reverse-engineering the .cmd file.
            var metadata = new ScheduledTaskRegistrar.SchedulingMetadata(
                Recipient:       label,
                Message:         message,
                Repeat:          parse.Repeat,
                IntervalSeconds: (int)parse.Interval.TotalSeconds);

            var reg = await ScheduledTaskRegistrar.RegisterAsync(
                exePath, scheduledArgs.ToArray(), parse.ScheduledFor.Value, metadata);
            if (!reg.Success)
            {
                Console.Error.WriteLine($"error: could not schedule task — {reg.Error}");
                return 1;
            }
            Console.WriteLine($"→ {label}: {message}");
            Console.WriteLine($"  ⏰ scheduled for {parse.ScheduledFor.Value:yyyy-MM-dd HH:mm} (task: {reg.TaskName})");
            if (parse.Repeat > 1)
                Console.WriteLine($"  ↻ then will repeat {parse.Repeat - 1} more time(s) every {DurationParser.Format(parse.Interval)}");
            Console.WriteLine($"  · launcher: {reg.LauncherPath}");
            return 0;
        }

        // Send path. Replace just the recipient phone for this send; null
        // out any explicit `toEmail` so the fanout is purely the override
        // number's carrier gateways.
        var overridden = config with
        {
            RecipientPhoneNumber = phone,
            RecipientEmailSmsAddress = null,
        };
        // `await using` so the underlying email transport gets a chance to
        // QUIT its SMTP session at the end of a --repeat loop instead of
        // having the OS yank the socket at process exit.
        await using var notifier = new PsstNotifier(overridden, via);

        var allSucceeded = true;
        for (var i = 0; i < parse.Repeat; i++)
        {
            // Inter-send delay. Printed before the sleep so the user sees
            // progress in the terminal even when the loop is long-running.
            if (i > 0)
            {
                Console.WriteLine($"  · sleeping {DurationParser.Format(parse.Interval)} before send {i + 1} of {parse.Repeat}…");
                try { await Task.Delay(parse.Interval); }
                catch (TaskCanceledException) { return 1; }
            }

            var result = await notifier.NotifyAsync(message, silent: true);
            var sendLabel = parse.Repeat == 1 ? "" : $" [{i + 1}/{parse.Repeat}]";
            Console.WriteLine($"→ {label}{sendLabel}: {message}");
            foreach (var a in result.SmsAttempts)
                Console.WriteLine($"  {(a.Success ? "✓" : "✗")} {a.Transport}: {a.Detail}");
            if (!result.AnySmsSent) allSucceeded = false;
        }
        return allSucceeded ? 0 : 1;
    }

    /// <summary>
    /// Walk the <c>sms</c> argv once, peeling off recognized flags and
    /// returning everything else as positional args. Flags may appear in
    /// any position, but a flag's value must immediately follow it.
    ///
    /// <para>
    /// Accepted flags (case-sensitive — matches the rest of the CLI):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>--repeat &lt;positive-int&gt;</c></item>
    ///   <item><c>--interval &lt;duration&gt;</c> or <c>--every &lt;duration&gt;</c></item>
    ///   <item><c>--schedule &lt;time&gt;</c> or <c>--start &lt;time&gt;</c></item>
    /// </list>
    ///
    /// <para>
    /// Validation rules enforced here (so the call site stays linear):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>--repeat</c> must be ≥ 1.</item>
    ///   <item><c>--interval</c> / <c>--every</c> must be a strictly positive duration.</item>
    ///   <item><c>--repeat &gt; 1</c> requires <c>--interval</c> / <c>--every</c> — otherwise a typo could fire a flood of messages with no spacing.</item>
    /// </list>
    ///
    /// <para>
    /// Convenience rule: if <c>--interval</c> / <c>--every</c> is present
    /// but <c>--schedule</c> / <c>--start</c> is not, the parser fills in
    /// an implicit "schedule for now" — specifically the next whole-minute
    /// boundary (Task Scheduler's <c>/ST</c> only accepts minute
    /// precision). This makes a drip loop detach into a scheduled task
    /// instead of blocking the originating shell process.
    /// </para>
    /// </summary>
    private static ParsedSmsFlags ParseSmsFlags(string[] args)
    {
        var positional = new List<string>();
        var withoutSchedule = new List<string>();
        var repeat = 1;
        var interval = TimeSpan.Zero;
        var hasInterval = false;
        DateTime? scheduledFor = null;
        string? via = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];

            if (a == "--repeat")
            {
                if (i + 1 >= args.Length)
                    return Fail("--repeat needs a positive integer");
                if (!int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
                    return Fail($"--repeat needs a positive integer (got '{args[i + 1]}')");
                repeat = n;
                withoutSchedule.Add(a);
                withoutSchedule.Add(args[i + 1]);
                i++;
                continue;
            }

            if (a == "--interval" || a == "--every")
            {
                if (i + 1 >= args.Length)
                    return Fail($"{a} needs a duration (e.g. 30s, 5m, 2h, 1d)");
                if (!DurationParser.TryParse(args[i + 1], out var d) || d <= TimeSpan.Zero)
                    return Fail($"{a} got '{args[i + 1]}' — expected something like 30s, 5m, 2h, 1d");
                interval = d;
                hasInterval = true;
                withoutSchedule.Add(a);
                withoutSchedule.Add(args[i + 1]);
                i++;
                continue;
            }

            if (a == "--schedule" || a == "--start")
            {
                if (i + 1 >= args.Length)
                    return Fail($"{a} needs a time (e.g. 10:30am, 2:30pm, 10:30, 22:30)");
                if (!TimeOfDayParser.TryParse(args[i + 1], DateTime.Now, out var when))
                    return Fail($"{a} got '{args[i + 1]}' — expected like 10:30am, 2:30pm, 10:30, 22:30");
                scheduledFor = when;
                // Intentionally do NOT add --schedule/--start to
                // withoutSchedule — the scheduled-fire invocation must not
                // recursively re-schedule itself.
                i++;
                continue;
            }

            if (a == "--via")
            {
                if (i + 1 >= args.Length)
                    return Fail("--via needs a value (twilio | email)");
                if (!PsstViaResolver.TryParse(args[i + 1], out _))
                    return Fail($"--via got '{args[i + 1]}' — expected 'twilio' or 'email'");
                via = args[i + 1];
                withoutSchedule.Add(a);
                withoutSchedule.Add(args[i + 1]);
                i++;
                continue;
            }

            positional.Add(a);
            withoutSchedule.Add(a);
        }

        if (repeat > 1 && !hasInterval)
            return Fail("--repeat > 1 requires --interval / --every (e.g. --interval 30m)");

        // Implicit `--schedule now` when an interval is present but no
        // explicit schedule. Hands the drip loop off to Task Scheduler so
        // the originating shell doesn't have to stay open.
        //
        // Skip this branch when we're already running as the deferred
        // child of a scheduled task (marker set by the launcher .cmd) —
        // otherwise the fire would just re-schedule itself instead of
        // sending, which is the worst kind of bug: silent infinite
        // recursion that never reaches the actual SMS path.
        if (hasInterval && !scheduledFor.HasValue && !IsFromScheduler())
            scheduledFor = NextMinuteBoundary(DateTime.Now);

        return new ParsedSmsFlags(
            positional.ToArray(),
            repeat,
            interval,
            scheduledFor,
            via,
            withoutSchedule.ToArray(),
            Error: null);

        static ParsedSmsFlags Fail(string err) =>
            new(Array.Empty<string>(), 1, TimeSpan.Zero, null, null, Array.Empty<string>(), err);
    }

    /// <summary>
    /// True iff this <c>psst</c> process was spawned by a launcher
    /// <c>.cmd</c> registered by <see cref="ScheduledTaskRegistrar"/>.
    /// The launcher sets <c>PSST_FROM_SCHEDULE=1</c> in its environment
    /// before invoking <c>psst.exe</c> so deferred fires can suppress
    /// the implicit <c>--schedule now</c> branch and actually run the
    /// send path instead of recursively re-scheduling themselves.
    /// </summary>
    private static bool IsFromScheduler() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PSST_FROM_SCHEDULE"),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// Earliest fire-time we can hand to <c>schtasks /ST</c>, which only
    /// supports minute precision. Returns the next whole-minute boundary
    /// strictly after <paramref name="now"/>, with a one-minute cushion
    /// when we're within 5 seconds of the boundary so the registration
    /// call doesn't race the trigger.
    /// </summary>
    private static DateTime NextMinuteBoundary(DateTime now)
    {
        var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Local)
            .AddMinutes(1);
        if (next - now < TimeSpan.FromSeconds(5))
            next = next.AddMinutes(1);
        return next;
    }

    /// <summary>
    /// Dispatch for the <c>scheduled</c> / <c>pending</c> subcommand.
    /// Subcommands:
    /// <list type="bullet">
    ///   <item><c>list</c> (default) — render a table of pending Psst tasks.</item>
    ///   <item><c>cancel &lt;task-name&gt;</c> (also: <c>rm</c>, <c>delete</c>) — remove one.</item>
    ///   <item><c>clear</c> — cancel every pending Psst task at once.</item>
    /// </list>
    /// </summary>
    private static async Task<int> ScheduledAsync(string[] args)
    {
        var sub = args.Length == 0 ? "list" : args[0].ToLowerInvariant();
        return sub switch
        {
            "list" or "ls"                       => await ScheduledListAsync(),
            "cancel" or "rm" or "delete" or "del" => await ScheduledCancelAsync(args.Skip(1).ToArray()),
            "clear"                              => await ScheduledClearAsync(),
            _                                    => ScheduledUsage(sub),
        };
    }

    /// <summary>
    /// Print the pending task list. Empty list is a soft success with a
    /// hint, not an error — "nothing pending" is a perfectly valid state.
    /// </summary>
    private static async Task<int> ScheduledListAsync()
    {
        var pending = await ScheduledTaskLister.ListAsync();
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending Psst tasks.");
            Console.WriteLine();
            Console.WriteLine("Schedule one with:");
            Console.WriteLine("  psst sms <to> \"<message>\" --schedule 10:30am");
            Console.WriteLine("  psst sms <to> \"<message>\" --repeat 3 --every 5m   # implicit `--schedule now`");
            return 0;
        }

        Console.WriteLine($"Pending Psst tasks ({pending.Count}):");
        Console.WriteLine();
        foreach (var t in pending)
        {
            var fire = (t.NextRunTime ?? t.FireTimeFromSidecar)?.ToString("yyyy-MM-dd HH:mm")
                       ?? "<unknown>";
            Console.WriteLine($"  ⏰ {fire}   {t.TaskName}");
            if (t.Metadata is not null)
            {
                Console.WriteLine($"     → {t.Metadata.Recipient}: \"{Truncate(t.Metadata.Message, 60)}\"");
                if (t.Metadata.Repeat > 1)
                {
                    var iv = TimeSpan.FromSeconds(t.Metadata.IntervalSeconds);
                    Console.WriteLine($"     ↻ {t.Metadata.Repeat} sends every {DurationParser.Format(iv)}");
                }
            }
            else
            {
                Console.WriteLine($"     · launcher: {t.LauncherPath}");
            }
        }
        Console.WriteLine();
        Console.WriteLine("Cancel one:  psst scheduled cancel <task-name>");
        Console.WriteLine("Cancel all:  psst scheduled clear");
        return 0;
    }

    /// <summary>Cancel a single task by name.</summary>
    private static Task<int> ScheduledCancelAsync(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: psst scheduled cancel <task-name>");
            Console.Error.WriteLine("       (run `psst scheduled` to see task names)");
            return Task.FromResult(1);
        }
        var taskName = args[0];
        var error = ScheduledTaskLister.Cancel(taskName);
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
            return Task.FromResult(1);
        }
        Console.WriteLine($"cancelled '{taskName}'");
        return Task.FromResult(0);
    }

    /// <summary>
    /// Cancel every pending Psst task. Loud about which tasks were
    /// dropped — silent mass-cancel is a footgun.
    /// </summary>
    private static async Task<int> ScheduledClearAsync()
    {
        var pending = await ScheduledTaskLister.ListAsync();
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending Psst tasks to clear.");
            return 0;
        }
        var failures = 0;
        foreach (var t in pending)
        {
            var err = ScheduledTaskLister.Cancel(t.TaskName);
            if (err is null)
            {
                Console.WriteLine($"  ✓ cancelled {t.TaskName}");
            }
            else
            {
                Console.Error.WriteLine($"  ✗ {t.TaskName}: {err}");
                failures++;
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static int ScheduledUsage(string sub)
    {
        Console.Error.WriteLine($"unknown scheduled subcommand: {sub}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  psst scheduled [list]               List pending Psst tasks");
        Console.Error.WriteLine("  psst scheduled cancel <task-name>   Cancel one task");
        Console.Error.WriteLine("  psst scheduled clear                Cancel every pending Psst task");
        Console.Error.WriteLine();
        Console.Error.WriteLine("`pending` is an alias for `scheduled`.");
        return 1;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static async Task Notify(PsstConfiguration config, string message, bool silent)
    {
        // Auto-notifier (post-command wrap + `psst test`) has no per-send
        // flag and no contact context — it just honors PSST_VIA when set,
        // else the project default (email).
        var via = PsstViaResolver.Resolve(cliFlagValue: null, contactDefault: null);
        await using var notifier = new PsstNotifier(config, via);
        var result = await notifier.NotifyAsync(message, silent: silent);
        Console.WriteLine(message);
        if (result.SoundPlayed) Console.WriteLine("  ♪ played Psst");
        foreach (var a in result.SmsAttempts)
            Console.WriteLine($"  {(a.Success ? "✓" : "✗")} {a.Transport}: {a.Detail}");
        if (result.SmsAttempts.Count == 0)
            Console.WriteLine("  · no SMS transport configured — run `psst ping` for setup hints");
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"unknown command: {cmd}");
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("psst — MindAttic.Psst CLI");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  [--silent] -- <command> [args...]   Run a command. Play Psst + SMS when it exits.");
        Console.WriteLine("  [--silent] test [message]           Fire a notification right now.");
        Console.WriteLine("  ping                                Show which SMS transports are configured.");
        Console.WriteLine("  sound                               Just play the Psst sound.");
        Console.WriteLine("  contacts [list|add|rm]              Manage the contact book.");
        Console.WriteLine("  sms [flags] <name-or-phone> <message...>");
        Console.WriteLine("                                      Send a one-off SMS (default email-fanout; --via twilio for A2P).");
        Console.WriteLine("  scheduled / pending [list|cancel|clear]");
        Console.WriteLine("                                      Inspect / cancel pending scheduled sends.");
        Console.WriteLine();
        Console.WriteLine("Global flags:");
        Console.WriteLine("  --silent   Skip the audio cue. Place before the subcommand.");
        Console.WriteLine();
        Console.WriteLine("`sms` flags (may appear anywhere among the sms args):");
        Console.WriteLine("  --via twilio|email          Pick the transport for this send.");
        Console.WriteLine("                              Precedence: --via > $PSST_VIA > contact default > email.");
        Console.WriteLine("  --repeat N                  Send the message N times total. Default 1.");
        Console.WriteLine("  --interval D / --every D    Delay between repeats. D = 30s | 5m | 2h | 1d.");
        Console.WriteLine("                              Required whenever --repeat > 1.");
        Console.WriteLine("  --schedule T / --start T    Defer first send to local time T via Task Scheduler.");
        Console.WriteLine("                              T = 10:30am | 2:30pm | 10:30 | 22:30 (next occurrence).");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  psst -- dotnet test");
        Console.WriteLine("  psst -- npm run build");
        Console.WriteLine("  psst --silent test \"deploy finished\"");
        Console.WriteLine("  psst sms jordan \"MFE.\"");
        Console.WriteLine("  psst sms jordan \"MFE.\" --via twilio");
        Console.WriteLine("  psst sms jordan \"ping\" --repeat 12 --every 5m");
        Console.WriteLine("  psst sms jordan \"good morning\" --schedule 10:30am");
        Console.WriteLine("  psst sms jordan \"standup\" --start 9:00am --repeat 5 --every 1m");
        Console.WriteLine("  psst contacts add jordan +15551234567 --via twilio   # stick this contact to Twilio");
        Console.WriteLine();
        Console.WriteLine("Credentials come from the shared MindAttic.Vault chain (User Secrets / env vars).");
        Console.WriteLine("Section: MindAttic:Vault:Notifications.");
    }

    /// <summary>
    /// Resolve a wrapped command name to a launchable full path using the same
    /// PATH × PATHEXT search the shell would. Returns the original string
    /// unchanged when the input already carries a path/extension or when no
    /// match is found (so <see cref="Process.Start(ProcessStartInfo)"/> still
    /// surfaces a sensible "not found" error).
    /// </summary>
    internal static string ResolveExecutable(string command)
    {
        if (string.IsNullOrEmpty(command)) return command;

        // An explicit path (rooted or containing a separator) is handed through
        // verbatim — CreateProcess resolves it, and .NET runs a .cmd/.bat when
        // given a full path with extension.
        if (Path.IsPathRooted(command)
            || command.Contains(Path.DirectorySeparatorChar)
            || command.Contains(Path.AltDirectorySeparatorChar))
            return command;

        var pathExts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hasExtension = Path.HasExtension(command);
        foreach (var dir in dirs)
        {
            // Honor an explicit extension the caller already typed ("foo.exe").
            if (hasExtension)
            {
                var exact = Path.Combine(dir, command);
                if (File.Exists(exact)) return exact;
            }
            // Otherwise try each PATHEXT in order (".EXE" beats ".CMD", matching
            // the shell), so a real exe is preferred over a same-named shim.
            foreach (var ext in pathExts)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return command;
    }

    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    // Invariant culture so the elapsed time reads "1.5s" everywhere — without
    // it, machines whose culture uses a comma decimal separator emit "1,5s"
    // into the notification (the rest of the CLI already formats machine-facing
    // values invariantly, e.g. the schtasks /ST time).
    internal static string FormatElapsed(TimeSpan t) =>
        t.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}m{t.Seconds:00}s")
            : string.Create(CultureInfo.InvariantCulture, $"{t.TotalSeconds:0.0}s");
}
