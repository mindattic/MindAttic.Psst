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
- **SMS that actually arrives.** Twilio first for real carrier delivery, with
  an email-to-SMS gateway fallback for zero-cost setups.
- **Credentials stay yours.** Secrets live outside the repo via the shared
  `MindAttic.Vault` chain — User Secrets, `%APPDATA%`, or environment
  variables. Never a `.env` checked into source control.

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

SMS is delivered via **Twilio** (preferred) with an **email-to-SMS gateway**
fallback. Credentials are resolved through the shared `MindAttic.Vault`
configuration chain (User Secrets / environment variables / optional
`appsettings.json`).

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

## Setting up Twilio

Twilio is the primary SMS transport. You need three values: an Account SID,
an Auth Token, and a Twilio phone number to send from.

### 1. Create a Twilio account

1. Go to <https://www.twilio.com/try-twilio> and sign up.
2. Verify the email address and your personal mobile number — Twilio uses the
   mobile number both for 2FA on the console and as the only allowed
   destination for **trial accounts** (see "Trial accounts" below).
3. After verification you land on the Twilio **Console** dashboard.

### 2. Buy (or pick) a phone number

1. In the Console, open **Phone Numbers → Manage → Buy a number**.
2. Filter by country and tick **SMS** in the **Capabilities** column.
3. Choose a number and click **Buy** (trial accounts get one number for free
   using trial credits).
4. The number you bought — in E.164 format like `+15555550100` — is your
   `twilio:from`.

### 3. Copy the Account SID and Auth Token

1. From the Console home page, scroll to **Account Info**.
2. The **Account SID** starts with `AC…` — this is `twilio:accountSid`.
3. Click **Show** next to **Auth Token** and copy it — this is
   `twilio:authToken`. Treat it like a password; rotating it invalidates any
   client that uses the old value.

### 4. Trial accounts — verify destination numbers

Trial accounts can **only** send SMS to verified numbers.

1. Open **Phone Numbers → Manage → Verified Caller IDs**.
2. Add and verify each number that will receive Psst notifications.
3. Upgrade to a paid account when you need to send to arbitrary numbers.

### 5. Wire the credentials into Psst

Psst reads from several sources, lowest → highest precedence:

| Source | Path | Notes |
|---|---|---|
| Vault file | `%APPDATA%\MindAttic\Notifications\providers.json` | canonical credential store |
| `.env` fallback | `%APPDATA%\MindAttic\Psst\.env` | KEY=VALUE; outside the repo |
| `appsettings.json` | `.\appsettings.json` (CWD) | optional, legacy |
| **settings.json** | `%APPDATA%\MindAttic\Psst\settings.json` | **primary**, outside the repo |
| Environment variables | `MindAttic__Vault__Notifications__*` | CI / containers override |

Pick whichever feels right. The file-based locations under `%APPDATA%`
keep credentials out of the source tree.

#### Option A — `settings.json` (recommended)

Create `%APPDATA%\MindAttic\Psst\settings.json`:

```json
{
  "MindAttic": {
    "Vault": {
      "Notifications": {
        "twilio": {
          "accountSid": "AC...",
          "authToken":  "...",
          "from":       "+15555550100"
        },
        "to": "+15555550101"
      }
    }
  }
}
```

#### Option B — `.env` (fallback)

Create `%APPDATA%\MindAttic\Psst\.env` (keys use `__` between segments):

```env
MindAttic__Vault__Notifications__twilio__accountSid=AC...
MindAttic__Vault__Notifications__twilio__authToken=...
MindAttic__Vault__Notifications__twilio__from=+15555550100
MindAttic__Vault__Notifications__to=+15555550101
```

Anything set in `settings.json` overrides the same key in `.env`.

#### Option C — Vault file (`providers.json`)

Create `%APPDATA%\MindAttic\Notifications\providers.json`:

```json
{
  "twilio": {
    "accountSid": "AC...",
    "authToken":  "...",
    "from":       "+15555550100"
  },
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

#### Option D — Environment variables

```powershell
$env:MindAttic__Vault__Notifications__twilio__accountSid = "AC..."
$env:MindAttic__Vault__Notifications__twilio__authToken  = "..."
$env:MindAttic__Vault__Notifications__twilio__from       = "+15555550100"
$env:MindAttic__Vault__Notifications__to                 = "+15555550101"
```

### 6. Verify it works

```powershell
psst ping     # lists each source path and whether it was found
psst test     # sends a real SMS — check your phone
```

### Rotating or revoking a leaked Auth Token

If you commit your Auth Token by accident, treat it as compromised:

1. In the Twilio Console, open **Account → API keys & tokens**.
2. Under **Live Credentials**, click **Reset** next to **Auth Token** (or
   create a new **Standard API Key** and migrate to using SID/Secret pairs,
   which can be revoked individually).
3. Update the value everywhere it's wired up: User Secrets on each dev
   machine, CI secrets, server env vars.
4. Audit recent message logs (**Monitor → Logs → Messaging**) for unexpected
   sends before assuming nothing went out under your account.

The same flow applies if a laptop is lost or a contributor leaves.

### Notes on US A2P 10DLC

If your `from` number is a US 10-digit long code, US carriers require
registration under the **A2P 10DLC** program for non-trivial message volume.
Unregistered traffic is heavily throttled and may be blocked outright. For
personal notifier use this rarely matters, but if you start sending dozens of
messages a day, register the number in the Twilio Console under
**Messaging → Regulatory compliance**.

## Email-to-SMS fallback (optional)

Most US carriers expose a per-number email address that delivers as SMS.
Examples: `5555550100@vtext.com` (Verizon), `5555550100@txt.att.net` (AT&T),
`5555550100@tmomail.net` (T-Mobile). Deliverability varies; this is the
fallback when Twilio isn't configured.

Add an `email` block to `%APPDATA%\MindAttic\Psst\settings.json`:

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
        "toEmail": "5555550100@vtext.com"
      }
    }
  }
}
```

For Gmail you must use an **app password** (Google Account → Security →
2-Step Verification → App passwords), not your account password.
