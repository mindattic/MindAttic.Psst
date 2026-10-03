# MindAttic.Psst

Stop babysitting your terminal. Psst taps you on the shoulder the moment a long-running command finishes — a sound at your desk, a text on your phone.

![C#](https://img.shields.io/badge/C%23-.NET%2010-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows-0078D6) ![Type](https://img.shields.io/badge/type-CLI-informational) ![Status](https://img.shields.io/badge/status-active-brightgreen)

```text
PS> psst -- dotnet test
  ... your command's own output ...
psst: C:\Program Files\dotnet\dotnet.exe — OK in 12m04s
  ♪ played Psst
  ✓ Email-to-SMS: sent to 8/8 (5555550101@tmomail.net, 5555550101@txt.att.net, ...)
```

The output shape above comes straight from the CLI's format strings (`PsstCli.WrapAsync` and `Notify`); the same line arrives on your phone as a text. Psst is a command-line tool, so there is no screenshot.

## Why

- Kick off a 12-minute build, switch to your inbox, and get pinged when it is actually done instead of flicking back to the terminal every thirty seconds.
- Know without looking: the text tells you what ran, whether it passed, and how long it took. Stay in the meeting; glance at your phone.
- Wrap anything that runs in a shell: builds, tests, deploys, migrations, long `curl` calls, training runs.
- Nothing to babysit in return: one CLI, no daemon, no tray icon, nothing listening on a port.
- Texts arrive without carrier registration: your number is fanned out to every known US carrier email-to-SMS gateway.
- Credentials stay out of the repo, resolved through the shared MindAttic.Vault chain (`%APPDATA%` files or environment variables).

## Features

- Wrap a command with `psst -- <command>`: Psst runs it to completion, then notifies whether it passed, failed, or never started. It returns the child's own exit code, so it stays transparent in scripts.
- Readable failures: well-known Windows NTSTATUS exit codes become labels such as `Ctrl-C`, `access violation` or `stack overflow` instead of raw negative integers.
- Ctrl-C still notifies: Psst suppresses the default terminate-immediately handler just long enough to report once the cancelled child exits.
- Sound and text in parallel: the local clip and the SMS dispatch run concurrently, so neither delays the other. Sound failure never fails the notification.
- One-off texts with `psst sms`, addressed by contact name or a bare US number. Unknown names and malformed numbers are hard errors; Psst never guesses.
- Repeat and schedule: drip a message N times at an interval, defer it to a wall-clock time through Windows Task Scheduler, or both. Scheduled tasks clean up after themselves.
- A small local contact book (`psst contacts`) with crash-safe writes.
- `psst ping` diagnoses configuration without sending anything; `psst sound` checks audio on its own.

## Quick start

Prerequisites: Windows, the .NET 10 SDK, and an SMTP account (for Gmail, an app password).

```powershell
git clone https://github.com/mindattic/MindAttic.Psst.git
cd MindAttic.Psst
dotnet build MindAttic.Psst.slnx --configuration Release
```

Create `%APPDATA%\MindAttic\Psst\settings.json` (see [Setting up SMS credentials](#setting-up-sms-credentials) for the other options):

```json
{
  "MindAttic": {
    "Vault": {
      "Notifications": {
        "email": {
          "smtpHost": "smtp.gmail.com",
          "smtpPort": 587,
          "username": "you@gmail.com",
          "password": "app-password",
          "from":     "you@gmail.com"
        },
        "to": "+15555550101"
      }
    }
  }
}
```

Then check and fire one, using `psst.exe` from `MindAttic.Psst.Cli\bin\Release\net10.0-windows\` (or put that folder on your `PATH`):

```powershell
psst ping                 # lists each config source and whether it was found
psst test                 # sends a real SMS and plays the clip
psst -- dotnet test       # the real thing
```

## Opt-in only

Psst is invoked only when a human explicitly asks to be notified, never auto-wrapped around a command by a script, an agent, or another tool. This is the org-wide rule HOUSE-LAW-9 in the MindAttic house rules, and it exists because Psst is easy to over-apply: it plays an audible sound at the operator's desk and sends a real SMS to a real phone. If you are an AI coding agent working in any MindAttic repo, do not prepend `psst --` to shell commands unless the user's own message asked for a Psst notification ("run this with psst", "notify me when this finishes via psst"). Every other invocation (`psst test`, `psst sms`, `psst ping` and so on) is likewise something a human runs deliberately.

## What it is and is not

See [BIBLE sections 1 to 3](docs/BIBLE.md#PST-§1) for the full canon. In short:

- It is a one-shot Windows CLI (`psst.exe`) plus the library behind it (`MindAttic.Psst`, `net10.0-windows`) that plays a local sound and/or sends an SMS, on demand or when a wrapped command exits.
- It is not a background daemon, a system-tray app, a cross-platform tool, a general messaging platform, a multi-transport fan-out engine (it fans out across carrier gateways for one number, not across transports), or a secret store.

## Commands

```text
psst -- <command> [args...]                Run a command. Play Psst + SMS when it exits.
psst test [message]                        Fire a notification right now.
psst ping                                  Show which SMS transports are configured.
psst sound                                 Just play the Psst sound.
psst contacts [list|add|rm]                Manage the contact book.
psst sms [flags] <to> <message...>         Send a one-off SMS (see "Repeat and schedule").
psst scheduled [list|cancel|clear]         Inspect / cancel pending scheduled sends.
psst pending                               Alias for `psst scheduled`.
```

Running `psst` with no arguments, or `psst -h` / `--help` / `help` / `/?`, prints the built-in usage summary (`PsstCli.PrintUsage`).

### Global flags

| Flag | Where it goes | Meaning |
|---|---|---|
| `--silent` | Anywhere before the `--` argv divider (or before a subcommand) | Skip the audio cue for this invocation. The SMS still sends. Applies to the `--` wrap form and to `psst test`. |

`--silent` is stripped out of argv before subcommand dispatch, so `psst test --silent "msg"` and `psst --silent test "msg"` behave the same, and a wrapped command that itself wants a literal `--silent` argument (after the `--` divider) still receives it untouched.

### Wrapping a command

`psst -- <command> [args...]` runs `<command>` to completion (resolved through `PATH`/`PATHEXT`, so bare `npm`/`yarn`/`tsc` `.cmd` shims launch correctly), captures its exit code and wall-clock elapsed time, then fires the notification pipeline regardless of whether the command passed or failed, and even if the child process failed to start at all. `psst` returns the child's own exit code (or `2` if the child could not be started). The message reads `psst: <command> — OK in <elapsed>` or `psst: <command> — FAIL (<reason>) in <elapsed>`.

Ctrl-C is handled specially: Psst suppresses the default terminate-immediately behaviour just long enough to still notify once the (already-cancelled) child exits.

### psst test

Fires a notification immediately, with no wrapped command. Uses the supplied `message`, or a default (`psst: test notification from MindAttic.Psst`) when none is given. Honours `--silent`.

### psst ping

A print-only diagnostic that sends nothing. It shows:

- whether email-to-SMS is configured, and the `from` address if so
- the configured recipient phone number and explicit recipient email
- the effective fanout recipient list (one address per carrier gateway) and how many gateways that resolves to
- every configuration source path, marked found or not
- any partial-configuration diagnostics (for example "email is configured but neither 'toEmail' nor 'to' is set")
- setup hints (a ready-to-paste `providers.json` template) when no transport is configured at all

### psst sound

Plays the embedded Psst clip and exits. No SMS, no wrapped command. Useful as a sanity check that audio playback works on this machine.

### psst contacts

Manage a small local address book so `psst sms` can target a name instead of a raw phone number.

| Command | Effect |
|---|---|
| `psst contacts` or `psst contacts list` (alias `ls`) | List every contact, name-padded, with an optional `[via …]` suffix when a per-contact transport default is set. |
| `psst contacts add <name> <phone>` | Add a contact. A case-insensitive name collision auto-suffixes to the next free `<name>N` (for example `ryan` becomes `ryan2`) and prints a warning rather than failing. |
| `psst contacts rm <name>` (aliases `remove`, `del`) | Remove a contact by name (case-insensitive). Errors if no such contact exists. |

Contacts persist to `%APPDATA%\MindAttic\Psst\contacts.json`:

```json
{
  "contacts": [
    { "name": "Jordan", "phone": "+15555550123" },
    { "name": "Alice",  "phone": "+15551234567", "defaultVia": "email" }
  ]
}
```

`defaultVia` is optional; today the only valid value is `"email"` (the sole transport, see [Transport selection](#transport-selection)).

### psst sms

`psst sms [flags] <to> <message...>` sends a one-off (or repeated or scheduled) SMS, independent of wrapping any command. No audio cue is played for `sms` sends.

`<to>` resolves in this order:

1. A case-insensitive contact-book name match.
2. A bare US phone number (10 digits, optionally `+1`-prefixed, with the usual punctuation stripped). Letters anywhere in the string (for example an extension like `"1 ext 5551234567"`) reject the input outright rather than risk a misparsed number.

Anything matching neither is a hard error: Psst refuses to guess and send to a typo. See [Repeat and schedule](#repeat-and-schedule) for the `--repeat`, `--interval` and `--schedule` flags.

### psst scheduled and psst pending

Inspect and cancel deferred `sms` sends registered via `--schedule`/`--start` (directly, or implicitly via `--interval`). `pending` is a plain alias for `scheduled`.

| Command | Effect |
|---|---|
| `psst scheduled` or `psst scheduled list` (alias `ls`) | List every pending Psst Task Scheduler entry: next fire time, task name, recipient, message preview, and repeat/interval if any. |
| `psst scheduled cancel <task-name>` (aliases `rm`, `delete`, `del`) | Delete one task plus its launcher `.cmd` and its JSON sidecar. |
| `psst scheduled clear` | Cancel every pending Psst task in one pass, reporting per-task success or failure. |

Already-fired tasks never appear in the listing; they self-delete on completion (see [PST-LAW-6](docs/BIBLE.md#PST-LAW-6)).

## Repeat and schedule

The `sms` subcommand accepts three optional flags that let you drip a message at a cadence, defer it to a specific time, or both. Flags may appear anywhere in the `sms` argument list.

| Flag | Alias | Argument | Meaning |
|---|---|---|---|
| `--repeat` | none | positive integer | Send the message N times total. Default `1`. |
| `--interval` | `--every` | duration | Delay between repeats. Required whenever `--repeat` is greater than 1. |
| `--schedule` | `--start` | time of day | Defer the first send to local wall-clock time T (next occurrence) via Windows Task Scheduler. |

### Duration format

`--interval` / `--every` take a non-negative integer followed by a unit suffix. The suffix is case-insensitive; a bare integer is treated as seconds.

| Form | Meaning | Examples |
|---|---|---|
| `Ns` | seconds | `30s`, `90s` |
| `Nm` | minutes | `5m`, `30m` |
| `Nh` | hours | `2h`, `12h` |
| `Nd` | days | `1d`, `7d` |
| `N` | seconds (default) | `1800` |

Decimals (`1.5h`) and negatives are rejected.

### Time format

`--schedule` / `--start` take a wall-clock time in the local timezone. It always resolves to the next future occurrence: if the time has already passed today, the schedule rolls forward to tomorrow.

| Form | Meaning | Examples |
|---|---|---|
| 12-hour with marker | hour:minute, am/pm | `10:30am`, `2:30pm`, `10:30 AM` |
| 24-hour | hour:minute, no marker | `10:30`, `22:30`, `23:59` |
| Whole-hour shortcut | hour plus am/pm only | `10am`, `2pm` |

### Examples

```powershell
# Single send (no flags: runs in-process and returns immediately).
psst sms jordan "MFE."

# Twelve sends, five minutes apart. Detaches to Task Scheduler so the
# shell isn't tied up for the whole hour.
psst sms jordan "MFE." --repeat 12 --every 5m

# Single send deferred to 10:30am local (today, or tomorrow if past 10:30).
psst sms jordan "good morning" --schedule 10:30am

# Defer to 9:00am, then ping five times one minute apart.
psst sms jordan "standup" --start 9:00am --repeat 5 --every 1m
```

### Implicit schedule now

Whenever you pass `--interval` (or `--every`) without an explicit `--schedule` / `--start`, Psst infers `--schedule now`. "Now" rounds up to the next whole-minute boundary, because `schtasks /ST` only supports minute precision; a small cushion is added when you are within 5 seconds of the boundary so the registration does not race the trigger.

The practical effect: a long drip loop hands itself off to Windows Task Scheduler instead of blocking your shell. You get your prompt back immediately, and the loop runs in a detached `psst.exe` child process spawned by Task Scheduler.

```powershell
# These two are equivalent.
psst sms jordan "ping" --repeat 12 --every 5m
psst sms jordan "ping" --repeat 12 --every 5m --schedule now   # (illustrative)
```

## How scheduling works

Under the hood, `--schedule` (and its alias `--start`):

1. Resolves the time to a concrete local `DateTime` using next-occurrence semantics.
2. Writes a small launcher `.cmd` file to `%LOCALAPPDATA%\MindAttic\Psst\scheduled\<id>.cmd` that invokes `psst.exe sms …` with the original argv minus `--schedule` (so the deferred run does not re-schedule itself), keeps `--repeat`/`--interval`, and sets `PSST_FROM_SCHEDULE=1` so the deferred fire runs the send path. It then runs `schtasks /Delete /TN <task-name> /F` and removes the JSON sidecar, so successful runs leave nothing pending behind.
3. Writes a JSON sidecar `%LOCALAPPDATA%\MindAttic\Psst\scheduled\<id>.json` with the recipient, message, repeat and interval values, used by `psst scheduled` to render a meaningful listing.
4. Calls `schtasks.exe /Create /SC ONCE /TN MindAttic.Psst.<id> /TR <launcher> /SD <date> /ST <time> /F`.

`schtasks /Z` (auto-delete after run) is intentionally not used: it requires an `EndBoundary` that Windows does not synthesize from a bare `/SC ONCE`. The launcher self-deletes instead, with the same effect and no edge cases.

### Inspecting and cancelling scheduled sends

Listing reads the actual Task Scheduler state (via `schtasks /Query /FO CSV /V`) and enriches each row with the JSON sidecar, so the table shows recipient, message preview, and repeat/interval at a glance.

```powershell
PS> psst scheduled
Pending Psst tasks (2):

  ⏰ 2026-05-22 12:53   MindAttic.Psst.82894a30e2ad
     → jordan (5555550123): "deploy finished"
  ⏰ 2026-05-23 09:00   MindAttic.Psst.f73f70ff1e97
     → jordan (5555550123): "standup reminder"
     ↻ 5 sends every 1m

Cancel one:  psst scheduled cancel <task-name>
Cancel all:  psst scheduled clear
```

If you prefer the raw Windows tools, both still work:

```powershell
Get-ScheduledTask -TaskName 'MindAttic.Psst.*' | Format-Table TaskName, State, `
    @{N='NextRun';E={(Get-ScheduledTaskInfo $_).NextRunTime}}

schtasks /Query /TN MindAttic.Psst.*    # tab-complete task name first
schtasks /Delete /TN <task-name> /F     # cancel one
```

## How it works

```text
 psst -- <command>          psst test / psst sms
        |                           |
  run child, time it                |
        |                           |
        +------------+--------------+
                     |
          PsstNotifier.NotifyAsync
                     |
         +-----------+------------+        (Task.WhenAll: concurrent)
         |                        |
   PsstSoundPlayer          ISmsClient (resolved transport)
   MP3 via NAudio           EmailSmsClient (MailKit SMTP)
   -> WAV fallback                |
                       one email per US carrier gateway
                       tmomail.net, txt.att.net, vtext.com, ...
```

### The notification pipeline

Every notification (from `-- <command>`, `test`, or an `sms` send) goes through `PsstNotifier.NotifyAsync`:

1. Sound playback and SMS dispatch run concurrently (`Task.WhenAll`): the audio cue never delays the text, and vice versa.
2. Sound is skipped when `--silent` was passed, and always for `sms`.
3. SMS is dispatched through exactly one resolved transport (see [PST-LAW-4](docs/BIBLE.md#PST-LAW-4)), with no silent fallback to a second transport once the resolved one is configured.
4. The caller gets back a `NotifyResult`: whether sound played, and the list of SMS attempts (transport name plus success/detail) so the CLI can print a pass/fail line per attempt.

Sound failure is always best-effort; it never fails the overall notification (`PST-LAW-2`).

### Sound playback

The embedded clip (`icq-uh-oh.mp3` / `icq-uh-oh.wav`, shipped inside the `MindAttic.Psst` assembly as embedded resources) plays via two transports, tried in order:

1. MP3 via NAudio (`Mp3FileReader` plus `WaveOutEvent`).
2. WAV via `System.Media.SoundPlayer`: a pure managed fallback with no codec dependency, used when the NAudio path fails (for example a missing ACM codec).

On a non-Windows host, `PsstSoundPlayer.PlayAsync` returns a failure (`"not windows"`) rather than throwing, matching the fact that this project targets `net10.0-windows` only.

### SMS transport and carrier fanout

Psst has exactly one SMS transport (`PsstVia.Email`; Twilio support was removed in [PST-A3](docs/AMENDMENTS.md#PST-A3-remove-twilio-email-only-sms-transport-2026-06-19)). It works by emailing the recipient's number, formatted as an address, at every known US carrier's email-to-SMS gateway domain:

| Carrier (and MVNOs) | Gateway domain |
|---|---|
| T-Mobile (incl. former Sprint) | `tmomail.net` |
| AT&T | `txt.att.net` |
| Verizon | `vtext.com` |
| US Cellular | `email.uscc.net` |
| Boost Mobile | `sms.myboostmobile.com` |
| Cricket Wireless | `sms.cricketwireless.net` |
| MetroPCS | `mymetropcs.com` |
| Google Fi | `msg.fi.google.com` |

For a 10-digit number `5551234567`, the fanout is `5551234567@tmomail.net, 5551234567@txt.att.net, …`, one message per gateway. The wrong-carrier gateways silently drop the mail; the recipient's real carrier delivers it. Duplicate buzzes across gateways are an accepted cost of guaranteed delivery without needing to know which carrier the number belongs to ([PST-LAW-3](docs/BIBLE.md#PST-LAW-3)). You can also pin one explicit address via `toEmail`; it is unioned with the auto-fanout list and deduplicated.

Delivery itself is a real SMTP send via MailKit, using the SMTP account you configure.

## Setting up SMS credentials

Psst reads from several sources, lowest to highest precedence:

| Source | Path | Notes |
|---|---|---|
| Vault file | `%APPDATA%\MindAttic\Notifications\providers.json` | canonical credential store |
| `appsettings.json` | `.\appsettings.json` (current directory) | optional, legacy |
| settings.json | `%APPDATA%\MindAttic\Psst\settings.json` | primary, outside the repo |
| Environment variables | `MindAttic__Vault__Notifications__*` | CI and container overrides |

All four sources populate one logical config section, `MindAttic:Vault:Notifications` (see [Configuration](#configuration)).

### Option A settings file

Create `%APPDATA%\MindAttic\Psst\settings.json` as shown in [Quick start](#quick-start). For Gmail you must use an app password (Google Account, Security, 2-Step Verification, App passwords), not your account password.

You can also pin a specific carrier gateway with `toEmail`:

```json
"toEmail": "5555550101@vtext.com"
```

If both `to` and `toEmail` are set, the fanout and the explicit address are combined (deduplicated).

### Option B Vault file

Create `%APPDATA%\MindAttic\Notifications\providers.json`:

```json
{
  "email": {
    "smtpHost": "smtp.example.com",
    "smtpPort": 587,
    "username": "user",
    "password": "***",
    "from":     "psst@example.com"
  },
  "to": "+15555550101"
}
```

### Option C environment variables

```powershell
$env:MindAttic__Vault__Notifications__email__smtpHost = "smtp.gmail.com"
$env:MindAttic__Vault__Notifications__email__smtpPort = "587"
$env:MindAttic__Vault__Notifications__email__username = "you@gmail.com"
$env:MindAttic__Vault__Notifications__email__password = "app-password"
$env:MindAttic__Vault__Notifications__email__from     = "you@gmail.com"
$env:MindAttic__Vault__Notifications__to              = "+15555550101"
```

### Verify it works

```powershell
psst ping     # lists each source path and whether it was found
psst test     # sends a real SMS, check your phone
```

## Configuration

All email and recipient settings live under one `IConfiguration` section, `MindAttic:Vault:Notifications` (`PsstConfiguration.Section`):

| Key | Type | Required | Meaning |
|---|---|---|---|
| `email:smtpHost` | string | yes (if using email) | SMTP server hostname. |
| `email:smtpPort` | int | no (default `587`) | Falls back to `587` for any unparseable or out-of-range (0 or below, above 65535) value, so a typo cannot surface as a confusing `ConnectAsync` error. |
| `email:username` | string | yes | SMTP auth username. |
| `email:password` | string | yes | SMTP auth password (a Gmail app password for Gmail accounts). |
| `email:from` | string | yes | The `From:` address on the outgoing SMTP message. |
| `to` | string | one of `to`/`toEmail` | Recipient's US phone number (any punctuated form), auto-fanned-out across every carrier gateway. |
| `toEmail` | string | one of `to`/`toEmail` | An explicit, pre-resolved carrier email-to-SMS address, unioned with the `to` fanout. |

`PsstConfiguration.Load` returns `Email: null` if any of `smtpHost`, `username`, `password` or `from` is missing (all four missing means silently unconfigured; one to three missing appends an entry to `Errors`, surfaced by `psst ping`). It also flags "email is configured but neither `toEmail` nor `to` is set" when you have wired SMTP credentials but forgot a recipient.

### Transport selection

`PsstVia` is currently a single-member enum (`Email`). The abstraction (`ISmsClient`, `PsstVia`, `PsstViaResolver`, `PsstNotifier.BuildClients`) is kept general so a future transport slots in without a rewrite (see [PST-A3](docs/AMENDMENTS.md#PST-A3-remove-twilio-email-only-sms-transport-2026-06-19)), but today every code path resolves to `email`. Precedence, highest to lowest:

1. The `PSST_VIA` environment variable (only `email`, case-insensitive, is recognised; anything else falls through).
2. The target contact's `defaultVia` (set in `contacts.json`).
3. The project default, `email`.

### Contact book storage

- File: `%APPDATA%\MindAttic\Psst\contacts.json`, next to `settings.json` but intentionally outside the settings chain so it stays plain, user-editable JSON.
- Lookup (`ContactBook.Find`) tries a case-insensitive name match first, then falls back to matching normalised phone digits.
- Writes (`ContactStore.Save`) go through a temp-file-then-`File.Replace` swap, so a crash mid-write cannot truncate the file.

## Building and testing

```powershell
# Restore + build (Release, matches CI)
dotnet restore MindAttic.Psst.slnx
dotnet build MindAttic.Psst.slnx --configuration Release --no-restore

# Run the xUnit test suite
dotnet test MindAttic.Psst.Tests/MindAttic.Psst.Tests.csproj --configuration Release --no-build
```

GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs exactly this restore, build, test sequence on `windows-latest` with .NET 10.0.x for every push and pull request against `main`, and uploads the `.trx` test results as a build artifact.

All projects target `net10.0-windows`:

| Project | Output | Role |
|---|---|---|
| `MindAttic.Psst` | `MindAttic.Psst` library / NuGet package, version 1.0.0 | Notifier, transports, sound, configuration, contacts, duration and time parsing. Depends on NAudio, MailKit and MindAttic.Vault. |
| `MindAttic.Psst.Cli` | `psst.exe` (`AssemblyName=psst`) | Argument parsing, subcommand dispatch, Windows Task Scheduler integration. |
| `MindAttic.Psst.Tests` | test assembly | xUnit test suite; see [BIBLE section 6](docs/BIBLE.md#PST-§6) for the last verified count. |

Versioning is whole-number only (`<Version>N.0.0</Version>`, bumped by major version alone, per HOUSE-LAW-1). There is no separate publish or pack script beyond the standard `dotnet build` / `dotnet pack` flow.

After editing anything under `docs/`, regenerate and lint the Codex canon:

```powershell
powershell -File tools/codex.ps1 digest   # regenerate docs/BIBLE.digest.md
powershell -File tools/codex.ps1 doctor   # lint front-matter, links, cited tests/paths, digest freshness
```

## Project layout

```text
MindAttic.Psst/
├── MindAttic.Psst/                  # library (net10.0-windows)
│   ├── Configuration/                 PsstConfiguration, EmailSettings, PsstConfigurationSources
│   ├── Contacts/                      Contact, ContactBook, ContactStore
│   ├── Sms/                           ISmsClient, EmailSmsClient (MailKit), CarrierGateways
│   ├── Sound/                         PsstSoundPlayer + embedded icq-uh-oh.mp3 / .wav
│   ├── Time/                          DurationParser, TimeOfDayParser
│   ├── PsstNotifier.cs                orchestrates sound + SMS
│   └── PsstVia.cs                     transport enum + PsstViaResolver
├── MindAttic.Psst.Cli/              # psst.exe front door (net10.0-windows)
│   ├── PsstCli.cs                     argv parsing, subcommand handlers
│   ├── Program.cs                     entry point
│   └── Scheduling/                    ScheduledTaskRegistrar, ScheduledTaskLister (schtasks.exe)
├── MindAttic.Psst.Tests/            # xUnit test project
├── docs/
│   ├── BIBLE.md                       L0: architecture, Laws, verified state
│   ├── AMENDMENTS.md                  L1: append-only change log
│   ├── USER_STORIES.md                L2: test-cited stories + backlog
│   ├── BIBLE.digest.md                GENERATED, never hand-edit
│   └── rfc/                           design notes
├── tools/
│   ├── codex.ps1                      docs doctor/digest CLI
│   └── build-readme.ps1               thin wrapper to the shared codex-standard engine
├── .github/workflows/ci.yml           restore, build, test on windows-latest
├── index.htm, privacy.htm, terms.htm  static SMS-program pages
├── README.md                          this file
└── MindAttic.Psst.slnx
```

## Compliance pages

Because Psst sends SMS, the repo ships the plain static pages carriers and SMS-registration processes expect, at the repo root. They are not generated from Markdown; do not overwrite them when regenerating `README.htm`.

- [index.htm](index.htm): a static page describing the tool.
- [privacy.htm](privacy.htm): Privacy Policy. Psst is a single-user tool: the account owner is the sole configurator and sole recipient of any SMS it sends; the one phone number it stores lives only in the local `%APPDATA%\MindAttic\Psst\settings.json` file (`MindAttic:Vault:Notifications:to`), is used solely to deliver the owner's own CLI-completion notifications, and is never sold or shared with third parties.
- [terms.htm](terms.htm): SMS Terms and Conditions. Covers opt-in (editing the same local settings file), the self-issued confirmation message, typical message frequency (0 to 20 a day, driven by the owner's own CLI activity), standard `STOP`/`HELP` keyword handling, and that message and data rates may apply per the owner's own carrier plan.

## Glossary

- Wrap: `psst -- <command>`, run a command to completion and notify on exit.
- Transport (via): the channel a send uses. Currently only `email` (carrier email-to-SMS); `PsstVia` and `PsstViaResolver` are shaped for future alternatives.
- Fanout: sending one email-to-SMS message to every known US carrier gateway for a number, since the carrier is unknown.
- Carrier gateway: a per-carrier email domain (for example `vtext.com`) that delivers email as SMS.
- Sidecar: the JSON metadata file written next to a scheduled launcher `.cmd` so `psst scheduled` can render a meaningful listing.
- Launcher: the self-deleting `.cmd` Task Scheduler runs to perform a deferred send.
- Vault chain: the MindAttic.Vault `IConfiguration` source order (vault files, appsettings, `%APPDATA%` settings.json, environment variables) that credentials resolve through.
- `PSST_FROM_SCHEDULE`: environment marker set by the launcher so a deferred send does not re-schedule itself.
- `PSST_VIA`: environment variable that overrides transport selection for one send.

## Documentation

- [docs/BIBLE.md](docs/BIBLE.md): architecture, rationale, the project's Laws and verified build/test evidence.
- [docs/AMENDMENTS.md](docs/AMENDMENTS.md): append-only change history.
- [docs/USER_STORIES.md](docs/USER_STORIES.md): story-by-story backlog, and which behaviours are test-verified versus manual-only.
- [docs/BIBLE.digest.md](docs/BIBLE.digest.md): generated digest of the bible.
- [AGENTS.md](AGENTS.md): instructions for coding agents working in this repo. Agents: read the [Opt-in only](#opt-in-only) rule above before touching any command line.

## License

This repository has no license file; all rights reserved.

---

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) (credential and settings chain Psst reads from).
