# MindAttic.Psst

> **Stop babysitting your terminal.** Psst taps you on the shoulder the moment
> a long-running command finishes — a sound at your desk, a text on your phone.

```text
psst -- dotnet test
psst -- npm run build
psst -- terraform apply
```

When the command exits, Psst plays a short attention-getter clip locally and
sends you an SMS with the command name, exit status, and elapsed time — whether
it succeeded or failed.

Full architecture, rationale, and the project's Laws live in
**[docs/BIBLE.md](docs/BIBLE.md)** — this README is the how-to-build/run/use
companion. See also **[docs/AMENDMENTS.md](docs/AMENDMENTS.md)** (append-only
change log) and **[docs/USER_STORIES.md](docs/USER_STORIES.md)** (test-cited
story backlog). SMS-compliance pages: **[privacy.htm](privacy.htm)** ·
**[terms.htm](terms.htm)**.

## Table of contents

- [Why Psst](#why-psst)
- [Opt-in only — read this before wiring it into anything](#opt-in-only--read-this-before-wiring-it-into-anything)
- [What it is / what it is NOT](#what-it-is--what-it-is-not)
- [CLI reference](#cli-reference)
  - [Global flags](#global-flags)
  - [`psst -- <command>`](#psst----command-args)
  - [`psst test`](#psst-test-message)
  - [`psst ping`](#psst-ping)
  - [`psst sound`](#psst-sound)
  - [`psst contacts`](#psst-contacts)
  - [`psst sms`](#psst-sms-flags-to-message)
  - [`psst scheduled` / `psst pending`](#psst-scheduled--psst-pending)
- [Repeat & schedule](#repeat--schedule)
- [How `--schedule` is implemented](#how---schedule-is-implemented)
- [The notification pipeline](#the-notification-pipeline)
- [Sound playback](#sound-playback)
- [SMS transport: email-to-SMS carrier fanout](#sms-transport-email-to-sms-carrier-fanout)
- [Setting up SMS credentials](#setting-up-sms-credentials)
- [Configuration reference](#configuration-reference)
- [Transport selection (`PSST_VIA`)](#transport-selection-psst_via)
- [Contact book](#contact-book)
- [Build, test, publish](#build-test-publish)
- [Directory layout](#directory-layout)
- [Compliance pages](#compliance-pages)
- [Glossary](#glossary)

## Why Psst

- **Reclaim your focus.** Kick off a 12-minute build, switch to your inbox, get
  pinged when it's actually done. No more flicking back to the terminal every
  thirty seconds.
- **Know without looking.** The SMS tells you _what_ ran, whether it _passed_,
  and _how long_ it took. Stay in the meeting; glance at your phone.
- **Works with anything.** If it runs in a shell, Psst can wrap it — builds,
  tests, deploys, migrations, long `curl`s, ML training runs.
- **No daemon, no service.** Just a single CLI. Nothing in the background,
  nothing listening on a port, nothing to babysit.
- **SMS that actually arrives.** Email-to-SMS carrier fanout fans your number
  out to every known US carrier gateway — no registration required.
- **Credentials stay yours.** Secrets live outside the repo via the shared
  `MindAttic.Vault` chain — `%APPDATA%\MindAttic\Notifications\providers.json`,
  `%APPDATA%\MindAttic\Psst\settings.json`, or environment variables. Never
  checked into source control.

## Opt-in only — read this before wiring it into anything

**`psst` is invoked only when a human explicitly asks to be notified — never
auto-wrapped around a command by a script, an agent, or another tool.** This is
an org-wide rule (`HOUSE-LAW-9` in
[`../MindAttic.HouseRules.md`](../MindAttic.HouseRules.md#HOUSE-LAW-9)), and it
exists specifically because Psst is easy to over-apply: it plays an audible
sound at the operator's desk and sends a real SMS to a real phone, so silently
wrapping "helpful" commands in `psst -- …` is a footgun, not a convenience. If
you are an AI coding agent operating in this workspace (or any MindAttic repo):
do not prepend `psst --` to shell commands unless the user's own message asked
for a Psst notification ("run this with psst", "notify me when this finishes
via psst"). Every other invocation — `psst test`, `psst sms`, `psst ping`,
etc. — is likewise something a human runs deliberately, not something another
tool should trigger on its behalf.

## What it is / what it is NOT

See [BIBLE §1–§3](docs/BIBLE.md#PST-§1) for the full canon. In short:

- **IS**: a one-shot Windows CLI (`psst.exe`) plus the library behind it
  (`MindAttic.Psst`, `net10.0-windows`) that plays a local sound and/or sends an
  SMS, on demand or when a wrapped command exits.
- **IS NOT**: a background daemon, a system-tray app, a cross-platform tool, a
  general chat/messaging platform, a multi-transport fan-out engine (it fans
  out across *carrier gateways* for one number, not across transports), or a
  secret store.

## CLI reference

```text
psst -- <command> [args...]                Run a command. Play Psst + SMS when it exits.
psst test [message]                        Fire a notification right now.
psst ping                                  Show which SMS transports are configured.
psst sound                                 Just play the Psst sound.
psst contacts [list|add|rm]                Manage the contact book.
psst sms [flags] <to> <message...>         Send a one-off SMS (see "Repeat & schedule" below).
psst scheduled [list|cancel|clear]         Inspect / cancel pending scheduled sends.
psst pending                               Alias for `psst scheduled`.
```

Running `psst` with no arguments, or `psst -h` / `--help` / `help` / `/?`,
prints the built-in usage summary (`PsstCli.PrintUsage`).

### Global flags

| Flag | Where it goes | Meaning |
|---|---|---|
| `--silent` | Anywhere before the `--` argv divider (or before a subcommand) | Skip the audio cue for this invocation. The SMS still sends. Applies to the `--` wrap form and to `psst test`. |

`--silent` is stripped out of argv before subcommand dispatch, so
`psst test --silent "msg"` and `psst --silent test "msg"` behave the same —
and a wrapped command that itself wants a literal `--silent` argument (after
the `--` divider) still receives it untouched.

### `psst -- <command> [args...]`

Runs `<command>` to completion (resolved through `PATH`/`PATHEXT`, so bare
`npm`/`yarn`/`tsc` `.cmd` shims launch correctly), captures its exit code and
wall-clock elapsed time, then fires the notification pipeline — **regardless
of whether the command passed or failed**, and even if the child process
failed to start at all. `psst` returns the child's own exit code, so it stays
transparent in a script or CI pipeline. A well-known Windows NTSTATUS failure
code is translated to a readable label (e.g. `Ctrl-C`, `access violation`,
`stack overflow`) instead of a raw signed integer.

Ctrl-C is handled specially: Psst suppresses the default terminate-immediately
behavior just long enough to still notify once the (already-Ctrl-C'd) child
exits.

### `psst test [message]`

Fires a notification immediately, with no wrapped command. Uses the supplied
`message`, or a default (`psst: test notification from MindAttic.Psst`) when
none is given. Honors `--silent`.

### `psst ping`

Print-only diagnostic — sends nothing. Shows:

- whether email-to-SMS is configured, and the `from` address if so
- the configured recipient phone number / explicit recipient email
- the effective fanout recipient list (one address per carrier gateway) and
  how many gateways that resolves to
- every configuration source path, marked found (`✓`) or not (`·`)
- any partial-configuration diagnostics (e.g. "email is configured but
  neither 'toEmail' nor 'to' is set")
- setup hints (a ready-to-paste `providers.json` template) when no transport
  is configured at all

### `psst sound`

Plays the embedded Psst clip and exits. No SMS, no wrapped command. Useful as
a sanity check that audio playback works on this machine.

### `psst contacts`

Manage a small local address book so `psst sms` can target a name instead of a
raw phone number.

| Command | Effect |
|---|---|
| `psst contacts` or `psst contacts list` (alias `ls`) | List every contact, name-padded, with an optional `[via …]` suffix when a per-contact transport default is set. |
| `psst contacts add <name> <phone>` | Add a contact. A case-insensitive name collision auto-suffixes to the next free `<name>N` (e.g. `ryan` → `ryan2`) and prints a warning rather than failing. |
| `psst contacts rm <name>` (aliases `remove`, `del`) | Remove a contact by name (case-insensitive). Errors if no such contact exists. |

Contacts persist to `%APPDATA%\MindAttic\Psst\contacts.json`:

```json
{
  "contacts": [
    { "name": "Ryan",  "phone": "+19203764617" },
    { "name": "Alice", "phone": "+15551234567", "defaultVia": "email" }
  ]
}
```

`defaultVia` is optional; today the only valid value is `"email"` (the sole
transport — see [Transport selection](#transport-selection-psst_via)).

### `psst sms [flags] <to> <message...>`

Send a one-off (or repeated/scheduled) SMS, independent of wrapping any
command. No audio cue is played for `sms` sends.

`<to>` resolves in this order:
1. A case-insensitive contact-book name match.
2. A bare US phone number (10 digits, optionally `+1`-prefixed, with the usual
   punctuation stripped). Letters anywhere in the string (e.g. an extension
   like `"1 ext 5551234567"`) reject the input outright rather than risk a
   misparsed number.

Anything matching neither is a hard error — Psst refuses to guess and send to
a typo.

See [Repeat & schedule](#repeat--schedule) for the `--repeat` / `--interval` /
`--schedule` flags.

### `psst scheduled` / `psst pending`

Inspect and cancel deferred `sms` sends registered via `--schedule`/`--start`
(directly, or implicitly via `--interval` — see below). `pending` is a plain
alias for `scheduled`.

| Command | Effect |
|---|---|
| `psst scheduled` or `psst scheduled list` (alias `ls`) | List every pending Psst Task Scheduler entry: next fire time, task name, recipient, message preview, and repeat/interval if any. |
| `psst scheduled cancel <task-name>` (aliases `rm`, `delete`, `del`) | Delete one task + its launcher `.cmd` + its JSON sidecar. |
| `psst scheduled clear` | Cancel every pending Psst task in one pass, reporting per-task success/failure. |

Already-fired tasks never appear in the listing — they self-delete on
completion (see [`PST-LAW-6`](docs/BIBLE.md#PST-LAW-6)).

## Repeat & schedule

The `sms` subcommand accepts three optional flags that let you drip a
message at a cadence, defer it to a specific time, or both. Flags may
appear anywhere in the `sms` arg list — before the recipient, after the
message, mixed with each other.

| Flag | Alias | Argument | Meaning |
|---|---|---|---|
| `--repeat` | — | positive integer | Send the message _N_ times total. Default `1`. |
| `--interval` | `--every` | duration | Delay between repeats. Required whenever `--repeat > 1`. |
| `--schedule` | `--start` | time-of-day | Defer the first send to local wall-clock time _T_ (next occurrence) via Windows Task Scheduler. |

### Duration format (`--interval` / `--every`)

A non-negative integer followed by a unit suffix. Suffix is
case-insensitive; a bare integer is treated as seconds.

| Form | Meaning | Examples |
|---|---|---|
| `Ns` | seconds | `30s`, `90s` |
| `Nm` | minutes | `5m`, `30m` |
| `Nh` | hours   | `2h`, `12h` |
| `Nd` | days    | `1d`, `7d` |
| `N`  | seconds (default) | `1800` |

Decimals (`1.5h`) are rejected — keep it integer-valued. Negatives are
rejected.

### Time format (`--schedule` / `--start`)

A wall-clock time in the local timezone. Always resolves to the **next
future occurrence** — if the time has already passed today, the schedule
rolls forward to tomorrow.

| Form | Meaning | Examples |
|---|---|---|
| 12-hour with marker | hour:minute, am/pm | `10:30am`, `2:30pm`, `10:30 AM` |
| 24-hour | hour:minute, no marker | `10:30`, `22:30`, `23:59` |
| Whole-hour shortcut | hour + am/pm only | `10am`, `2pm` |

### Examples

```powershell
# Single send (no flags — runs in-process and returns immediately).
psst sms jordan "MFE."

# Twelve sends, five minutes apart. Detaches to Task Scheduler so the
# shell isn't tied up for the whole hour. See "Implicit --schedule now".
psst sms jordan "MFE." --repeat 12 --every 5m

# Single send deferred to 10:30am local (today, or tomorrow if past 10:30).
psst sms jordan "good morning" --schedule 10:30am

# Defer to 9:00am, then ping five times one minute apart.
psst sms jordan "standup" --start 9:00am --repeat 5 --every 1m
```

### Implicit `--schedule now`

Whenever you pass `--interval` (or its alias `--every`) **without** an
explicit `--schedule` / `--start`, Psst infers `--schedule now` for you.
"Now" rounds up to the next whole-minute boundary, because
`schtasks /ST` only supports minute precision; a small cushion is added
when you're within 5 seconds of the boundary so the registration doesn't
race the trigger.

The practical effect: a long drip loop hands itself off to Windows Task
Scheduler instead of blocking your shell. You get your prompt back
immediately, and the loop runs in a detached `psst.exe` child process
spawned by Task Scheduler.

```powershell
# These two are equivalent.
psst sms jordan "ping" --repeat 12 --every 5m
psst sms jordan "ping" --repeat 12 --every 5m --schedule now   # (illustrative)
```

## How `--schedule` is implemented

Under the hood, `--schedule` (and its alias `--start`):

1. Resolves the time to a concrete local `DateTime` using
   next-occurrence semantics.
2. Writes a small launcher `.cmd` file to
   `%LOCALAPPDATA%\MindAttic\Psst\scheduled\<id>.cmd` that:
   - invokes `psst.exe sms …` with the original argv minus
     `--schedule` (so the deferred run doesn't recursively re-schedule)
     and with `--repeat`/`--interval` preserved, and with
     `PSST_FROM_SCHEDULE=1` set so the deferred fire runs the send path
     instead of re-registering itself;
   - then runs `schtasks /Delete /TN <task-name> /F` and removes the
     JSON sidecar — so successful runs leave nothing pending behind.
3. Writes a JSON sidecar `%LOCALAPPDATA%\MindAttic\Psst\scheduled\<id>.json`
   with the recipient, message, repeat, and interval values, used by
   `psst scheduled` to render a meaningful listing.
4. Calls `schtasks.exe /Create /SC ONCE /TN MindAttic.Psst.<id> /TR
   <launcher> /SD <date> /ST <time> /F`.

> Note: `schtasks /Z` (auto-delete after run) is intentionally not used
> here — it requires an `EndBoundary` that Windows doesn't synthesize
> from a bare `/SC ONCE`. The launcher self-deletes instead, achieving
> the same effect with zero edge cases.

### Inspecting & cancelling scheduled sends

```text
psst scheduled            # list all pending Psst tasks (alias: psst pending)
psst scheduled list       # same as above
psst scheduled cancel <task-name>
                          # delete one task + its launcher + sidecar (alias: rm, delete)
psst scheduled clear      # cancel every pending Psst task in one go
```

Listing reads the actual Task Scheduler state (via `schtasks /Query
/FO CSV /V`) and enriches each row with the JSON sidecar, so the table
shows recipient, message preview, and repeat/interval at a glance.
Already-fired tasks don't appear — they self-deleted on completion.

Example:

```powershell
PS> psst scheduled
Pending Psst tasks (2):

  ⏰ 2026-05-22 12:53   MindAttic.Psst.82894a30e2ad
     → jordan (12088996244): "deploy finished"
  ⏰ 2026-05-23 09:00   MindAttic.Psst.f73f70ff1e97
     → jordan (12088996244): "standup reminder"
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

## The notification pipeline

Every notification (whether from `-- <command>`, `test`, or an `sms` send)
goes through `PsstNotifier.NotifyAsync`:

1. Sound playback and SMS dispatch run **concurrently** (`Task.WhenAll`) — the
   audio cue never delays the text, and vice versa.
2. Sound is skipped when `--silent` was passed (or, for `sms`, always — `sms`
   never plays the sound).
3. SMS is dispatched through exactly one resolved transport (see
   [`PST-LAW-4`](docs/BIBLE.md#PST-LAW-4)) — no silent fallback to a second
   transport once the resolved one is configured.
4. The caller gets back a `NotifyResult`: whether sound played, and the list
   of SMS attempts (transport name + success/detail) so the CLI can print a
   `✓`/`✗` line per attempt.

Sound failure is always best-effort — it never fails the overall
notification (`PST-LAW-2`).

## Sound playback

The embedded clip (`icq-uh-oh.mp3` / `icq-uh-oh.wav`, shipped inside the
`MindAttic.Psst` assembly as embedded resources) plays via two transports,
tried in order:

1. **MP3 via NAudio** (`Mp3FileReader` + `WaveOutEvent`, WASAPI output).
2. **WAV via `System.Media.SoundPlayer`** — pure managed fallback, no codec
   dependency, used when the NAudio path fails (e.g. a missing ACM codec).

On a non-Windows host, `PsstSoundPlayer.PlayAsync` returns a failure
(`"not windows"`) rather than throwing — sound degrades silently everywhere
except Windows, matching the fact that this project targets
`net10.0-windows` only.

## SMS transport: email-to-SMS carrier fanout

Psst has exactly **one** SMS transport (`PsstVia.Email` — Twilio support was
removed in [`PST-A3`](docs/AMENDMENTS.md#PST-A3-remove-twilio-email-only-sms-transport-2026-06-19)).
It works by emailing the recipient's number, formatted as an address, at
*every* known US carrier's email-to-SMS gateway domain:

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

For a 10-digit number `5551234567`, the fanout is
`5551234567@tmomail.net, 5551234567@txt.att.net, …` — one message per
gateway. The wrong-carrier gateways silently drop the mail; the recipient's
real carrier delivers it. Duplicate buzzes across gateways are an accepted
cost of guaranteed delivery without needing to know (or ask) which carrier
the number belongs to ([`PST-LAW-3`](docs/BIBLE.md#PST-LAW-3)). You can also
pin one explicit address via `toEmail` (see below) — it's unioned with the
auto-fanout list, deduplicated.

Delivery itself is a real SMTP send via MailKit, using the SMTP account you
configure below.

## Setting up SMS credentials

Psst reads from several sources, lowest → highest precedence:

| Source | Path | Notes |
|---|---|---|
| Vault file | `%APPDATA%\MindAttic\Notifications\providers.json` | canonical credential store |
| `appsettings.json` | `.\appsettings.json` (CWD) | optional, legacy |
| **settings.json** | `%APPDATA%\MindAttic\Psst\settings.json` | **primary**, outside the repo |
| Environment variables | `MindAttic__Vault__Notifications__*` | CI / containers override |

All four sources ultimately populate one logical config section,
`MindAttic:Vault:Notifications` (see [Configuration reference](#configuration-reference)).

### Option A — `settings.json` (recommended)

Create `%APPDATA%\MindAttic\Psst\settings.json`:

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

For Gmail you must use an **app password** (Google Account → Security →
2-Step Verification → App passwords), not your account password.

You can also pin a specific carrier gateway with `toEmail`:

```json
"toEmail": "5555550101@vtext.com"
```

If both `to` and `toEmail` are set, the fanout and the explicit address are
combined (deduplicated).

### Option B — Vault file (`providers.json`)

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

### Option C — Environment variables

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
psst test     # sends a real SMS — check your phone
```

## Configuration reference

All email/recipient settings live under one `IConfiguration` section,
`MindAttic:Vault:Notifications` (`PsstConfiguration.Section`):

| Key | Type | Required? | Meaning |
|---|---|---|---|
| `email:smtpHost` | string | yes (if using email) | SMTP server hostname. |
| `email:smtpPort` | int | no (default `587`) | Falls back to `587` for any unparseable/out-of-range (`≤0` or `>65535`) value, so a typo can't surface as a confusing `ConnectAsync` error. |
| `email:username` | string | yes | SMTP auth username. |
| `email:password` | string | yes | SMTP auth password (a Gmail **app password** for Gmail accounts). |
| `email:from` | string | yes | The `From:` address on the outgoing SMTP message. |
| `to` | string | one of `to`/`toEmail` | Recipient's US phone number (any punctuated form); auto-fanned-out across every carrier gateway. |
| `toEmail` | string | one of `to`/`toEmail` | An explicit, pre-resolved carrier email-to-SMS address, unioned with the `to` fanout. |

`PsstConfiguration.Load` returns `Email: null` if any of `smtpHost` /
`username` / `password` / `from` is missing (all four missing → silently
unconfigured; 1–3 missing → an entry appended to `Errors`, surfaced by
`psst ping`). It also flags "email is configured but neither `toEmail` nor
`to` is set" when you've wired SMTP creds but forgot a recipient.

## Transport selection (`PSST_VIA`)

`PsstVia` is currently a single-member enum (`Email`) — the abstraction
(`ISmsClient`, `PsstVia`, `PsstViaResolver`, `PsstNotifier.BuildClients`) is
kept general-shaped so a future transport slots in without a rewrite (see
[`PST-A3`](docs/AMENDMENTS.md#PST-A3-remove-twilio-email-only-sms-transport-2026-06-19)),
but today every code path resolves to `email`. Precedence, highest to
lowest:

1. `PSST_VIA` environment variable (only `email`, case-insensitive, is
   recognized; anything else falls through).
2. The target contact's `defaultVia` (set via `contacts.json`).
3. Project default — `email`.

## Contact book

See [`psst contacts`](#psst-contacts) above for the CLI surface. Storage
details:

- File: `%APPDATA%\MindAttic\Psst\contacts.json` (next to `settings.json`,
  intentionally outside the settings/User-Secrets chain so it's plain,
  user-editable JSON).
- Lookup (`ContactBook.Find`) tries a case-insensitive name match first, then
  falls back to matching normalized phone digits.
- Writes (`ContactStore.Save`) go through a temp-file-then-`File.Replace`
  swap, so a crash mid-write can't truncate the file.

## Build, test, publish

```powershell
# Restore + build (Release, matches CI)
dotnet restore MindAttic.Psst.slnx
dotnet build MindAttic.Psst.slnx --configuration Release --no-restore

# Run the xUnit test suite
dotnet test MindAttic.Psst.Tests/MindAttic.Psst.Tests.csproj --configuration Release --no-build
```

GitHub Actions (`.github/workflows/ci.yml`) runs exactly this restore →
build → test sequence on `windows-latest` with `.NET 10.0.x`, for every push
and pull request against `main`, and uploads the `.trx` test results as a
build artifact.

Both shipped projects target `net10.0-windows`:

| Project | Output | Role |
|---|---|---|
| `MindAttic.Psst` | `MindAttic.Psst` (library / NuGet package, `<Version>1.0.0</Version>`) | Notifier, transports, sound, configuration, contacts, duration/time parsing. |
| `MindAttic.Psst.Cli` | `psst.exe` (`AssemblyName=psst`) | argv parsing, subcommand dispatch, Windows Task Scheduler integration. |
| `MindAttic.Psst.Tests` | test assembly | xUnit test suite (100 tests as of the last verified BIBLE snapshot — see [BIBLE §6](docs/BIBLE.md#PST-§6)). |

Versioning is **whole-number only** — `<Version>N.0.0</Version>`, bumped by
major version alone, per `HOUSE-LAW-1`. There is currently no separate
publish/pack script in this repo beyond the standard `dotnet build`/`dotnet
pack` flow.

After editing anything under `docs/`, regenerate and lint the Codex canon:

```powershell
powershell -File tools/codex.ps1 digest   # regenerate docs/BIBLE.digest.md
powershell -File tools/codex.ps1 doctor   # lint front-matter, links, cited tests/paths, digest freshness
```

## Directory layout

```text
MindAttic.Psst/
├── MindAttic.Psst/                  # library (net10.0-windows)
│   ├── Configuration/                 PsstConfiguration, EmailSettings, PsstConfigurationSources
│   ├── Contacts/                      Contact, ContactBook, ContactStore
│   ├── Sms/                           ISmsClient, EmailSmsClient (MailKit), CarrierGateways
│   ├── Sound/                         PsstSoundPlayer + embedded icq-uh-oh.mp3 / .wav
│   ├── Time/                          DurationParser, TimeOfDayParser
│   ├── PsstNotifier.cs                 orchestrates sound + SMS
│   └── PsstVia.cs                      transport enum + PsstViaResolver
├── MindAttic.Psst.Cli/               # psst.exe front door (net10.0-windows)
│   ├── PsstCli.cs                      argv parsing, subcommand handlers
│   ├── Program.cs                      entry point
│   └── Scheduling/                     ScheduledTaskRegistrar, ScheduledTaskLister (schtasks.exe)
├── MindAttic.Psst.Tests/             # xUnit test project
├── docs/
│   ├── BIBLE.md                        L0 — architecture, Laws, verified state
│   ├── AMENDMENTS.md                   L1 — append-only change log
│   ├── USER_STORIES.md                 L2 — test-cited stories + backlog
│   ├── BIBLE.digest.md                 GENERATED — never hand-edit
│   └── rfc/                            design notes (open: RFC 0001)
├── tools/
│   ├── codex.ps1                       docs doctor/digest CLI
│   └── build-readme.ps1                thin wrapper → shared codex-standard engine
├── .github/workflows/ci.yml           restore → build → test on windows-latest
├── index.htm, privacy.htm, terms.htm  static SMS-program pages (see below)
├── README.md                          this file
└── MindAttic.Psst.slnx
```

## Compliance pages

Because Psst sends SMS, the repo ships the plain static pages carriers and
SMS-registration processes expect, at the repo root (not generated from
Markdown — do not overwrite them when regenerating `README.htm`):

- **[index.htm](index.htm)** — landing page for the tool.
- **[privacy.htm](privacy.htm)** — Privacy Policy. Summarizes that Psst is a
  **single-user** tool: the account owner is the sole configurator and sole
  recipient of any SMS it sends; the one phone number it stores lives only in
  the local `%APPDATA%\MindAttic\Psst\settings.json` file
  (`MindAttic:Vault:Notifications:to`), is used solely to deliver the owner's
  own CLI-completion notifications, and is never sold or shared with third
  parties.
- **[terms.htm](terms.htm)** — SMS Terms & Conditions. Covers opt-in (editing
  the same local settings file), the self-issued confirmation message,
  typical message frequency (0–20/day, driven by the owner's own CLI
  activity), standard `STOP`/`HELP` keyword handling, and that message/data
  rates may apply per the owner's own carrier plan.

## Glossary

- **Wrap** — `psst -- <command>`: run a command to completion and notify on
  exit.
- **Transport (via)** — the channel a send uses. Currently only `email`
  (carrier email-to-SMS); the `PsstVia` enum and `PsstViaResolver` are shaped
  for future alternative transports.
- **Fanout** — sending one email-to-SMS message to every known US carrier
  gateway for a number, since the carrier is unknown.
- **Carrier gateway** — a per-carrier email domain (e.g. `vtext.com`) that
  delivers email as SMS.
- **Sidecar** — the JSON metadata file written next to a scheduled launcher
  `.cmd` so `psst scheduled` can render a meaningful listing.
- **Launcher** — the self-deleting `.cmd` Task Scheduler runs to perform a
  deferred send.
- **Vault chain** — the `MindAttic.Vault` `IConfiguration` source order (vault
  files → appsettings → `%APPDATA%` settings.json → env vars) credentials
  resolve through.
- **`PSST_FROM_SCHEDULE`** — env marker set by the launcher so a deferred send
  doesn't recursively re-schedule itself.
- **`PSST_VIA`** — env var that overrides transport selection for one send
  (see [Transport selection](#transport-selection-psst_via)).

---

For architecture, the project's Laws, and verified build/test evidence, see
**[docs/BIBLE.md](docs/BIBLE.md)**. For the change history, see
**[docs/AMENDMENTS.md](docs/AMENDMENTS.md)**. For the story-by-story backlog
and which behaviors are test-verified vs. manual-only, see
**[docs/USER_STORIES.md](docs/USER_STORIES.md)**.
