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
psst -- <command> [args...]    Run a command. Play Psst + SMS when it exits.
psst test [message]            Fire a notification right now.
psst ping                      Show which SMS transports are configured.
psst sound                     Just play the Psst sound.
```

SMS is delivered via **Twilio** (preferred) with an **email-to-SMS gateway**
fallback. Credentials are resolved through the shared `MindAttic.Vault`
configuration chain (User Secrets / environment variables / optional
`appsettings.json`).

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
| `.env` fallback | `%APPDATA%\MindAttic\Psst\.env` | KEY=VALUE; outside the repo |
| `appsettings.json` | `.\appsettings.json` (CWD) | optional, legacy |
| **settings.json** | `%APPDATA%\MindAttic\Psst\settings.json` | **primary**, outside the repo |
| User Secrets | per-user store, id `mindattic-vault-shared` | dev convenience |
| Environment variables | `MindAttic__Vault__Notifications__*` | CI / containers override |

Pick whichever feels right. The two file-based locations under `%APPDATA%`
keep credentials out of the source tree without needing the `dotnet`
user-secrets tooling.

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

#### Option C — User Secrets

```powershell
dotnet user-secrets set "MindAttic:Vault:Notifications:twilio:accountSid" "AC..." `
  --id mindattic-vault-shared
dotnet user-secrets set "MindAttic:Vault:Notifications:twilio:authToken"  "..." `
  --id mindattic-vault-shared
dotnet user-secrets set "MindAttic:Vault:Notifications:twilio:from"       "+15555550100" `
  --id mindattic-vault-shared
dotnet user-secrets set "MindAttic:Vault:Notifications:to"                "+15555550101" `
  --id mindattic-vault-shared
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
