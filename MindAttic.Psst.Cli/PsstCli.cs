namespace MindAttic.Psst.Cli;

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using MindAttic.Psst;
using MindAttic.Psst.Configuration;
using MindAttic.Psst.Sound;
using MindAttic.Vault.Configuration;

/// <summary>
/// Top-level CLI for the <c>psst</c> tool. Wraps a command line, runs it to
/// completion, then plays the ICQ Psst sound and fires an SMS through the
/// first available transport (Twilio, then email-to-SMS).
///
/// <para>Usage:</para>
/// <code>
///   psst -- &lt;command&gt; [args...]    Run the command, notify on exit.
///   psst test                       Fire a test notification immediately.
///   psst ping                       Show what's configured + what would fire.
///   psst sound                      Play just the Psst sound (sanity check).
/// </code>
/// </summary>
public sealed class PsstCli
{
    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        // Compose User Secrets + env vars + optional appsettings.json. Psst reads
        // its own section directly out of this — no Vault credential-store wiring
        // is needed since we don't resolve LLM/broker keys here.
        var configuration = BuildConfiguration();
        var psstConfig = PsstConfiguration.Load(configuration);

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "--"     => await WrapAsync(args.Skip(1).ToArray(), psstConfig),
                "test"   => await TestAsync(args.Skip(1).ToArray(), psstConfig),
                "ping"   => Ping(psstConfig),
                "sound"  => Sound(),
                _        => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Build the config chain handed to Vault. User Secrets (shared id) +
    /// env vars + optional local appsettings.json from the working directory.
    /// </summary>
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(VaultConfigurationKeys.SharedUserSecretsId, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>Run the wrapped command, notify regardless of exit code, return the child's code.</summary>
    private static async Task<int> WrapAsync(string[] commandArgs, PsstConfiguration config)
    {
        if (commandArgs.Length == 0)
        {
            Console.Error.WriteLine("usage: psst -- <command> [args...]");
            return 1;
        }

        var fileName = commandArgs[0];
        var arguments = commandArgs.Length > 1
            ? string.Join(" ", commandArgs.Skip(1).Select(QuoteIfNeeded))
            : "";

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
        };

        var sw = Stopwatch.StartNew();
        int exitCode;
        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"failed to start: {fileName}");
            await proc.WaitForExitAsync();
            exitCode = proc.ExitCode;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var failMsg = $"psst: '{fileName}' failed to start — {ex.Message}";
            await Notify(config, failMsg);
            Console.Error.WriteLine(failMsg);
            return 2;
        }
        sw.Stop();

        var status = exitCode == 0 ? "OK" : $"FAIL (exit {exitCode})";
        var elapsed = FormatElapsed(sw.Elapsed);
        var message = $"psst: {fileName} — {status} in {elapsed}";

        await Notify(config, message);
        return exitCode;
    }

    /// <summary>Fire a notification right now with a fixed or user-supplied message.</summary>
    private static async Task<int> TestAsync(string[] args, PsstConfiguration config)
    {
        var message = args.Length > 0
            ? string.Join(' ', args)
            : "psst: test notification from MindAttic.Psst";
        await Notify(config, message);
        return 0;
    }

    /// <summary>Print what transports would fire without sending anything.</summary>
    private static int Ping(PsstConfiguration config)
    {
        Console.WriteLine("MindAttic.Psst — config check");
        Console.WriteLine($"  Twilio:        {(config.Twilio is null ? "not configured" : "configured (" + config.Twilio.From + " → " + (config.RecipientPhoneNumber ?? "<missing to>") + ")")}");
        Console.WriteLine($"  Email-to-SMS:  {(config.Email is null ? "not configured" : "configured (" + config.Email.From + " → " + (config.RecipientEmailSmsAddress ?? "<missing toEmail>") + ")")}");
        Console.WriteLine($"  Recipient #:   {config.RecipientPhoneNumber ?? "<unset>"}");
        Console.WriteLine($"  Recipient @:   {config.RecipientEmailSmsAddress ?? "<unset>"}");
        Console.WriteLine();
        if (!config.HasAnySmsTransport)
        {
            Console.WriteLine("No SMS transports configured. Set User Secrets, e.g.:");
            Console.WriteLine("  dotnet user-secrets set \"MindAttic:Vault:Notifications:twilio:accountSid\" \"AC...\"");
            Console.WriteLine("  dotnet user-secrets set \"MindAttic:Vault:Notifications:twilio:authToken\"  \"...\"");
            Console.WriteLine("  dotnet user-secrets set \"MindAttic:Vault:Notifications:twilio:from\"       \"+15555550100\"");
            Console.WriteLine("  dotnet user-secrets set \"MindAttic:Vault:Notifications:to\"               \"+15555550101\"");
        }
        return 0;
    }

    /// <summary>Play the Psst sound and exit. No SMS.</summary>
    private static int Sound()
    {
        var played = PsstSoundPlayer.Play(waitForCompletion: true);
        if (played)
        {
            Console.WriteLine($"played via {PsstSoundPlayer.LastTransport}.");
            return 0;
        }
        Console.Error.WriteLine($"could not play: {PsstSoundPlayer.LastError}");
        return 1;
    }

    private static async Task Notify(PsstConfiguration config, string message)
    {
        var notifier = new PsstNotifier(config);
        var result = await notifier.NotifyAsync(message);
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
        Console.WriteLine("  -- <command> [args...]   Run a command. Play Psst + SMS when it exits.");
        Console.WriteLine("  test [message]           Fire a notification right now.");
        Console.WriteLine("  ping                     Show which SMS transports are configured.");
        Console.WriteLine("  sound                    Just play the Psst sound.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  psst -- dotnet test");
        Console.WriteLine("  psst -- npm run build");
        Console.WriteLine();
        Console.WriteLine("Credentials come from the shared MindAttic.Vault chain (User Secrets / env vars).");
        Console.WriteLine("Section: MindAttic:Vault:Notifications.");
    }

    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    private static string QuoteIfNeeded(string s) =>
        s.Contains(' ') && !s.StartsWith('"') ? $"\"{s}\"" : s;

    private static string FormatElapsed(TimeSpan t) =>
        t.TotalMinutes >= 1
            ? $"{(int)t.TotalMinutes}m{t.Seconds:00}s"
            : $"{t.TotalSeconds:0.0}s";
}
