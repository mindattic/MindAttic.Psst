---
codex: 1
project: MindAttic.Psst
code: PST
layer: bible
status: living
updated: 2026-06-07
---

# MindAttic.Psst — Project Bible

> Single source of truth for what MindAttic.Psst IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/run; this says how to think about the system.

## 1. The one sentence {#PST-§1}

MindAttic.Psst is a Windows .NET notifier — a library plus a `psst` CLI front door — that
**plays a short attention-getter sound locally and sends an SMS** the moment a wrapped CLI
command finishes (or on demand), so you can walk away from a long-running build and still know
the instant it lands.

## 2. The product promise {#PST-§2}

- **Wrap anything, get pinged.** `psst -- <command> [args...]` runs the command to completion,
  captures its exit code and wall-clock duration, then fires sound + SMS — whether it passed or
  failed. See [§4.3](#PST-§4) `WrapAsync`.
- **Know without looking.** The message states *what* ran, the *OK/FAIL* status (with a readable
  label for well-known NTSTATUS crash codes), and *how long* it took.
- **SMS that actually arrives.** Email-to-SMS carrier fanout is the zero-setup default; Twilio
  A2P 10DLC is selectable per-send for direct carrier delivery. Exactly one transport per send —
  no implicit fallback chain at send time ([LAW-4](#PST-LAW-4)).
- **No daemon, no service.** A single CLI. Nothing listening on a port; deferred/repeat sends
  detach to Windows Task Scheduler rather than holding the shell open.
- **Credentials stay out of the repo.** Secrets resolve through the shared `MindAttic.Vault`
  chain ([HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3)).
- **Drip & defer.** `psst sms` supports `--repeat N`, `--interval/--every <dur>`, and
  `--schedule/--start <time>` with next-occurrence semantics.

## 3. What it is NOT {#PST-§3}

- **NOT a background daemon or system tray app.** It is a one-shot CLI process. There is no
  resident listener, no port, no service registration.
- **NOT cross-platform.** It targets `net10.0-windows`; sound playback is gated on Windows and
  scheduling shells out to `schtasks.exe`. `PsstSoundPlayer` returns `"not windows"` elsewhere.
- **NOT a general messaging/chat platform.** It sends short one-way notifications, not
  conversations. No inbound handling.
- **NOT a multi-transport fan-out at send time.** A single send picks exactly one transport
  ([LAW-4](#PST-LAW-4)); the only fan-out is across *carrier email gateways* for one recipient.
- **NOT a secret store.** It reads credentials from `MindAttic.Vault`; it never persists them and
  never commits them.
- **NOT semantically versioned.** Whole-number bumps only ([HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1)).

## 4. Architecture canon {#PST-§4}

```
                       ┌─────────────────────────────────────────────┐
   user shell  ──argv──▶│  MindAttic.Psst.Cli  (psst.exe front door)  │
                       │   PsstCli — parse argv, dispatch subcommands │
                       └───────┬───────────────────────┬─────────────┘
                               │                        │
                  wrap/test/sms│             scheduled/ │ contacts
                               │             pending    │
                               ▼                        ▼
        ┌──────────────────────────────┐   ┌────────────────────────────┐
        │  MindAttic.Psst  (library)   │   │  Scheduling (CLI-local)     │
        │                              │   │  ScheduledTaskRegistrar     │
        │  PsstNotifier ──┬── sound    │   │  ScheduledTaskLister        │
        │                 │   PsstSound│   │    └─▶ schtasks.exe          │
        │                 └── SMS      │   └────────────────────────────┘
        │     ISmsClient               │
        │      ├─ EmailSmsClient (MailKit, carrier fanout)
        │      └─ TwilioSmsClient (HTTP, A2P)
        │  PsstConfiguration ◀── MindAttic.Vault chain
        └──────────────────────────────┘
```

### 4.1 Projects {#PST-§4.1}

- **`MindAttic.Psst`** — the library/NuGet package (`net10.0-windows`). Owns the notifier,
  transports, sound, configuration, contacts, and duration/time parsing. File:
  `MindAttic.Psst/MindAttic.Psst.csproj`.
- **`MindAttic.Psst.Cli`** — the `psst.exe` console front door (`AssemblyName=psst`,
  `OutputType=Exe`). Owns argv parsing, subcommand dispatch, and Windows Task Scheduler
  integration. File: `MindAttic.Psst.Cli/MindAttic.Psst.Cli.csproj`.
- **`MindAttic.Psst.Tests`** — xUnit test project (114 tests). File:
  `MindAttic.Psst.Tests/MindAttic.Psst.Tests.csproj`.

### 4.2 Domain model — NOUNS {#PST-§4.2}

- **`PsstNotifier`** (`MindAttic.Psst/PsstNotifier.cs`) — orchestrates one notification: runs
  sound + SMS concurrently, dispatches via the chosen transport, returns a `NotifyResult`.
- **`NotifyResult`** — snapshot of one notify call (`Sound`, `SmsAttempts`).
- **`ISmsClient` / `SmsResult`** (`MindAttic.Psst/Sms/ISmsClient.cs`) — single-shot SMS
  dispatcher contract + its outcome record.
- **`EmailSmsClient`** (`MindAttic.Psst/Sms/EmailSmsClient.cs`) — MailKit SMTP transport that
  delivers through carrier email-to-SMS gateways.
- **`TwilioSmsClient`** (`MindAttic.Psst/Sms/TwilioSmsClient.cs`) — Twilio REST A2P transport.
- **`CarrierGateways`** (`MindAttic.Psst/Sms/CarrierGateways.cs`) — US carrier gateway catalog +
  phone normalization + recipient fan-out/combine helpers.
- **`PsstVia` / `PsstViaResolver`** (`MindAttic.Psst/PsstVia.cs`) — transport enum + precedence
  resolver (`--via` > `PSST_VIA` > contact default > project default Email).
- **`PsstFeatures`** (`MindAttic.Psst/PsstFeatures.cs`) — compile-time feature gate
  (`TwilioEnabled`).
- **`PsstConfiguration` / `TwilioSettings` / `EmailSettings`** (`MindAttic.Psst/Configuration/PsstConfiguration.cs`)
  — strongly-typed settings loaded from the Vault `IConfiguration` chain, with partial-config
  diagnostics.
- **`PsstConfigurationSources`** (`MindAttic.Psst/Configuration/PsstConfigurationSources.cs`) —
  resolves the on-disk `settings.json` path via Vault path math.
- **`Contact` / `ContactBook` / `ContactStore`** (`MindAttic.Psst/Contacts/`) — the contact book
  domain + persistence.
- **`PsstPlayResult`** (`MindAttic.Psst/Sound/PsstSoundPlayer.cs`) — outcome of a sound play.
- **`PendingTask`** (`MindAttic.Psst.Cli/Scheduling/ScheduledTaskLister.cs`) — one pending
  scheduled send.

### 4.3 Key services — VERBS {#PST-§4.3}

- **`PsstNotifier.NotifyAsync`** — play sound (unless silent) + dispatch SMS concurrently.
- **`PsstCli.RunAsync` / `WrapAsync` / `TestAsync` / `Ping` / `SoundAsync` / `Contacts` / `SmsAsync` / `ScheduledAsync`**
  (`MindAttic.Psst.Cli/PsstCli.cs`) — the subcommand handlers.
- **`PsstCli.ResolveExecutable`** — PATH × PATHEXT resolution so bare `npm`/`yarn` `.cmd` shims launch.
- **`PsstCli.ParseSmsFlags`** — single-pass flag parser for `--repeat/--interval/--every/--schedule/--start/--via`.
- **`PsstViaResolver.Resolve`** — apply transport precedence for one send.
- **`PsstConfiguration.Load`** — build typed settings from `IConfiguration`.
- **`PsstSoundPlayer.PlayAsync`** — play the embedded clip (MP3 via NAudio, WAV via SoundPlayer fallback).
- **`CarrierGateways.BuildFanout` / `Combine` / `NormalizeTo10Digits`** — recipient list math.
- **`DurationParser.TryParse/Format`** (`MindAttic.Psst/Time/DurationParser.cs`) — `30s/5m/2h/1d` durations.
- **`TimeOfDayParser.TryParse`** (`MindAttic.Psst/Time/TimeOfDayParser.cs`) — next-occurrence wall-clock times.
- **`ScheduledTaskRegistrar.RegisterAsync`** (`MindAttic.Psst.Cli/Scheduling/ScheduledTaskRegistrar.cs`) — register a deferred send with `schtasks`.
- **`ScheduledTaskLister.ListAsync/Cancel`** (`MindAttic.Psst.Cli/Scheduling/ScheduledTaskLister.cs`) — enumerate/cancel pending sends.

## 5. The Laws {#PST-§5}

> This project **inherits the org-wide House Rules** at
> [`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference — they are not restated
> here. Directly relevant inherited laws:
> [HOUSE-LAW-1 whole-number versioning](../../MindAttic.HouseRules.md#HOUSE-LAW-1),
> [HOUSE-LAW-3 credentials via MindAttic.Vault](../../MindAttic.HouseRules.md#HOUSE-LAW-3),
> [HOUSE-LAW-6 one engine, many front doors](../../MindAttic.HouseRules.md#HOUSE-LAW-6),
> [HOUSE-LAW-8 done is verified, not asserted](../../MindAttic.HouseRules.md#HOUSE-LAW-8),
> [HOUSE-LAW-9 `psst` only on explicit request](../../MindAttic.HouseRules.md#HOUSE-LAW-9).
>
> The laws below are **project-specific** to MindAttic.Psst.

### PST-LAW-1 — Notify on every exit, pass or fail {#PST-LAW-1}
A wrapped command fires the notification regardless of its exit code, and even when the child
fails to start. `psst` returns the child's exit code so it stays transparent in a pipeline.
(`WrapAsync`.)

### PST-LAW-2 — Sound is best-effort and non-blocking {#PST-LAW-2}
Audio failure never fails a notification. Sound and SMS run concurrently
(`Task.WhenAll`); the MP3 path falls back to WAV, and a non-Windows host degrades to a
silent no-op rather than throwing. (`PsstNotifier.NotifyAsync`, `PsstSoundPlayer.PlayAsync`.)

### PST-LAW-3 — Over-send, never miss {#PST-LAW-3}
For an unknown carrier, the email transport fans out to *every* known US carrier gateway for the
recipient's number. Duplicate buzzes are an acceptable price for guaranteed delivery; wrong-carrier
gateways silently drop. (`CarrierGateways`.)

### PST-LAW-4 — One transport per send, no send-time fallback {#PST-LAW-4}
Each send resolves to exactly one transport via the `PsstViaResolver` precedence chain
(`--via` > `PSST_VIA` > contact default > project default Email). The notifier never silently
tries a second transport after the chosen one is configured. (`PsstNotifier.BuildClients`,
`PsstViaResolver.Resolve`.)

### PST-LAW-5 — Don't recursively re-schedule {#PST-LAW-5}
A deferred/scheduled invocation must run the send path, never re-register itself. The launcher
sets `PSST_FROM_SCHEDULE=1`, `--schedule`/`--start` is stripped from the deferred argv, and the
implicit-`--schedule now` branch is suppressed when running as a scheduler child.
(`PsstCli.ParseSmsFlags`, `IsFromScheduler`.)

### PST-LAW-6 — Self-cleaning scheduled tasks {#PST-LAW-6}
A successful scheduled send leaves nothing pending: the launcher `.cmd` deletes its own
Task Scheduler entry and JSON sidecar after firing (rather than relying on `schtasks /Z`).
(`ScheduledTaskRegistrar`, README "How `--schedule` is implemented".)

### PST-LAW-7 — Machine-facing values are culture-invariant {#PST-LAW-7}
Values that cross a process or OS boundary (elapsed time in the SMS body, the `schtasks /ST`
time/date) are formatted with `CultureInfo.InvariantCulture`, so a comma-decimal locale can't
emit "1,5s" or a non-en-US date schtasks rejects. (`PsstCli.FormatElapsed`.)

## 6. Verified state {#PST-§6}

Evidence captured 2026-06-07 on this working tree.

- ✅ **Build**: `dotnet build MindAttic.Psst.slnx -c Release` — clean (all three projects compile).
- ✅ **Tests**: `dotnet test MindAttic.Psst.slnx -c Release` — **114 passed, 0 failed, 0 skipped**
  (Duration ~0.4s).
- ✅ **Notification pipeline** (`PsstNotifier`) — verified by `PsstNotifierTests` (10 tests:
  silent suppression, concurrent sound+SMS, transport selection, cancellation propagation).
- ✅ **Twilio transport** (`TwilioSmsClient`) — verified by `TwilioSmsClientTests` (9 tests:
  URL, basic-auth, form fields, success/failure mapping, error truncation).
- ✅ **Configuration loading** (`PsstConfiguration`) — verified by `PsstConfigurationTests` and
  `PsstConfigurationSourcesTests` (partial-config diagnostics, Vault path resolution).
- ✅ **Duration & time parsing** — verified by `DurationParserTests` and `TimeOfDayParserTests`
  (unit forms, next-occurrence semantics, round-trips, malformed rejection).
- ✅ **Scheduler argv quoting** (`ScheduledTaskRegistrar`) — verified by
  `ScheduledTaskRegistrarTests` (percent-doubling, cmd metacharacter quoting).
- ✅ **CLI helpers** (`FormatElapsed`, `ResolveExecutable`) — verified by `PsstCliTests`.
- 🟡 **End-to-end live SMS delivery** (real Twilio / real carrier gateway) — not covered by an
  automated test (requires live credentials + a real phone). Exercised manually via `psst test`.
- 🟡 **Live `schtasks` registration / sound playback on hardware** — unit-tested at the
  string/quoting level; the actual OS calls are validated manually.

## 7. Active frontier {#PST-§7}

- See [`docs/USER_STORIES.md`](USER_STORIES.md) for the epic/story breakdown and backlog.
- See [`docs/rfc/`](rfc/) for open design notes. Current: [RFC 0001](rfc/0001-example.md).
- No structured L5 canon-as-data is warranted (see this bible's §4 — the only catalog,
  `CarrierGateways.UnitedStatesDomains`, is small and lives in code by design per
  [PST-LAW-3](#PST-LAW-3)). Revisit if international carriers or per-region routing get added.

## 8. Quality bar {#PST-§8}

A feature is **done** (`✅`) only when:
1. It builds clean under `dotnet build -c Release` (no new warnings introduced).
2. It is covered by a green xUnit test named in [§6](#PST-§6) or in the owning story.
3. User-facing behavior is reflected in `README.md` and (if it changes a rule) in [§5](#PST-§5).
4. Anything that crosses a process/OS/locale boundary is culture-invariant ([PST-LAW-7](#PST-LAW-7)).
5. No secret is hard-coded ([HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3)) and versioning
   stays whole-number ([HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1)).

Anything not meeting all five is `🟡 partial` or `⬜ planned` — never `✅`.

## 9. Glossary {#PST-§9}

- **Wrap** — `psst -- <command>`: run a command to completion and notify on exit.
- **Transport (via)** — the channel a send uses: `email` (carrier email-to-SMS) or `twilio` (A2P 10DLC).
- **Fanout** — sending one email-to-SMS message to every known US carrier gateway for a number,
  since the carrier is unknown ([PST-LAW-3](#PST-LAW-3)).
- **A2P 10DLC** — Application-to-Person messaging over US 10-digit long codes; Twilio's registered
  path. Unregistered traffic is throttled/dropped (carrier error 30034).
- **Carrier gateway** — a per-carrier email domain (e.g. `vtext.com`) that delivers email as SMS.
- **Sidecar** — the JSON metadata file written next to a scheduled launcher `.cmd` so
  `psst scheduled` can render a meaningful listing.
- **Launcher** — the self-deleting `.cmd` Task Scheduler runs to perform a deferred send
  ([PST-LAW-6](#PST-LAW-6)).
- **Vault chain** — the `MindAttic.Vault` `IConfiguration` source order (vault files →
  appsettings → `%APPDATA%` settings.json → env vars) credentials resolve through.
- **PSST_FROM_SCHEDULE** — env marker set by the launcher so a deferred send doesn't recursively
  re-schedule itself ([PST-LAW-5](#PST-LAW-5)).
