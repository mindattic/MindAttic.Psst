namespace MindAttic.Psst.Cli;

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using MindAttic.Psst;
using MindAttic.Psst.Configuration;
using MindAttic.Psst.Contacts;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;
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
///                                  Run the command, notify on exit.
///   psst test [--silent] [message] Fire a test notification immediately.
///   psst ping                      Show what's configured + what would fire.
///   psst sound                     Play just the Psst sound (sanity check).
/// </code>
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

        // Pull off a leading --silent so we don't have to repeat parsing per command.
        var silent = false;
        if (args.Length > 0 && args[0] == "--silent")
        {
            silent = true;
            args = args.Skip(1).ToArray();
        }

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
                "--"        => await WrapAsync(args.Skip(1).ToArray(), psstConfig, silent),
                "test"      => await TestAsync(args.Skip(1).ToArray(), psstConfig, silent),
                "ping"      => Ping(psstConfig),
                "sound"     => await SoundAsync(),
                "contacts"  => Contacts(args.Skip(1).ToArray()),
                "sms"       => await SmsAsync(args.Skip(1).ToArray(), psstConfig),
                _           => UnknownCommand(args[0]),
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
    ///   <item><description>User Secrets (shared MindAttic ID) — per-dev convenience.</description></item>
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

        // 4. User Secrets (dev convenience).
        builder.AddUserSecrets(VaultConfigurationKeys.SharedUserSecretsId, reloadOnChange: false);

        // 5. Environment variables (highest priority).
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
            FileName = commandArgs[0],
            UseShellExecute = false,
        };
        // ArgumentList escapes per-arg; avoids the quoting bugs that come from
        // hand-building a command line out of space-joined strings.
        for (var i = 1; i < commandArgs.Length; i++)
            psi.ArgumentList.Add(commandArgs[i]);

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
            var failMsg = $"psst: '{psi.FileName}' failed to start — {ex.Message}";
            await Notify(config, failMsg, silent);
            Console.Error.WriteLine(failMsg);
            return 2;
        }
        sw.Stop();

        var status = exitCode == 0 ? "OK" : $"FAIL (exit {exitCode})";
        var elapsed = FormatElapsed(sw.Elapsed);
        var message = $"psst: {psi.FileName} — {status} in {elapsed}";

        await Notify(config, message, silent);
        return exitCode;
    }

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
        Console.WriteLine($"  · user secrets:  {VaultConfigurationKeys.SharedUserSecretsId}");
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
            Console.WriteLine($"  • Create {settingsPath}:");
            Console.WriteLine("      {");
            Console.WriteLine("        \"MindAttic\": { \"Vault\": { \"Notifications\": {");
            Console.WriteLine("          \"twilio\": {");
            Console.WriteLine("            \"accountSid\": \"AC...\", \"authToken\": \"...\", \"from\": \"+15555550100\"");
            Console.WriteLine("          },");
            Console.WriteLine("          \"to\": \"+15555550101\"");
            Console.WriteLine("        } } }");
            Console.WriteLine("      }");
            Console.WriteLine("  • Or set via shared User Secrets (works across every MindAttic app):");
            Console.WriteLine($"      dotnet user-secrets --id {VaultConfigurationKeys.SharedUserSecretsId} \\");
            Console.WriteLine("          set \"MindAttic:Vault:Notifications:twilio:accountSid\" \"AC...\"");
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
        var book = ContactStore.Load();
        var path = ContactStore.GetPath();
        Console.WriteLine($"Contact book ({path}):");
        if (book.Contacts.Count == 0)
        {
            Console.WriteLine("  (empty)");
            Console.WriteLine();
            Console.WriteLine("Add one with:  psst contacts add <name> <phone>");
            return 0;
        }
        var nameWidth = book.Contacts.Max(c => c.Name.Length);
        foreach (var c in book.Contacts)
            Console.WriteLine($"  {c.Name.PadRight(nameWidth)}  {c.Phone}");
        return 0;
    }

    private static int ContactsAdd(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: psst contacts add <name> <phone>");
            return 1;
        }
        var (requestedName, phone) = (args[0], args[1]);
        var book = ContactStore.Load();

        // Case-insensitive collision check: if "Ryan" exists and the user
        // adds "ryan", auto-suffix to the first available "<name>N" (N≥2)
        // and warn. Matches the convention the user wanted: ryan, ryan2,
        // ryan3, … — first instance unsuffixed.
        var finalName = NextAvailableContactName(book, requestedName);
        if (!finalName.Equals(requestedName, StringComparison.Ordinal))
            Console.Error.WriteLine($"warning: '{requestedName}' already exists (case-insensitive); saving as '{finalName}'");

        try
        {
            var added = book.WithAdded(new Contact(finalName, phone));
            ContactStore.Save(added);
            Console.WriteLine($"added '{finalName}' → {phone}");
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
        var book = ContactStore.Load();
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
        Console.Error.WriteLine("  psst contacts [list]              List all contacts");
        Console.Error.WriteLine("  psst contacts add <name> <phone>  Add a contact");
        Console.Error.WriteLine("  psst contacts rm <name>           Remove a contact");
        return 1;
    }

    /// <summary>
    /// Send a one-off SMS to a contact (by name) or an arbitrary US phone
    /// number. Uses the same email-to-SMS fanout as the notifier path, with
    /// the recipient overridden for this send only — no configured `to`
    /// fallback, no audio cue.
    /// </summary>
    private static async Task<int> SmsAsync(string[] args, PsstConfiguration config)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: psst sms <name-or-phone> <message...>");
            return 1;
        }
        var recipient = args[0];
        var message = string.Join(' ', args.Skip(1));

        var book = ContactStore.Load();
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

        if (config.Email is null)
        {
            Console.Error.WriteLine("email transport is not configured — can't send.");
            Console.Error.WriteLine("Run `psst ping` to see what's wired up.");
            return 1;
        }

        // Replace just the recipient phone for this send; null out any
        // explicit `toEmail` so the fanout is purely the override number's.
        var overridden = config with
        {
            RecipientPhoneNumber = phone,
            RecipientEmailSmsAddress = null,
        };

        var notifier = new PsstNotifier(overridden);
        var result = await notifier.NotifyAsync(message, silent: true);
        Console.WriteLine($"→ {label}: {message}");
        foreach (var a in result.SmsAttempts)
            Console.WriteLine($"  {(a.Success ? "✓" : "✗")} {a.Transport}: {a.Detail}");
        return result.AnySmsSent ? 0 : 1;
    }

    private static async Task Notify(PsstConfiguration config, string message, bool silent)
    {
        var notifier = new PsstNotifier(config);
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
        Console.WriteLine("  [--silent] -- <command> [args...]  Run a command. Play Psst + SMS when it exits.");
        Console.WriteLine("  [--silent] test [message]          Fire a notification right now.");
        Console.WriteLine("  ping                               Show which SMS transports are configured.");
        Console.WriteLine("  sound                              Just play the Psst sound.");
        Console.WriteLine("  contacts [list|add|rm]             Manage the contact book.");
        Console.WriteLine("  sms <name-or-phone> <message...>   Send a one-off SMS via the email-fanout chain.");
        Console.WriteLine();
        Console.WriteLine("Flags:");
        Console.WriteLine("  --silent   Skip the audio cue. Place before the subcommand.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  psst -- dotnet test");
        Console.WriteLine("  psst -- npm run build");
        Console.WriteLine("  psst --silent test \"deploy finished\"");
        Console.WriteLine();
        Console.WriteLine("Credentials come from the shared MindAttic.Vault chain (User Secrets / env vars).");
        Console.WriteLine("Section: MindAttic:Vault:Notifications.");
    }

    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    private static string FormatElapsed(TimeSpan t) =>
        t.TotalMinutes >= 1
            ? $"{(int)t.TotalMinutes}m{t.Seconds:00}s"
            : $"{t.TotalSeconds:0.0}s";
}
