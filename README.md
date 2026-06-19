# MindAttic.Psst

> **Stop babysitting your terminal.** Psst taps you on the shoulder the moment
> a long-running command finishes — a sound at your desk, a text on your phone.

Wrap any command. Walk away. Get pinged when it's done.

```text
psst -- dotnet test
psst -- npm run build
psst -- terraform apply
```

When the command exits, Psst plays a short attention-getter clip locally and
sends you an SMS with the command name, exit status, and elapsed time — whether
it succeeded or failed.

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

## CLI at a glance

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

SMS is delivered via **email-to-SMS carrier fanout**. Credentials are resolved
through the shared `MindAttic.Vault` configuration chain (`%APPDATA%`
vault files / `settings.json` / environment variables).

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

### How `--schedule` is implemented

Under the hood, `--schedule` (and its alias `--start`):

1. Resolves the time to a concrete local `DateTime` using
   next-occurrence semantics.
2. Writes a small launcher `.cmd` file to
   `%LOCALAPPDATA%\MindAttic\Psst\scheduled\<id>.cmd` that:
   - invokes `psst.exe sms …` with the original argv minus
     `--schedule` (so the deferred run doesn't recursively re-schedule)
     and with `--repeat`/`--interval` preserved;
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

## Inspecting & cancelling scheduled sends

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

## Setting up SMS

Psst sends SMS via **email-to-SMS carrier fanout**. You need an SMTP account
(Gmail app password, Outlook, or any relay) and your recipient's phone number.
Psst fans the message out to every known US carrier gateway automatically —
no carrier registration required.

### Wire the credentials into Psst

Psst reads from several sources, lowest → highest precedence:

| Source | Path | Notes |
|---|---|---|
| Vault file | `%APPDATA%\MindAttic\Notifications\providers.json` | canonical credential store |
| `appsettings.json` | `.\appsettings.json` (CWD) | optional, legacy |
| **settings.json** | `%APPDATA%\MindAttic\Psst\settings.json` | **primary**, outside the repo |
| Environment variables | `MindAttic__Vault__Notifications__*` | CI / containers override |

#### Option A — `settings.json` (recommended)

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

#### Option B — Vault file (`providers.json`)

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

#### Option C — Environment variables

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
