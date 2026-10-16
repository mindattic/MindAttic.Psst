---
codex: 1
project: MindAttic.Psst
code: M.PSST
layer: stories
status: living
updated: 2026-10-03
---
<!-- GENERATED from MindAttic.Workflow/data/workflow.db by `workflow db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the workflow CLI. -->

# MindAttic.Psst — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites its verifying test.
> Test tokens are xUnit method names in `MindAttic.Psst.Tests`. See
> [BIBLE §6](BIBLE.md#M.PSST-§6) for the aggregate build/test evidence.

## Epic A — Wrap & notify

- **M.PSST-US-A1 ✅** As a developer, I can run `psst -- <command>` and be notified when it exits,
  so I can walk away from a long build. *Given a wrapped command, When it exits, Then Psst fires
  sound + SMS with the command, OK/FAIL status, and elapsed time, and returns the child's exit
  code.* *(Notification orchestration verified by `NotifyAsync_NotSilent_InvokesSoundPlayerAndReportsResult`,
  `NotifyAsync_PassesMessageToTransport`; bare-name resolution by
  `ResolveExecutable_BareName_ResolvesToCmdShimViaPathExt`,
  `ResolveExecutable_PrefersExeOverCmdWhenBothPresent`; elapsed formatting by
  `FormatElapsed_SubMinute_UsesInvariantDecimalSeparator`,
  `FormatElapsed_OverAMinute_RendersMinutesAndSeconds`.)*
- **M.PSST-US-A2 ✅** As a developer, I can pass `--silent` to skip the audio cue, so I don't make
  noise in a quiet room. *Given `--silent`, When notifying, Then the sound player is never invoked
  but the SMS still sends.* *(verified by `NotifyAsync_Silent_DoesNotInvokeSoundPlayer`.)*
- **M.PSST-US-A3 🟡** As a developer, I get a readable failure label instead of a raw NTSTATUS code,
  so a Ctrl-C reads as "Ctrl-C" not "exit -1073741510". *(Behavior lives in `WrapAsync`/
  `DescribeExitCode`; no dedicated test for the mapping yet — see backlog M.PSST-US-A4.)*

## Epic B — Send & transports

- **M.PSST-US-B1 🟡** As a user, I can send a one-off SMS with `psst sms <to> <message>`, resolving
  the recipient from the contact book or a bare US number. *(Contact resolution verified by
  `ContactBook.Find` behavior; phone normalization by `CarrierGateways` usage — exercised through
  config/notifier tests. The `SmsAsync` argv→send happy path has no direct end-to-end test;
  flag parsing and transports are tested in isolation.)*
- **M.PSST-US-B2 🟡** As a user, my message reaches an unknown-carrier phone via email-to-SMS
  fanout across every known US gateway, so I don't have to know the carrier. *Given a 10-digit
  number, When sending via email, Then one message goes to each carrier gateway.* *(Fanout/normalize
  logic verified through `PsstConfigurationTests` recipient handling and `PsstNotifierTests`
  transport dispatch; `CarrierGateways` is pure and deterministic, but has no dedicated test file — see backlog
  M.PSST-US-B6.)*
- **M.PSST-US-B5 ✅** As a user, exactly one transport is attempted per send (no surprise fallback),
  matching the resolved `via`. *(verified by `NotifyAsync_FirstTransportSucceeds_DoesNotCallSecond`,
  `NotifyAsync_NoTransports_ReturnsEmptyAttempts`, and `NotifyResult` accounting in
  `AnySmsSent_FalseWhenEmpty`, `AnySmsSent_FalseWhenAllFailed`,
  `FirstSuccess_ReturnsFirstSuccessfulAttempt`.)*
  > Note: the notifier *internally* supports a client list and falls back across it
  > (`NotifyAsync_FirstTransportFails_FallsBackToSecond`,
  > `NotifyAsync_AllTransportsFail_ReportsAllAttempts`), but `BuildClients` supplies exactly one
  > client per resolved `via` ([M.PSST-LAW-4](BIBLE.md#M.PSST-LAW-4)).

## Epic C — Repeat & schedule

- **M.PSST-US-C1 ✅** As a user, I can repeat a message `N` times at an interval (`--repeat`,
  `--interval/--every`), with durations like `30s/5m/2h/1d`. *Given a duration string, Then it
  parses to the right span; bad forms are rejected.* *(verified by `TryParse_AcceptsSecondForms`,
  `TryParse_AcceptsMinuteForms`, `TryParse_AcceptsHourForms`, `TryParse_AcceptsDayForms`,
  `TryParse_RejectsMalformed`, `Parse_ThrowsOnGarbage`, `Format_RoundTripsAndPicksLargestExactUnit`,
  `Format_RoundTripsThroughParse`.)*
- **M.PSST-US-C2 ✅** As a user, I can defer the first send to a wall-clock time
  (`--schedule/--start`), always resolving to the next future occurrence. *Given 12h/24h/whole-hour
  forms, Then they parse; a past time rolls to tomorrow.* *(verified by `TryParse_TwelveHour`,
  `TryParse_TwentyFourHour`, `TryParse_WholeHourShortcut`, `TryParse_NextOccurrence_TodayWhenFuture`,
  `TryParse_NextOccurrence_TomorrowWhenPast`, `TryParse_NextOccurrence_TomorrowWhenSameMinute`,
  `TimeOfDayParser` `TryParse_RejectsMalformed`/`Parse_ThrowsOnGarbage`.)*
- **M.PSST-US-C3 ✅** As a user, a deferred/repeat send detaches to Windows Task Scheduler with a
  correctly-quoted launcher, so cmd metacharacters and `%` in my message don't break the task.
  *(verified by `BuildInvocationLine_DoublesLiteralPercent`,
  `BuildInvocationLine_DoublesEnvVarPercentsSoNothingLeaks`,
  `QuoteForWindowsCommandLine_QuotesCmdMetacharacters`,
  `QuoteForWindowsCommandLine_PassesPlainTokensVerbatim`,
  `BuildInvocationLine_QuotesExePathAndCombinesWithMetacharArgs`.)*
- **M.PSST-US-C4 🟡** As a user, I can `psst scheduled list/cancel/clear` to inspect and cancel
  pending sends. *Listing/cancel logic shells out to `schtasks` (`ScheduledTaskLister`); the
  quoting/registrar half is tested but the list/cancel OS round-trip is verified manually only.*
- **M.PSST-US-C5 🟡** As a user, a successful scheduled send self-cleans (launcher deletes its task +
  sidecar) so nothing lingers in Task Scheduler ([M.PSST-LAW-6](BIBLE.md#M.PSST-LAW-6)). *Verified
  manually; no automated test exercises the real `schtasks /Delete` round-trip.*

## Epic D — Configuration & contacts

- **M.PSST-US-D1 ✅** As a user, my notifier reads credentials from the `MindAttic.Vault` chain
  (vault files → appsettings → `%APPDATA%/MindAttic/Psst/settings.json` → env vars), never the
  repo. *(verified by `PsstConfigurationTests` — `Load_FullEmail_PopulatesEmailRecord`,
  `Load_EmptyConfiguration_ReturnsAllNullsAndNoErrors`, `HasAnySmsTransport_TrueWhenEmailConfigured`;
  path resolution by `GetSettingsPath_IsSettingsJsonUnderAppData`,
  `GetAppDataDirectory_LandsUnderRoamingMindAtticPsst`,
  `GetAppDataDirectory_RespectsVaultRoamingRootOverride`.)*
- **M.PSST-US-D2 ✅** As a user, partial/misconfigured credentials produce a clear diagnostic instead
  of a silent failure. *(verified by `Load_EmailMissingRequiredField_ReturnsNullEmail_AndAddsError`.)*
- **M.PSST-US-D3 🟡** As a user, I can manage a contact book (`psst contacts list/add/rm`) with
  case-insensitive name collision suffixing. *`ContactBook`
  add/remove/find semantics are exercised through the notifier/config tests, but there is no
  dedicated `ContactBookTests`/`ContactStoreTests` and the `Contacts` CLI handler is untested.*
- **M.PSST-US-D4 🟡** As a user, `psst ping` shows which transports are configured and what would
  fire, without sending. *Print-only diagnostics (`Ping`); relies on `PsstConfiguration` +
  `CarrierGateways`; the `Ping` output itself is not asserted by a test.*

## Epic E — Sound

- **M.PSST-US-E1 🟡** As a user, `psst sound` plays the embedded clip, preferring MP3 (NAudio) and
  falling back to WAV (SoundPlayer), degrading silently off-Windows. *Pipeline integration
  verified by `NotifyAsync_SoundPlayerFails_PropagatesFalseAndKeepsError`,
  `SoundPlayed_FalseWhenSoundFailed`. Actual audio output and the NAudio-vs-WAV fallback on
  hardware are verified manually; only the result-propagation seam is unit-tested.*

## Priority backlog

Dependency-ordered toward "every documented behavior has a direct test" (the headline quality goal):

1. ⬜ **M.PSST-US-B6** Add `CarrierGatewaysTests` — direct coverage of `NormalizeTo10Digits`,
   `BuildFanout`, `Combine` (promotes B2 to a clean ✅).
2. ⬜ **M.PSST-US-B7** Add `PsstViaResolverTests` — precedence chain `PSST_VIA` env > contact >
   default, with unrecognised values falling through.
3. ⬜ **M.PSST-US-D5** Add `ContactBookTests` + `ContactStoreTests` — add/remove/find, collision
   suffixing, round-trip persistence (promotes D3).
4. ⬜ **M.PSST-US-A4** Add a `DescribeExitCode` mapping test (promotes A3).
5. ⬜ **M.PSST-US-C6** Integration-test the `schtasks` list/cancel round-trip behind a Windows-only
   trait (promotes C4/C5).
6. ⬜ **M.PSST-US-D6** Assert `psst ping` output shape (promotes D4).

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| M.PSST-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| M.PSST-T2 | 2026-10-14 | chore | closed | NUnit tests for ContactBook (Contacts/Contact.cs): Find matches name case-insensitively before phone, falls back to 10-digit phone match across formats (+1, dashes, parens), returns null for blank or unmatched input; WithAdded rejects blank name/phone and case-insensitive duplicate names and leaves the original book unchanged; WithoutContact removes only the first case-insensitive match and returns null when absent - this is how psst sms resolves recipients, so a wrong match texts the wrong person | 865fde2 |
| M.PSST-T3 | 2026-10-14 | chore | open | NUnit tests for ContactStore (Contacts/ContactStore.cs): TryLoad returns Empty with no Error for a missing file, Empty plus an Error naming the path for malformed JSON, and reads camelCase/case-insensitive fields with defaultVia as lowercase email; Save creates the folder, round-trips contacts, omits null defaultVia, and replaces an existing file via the .tmp swap without leaving the .tmp behind - use the VaultRoamingRoot override (as PsstConfigurationSourcesTests does) to point at a temp dir so the user's real contacts.json is never touched | — |
| M.PSST-T4 | 2026-10-14 | chore | open | NUnit tests for CarrierGateways (Sms/CarrierGateways.cs): NormalizeTo10Digits accepts E.164, 1-prefixed 11-digit, 10-digit and punctuated/spaced forms, rejects null/blank, any letters (ext, x4), 9/12-digit and 11-digit not starting with 1; BuildFanout yields digits@domain for every UnitedStatesDomains entry in order or null for a bad number; Combine dedupes case-insensitively, trims, skips blanks, keeps explicit-first order and returns null when both sides are empty - this decides who actually receives every SMS | — |
| M.PSST-T5 | 2026-10-14 | chore | open | NUnit tests for PsstViaResolver (PsstVia.cs): Resolve precedence PSST_VIA env value over contact default over the Email project default, unparseable or whitespace values fall through rather than erroring, explicit envVarValue parameter used instead of the real environment; TryParse is case-insensitive and trims; Format returns lowercase email - guards the transport-selection chain the CLI and contacts.json defaultVia depend on | — |
| M.PSST-T6 | 2026-10-14 | chore | open | NUnit tests for PsstCli sms argument parsing (PsstCli.ParseSmsFlags, NextMinuteBoundary, NextAvailableContactName; make them internal for the test project): --repeat rejects missing/zero/negative/non-numeric, --interval/--every reject missing/zero/garbage durations, --repeat over 1 without an interval fails, flags work in any position, --schedule/--start is stripped from ArgsWithoutSchedule while everything else is kept in order, an interval without a schedule implies next-minute scheduling except when PSST_FROM_SCHEDULE=1, NextMinuteBoundary adds a minute cushion within 5s of the boundary, and contact-name collisions auto-suffix ryan2, ryan3 case-insensitively - a parsing slip here can flood a phone with messages or recurse scheduling forever | — |
| M.PSST-T7 | 2026-10-14 | chore | open | NUnit tests for ScheduledTaskLister parsing and safety (Scheduling/ScheduledTaskLister.cs; expose ParseCsvLine and TryReadSidecar as internal): ParseCsvLine handles quoted fields, doubled quotes, embedded commas and empty fields; TryReadSidecar returns null for missing or malformed JSON, defaults repeat to 1 and interval to 0 when absent, and parses fireTime ISO round-trip back to local time; Cancel refuses blank names and any task outside the MindAttic\Psst\MindAttic.Psst. prefix (with or without leading backslash) without invoking schtasks - Cancel must never delete tasks psst did not create | — |
| M.PSST-T8 | 2026-10-14 | chore | open | NUnit tests for ScheduledTaskRegistrar launcher and sidecar output (Scheduling/ScheduledTaskRegistrar.cs): RegisterAsync rejects empty exe path, missing exe and non-future fire time before writing anything; the generated .cmd contains chcp 65001, set PSST_FROM_SCHEDULE=1 before the psst invocation, preserves the exit code, and self-deletes the task and sidecar; the sidecar JSON carries id, taskName, ISO fireTime, recipient, message, repeat, intervalSeconds and is readable by ScheduledTaskLister - needs a seam to redirect ScheduledDirectory (currently hard-wired to LOCALAPPDATA) and to skip the real schtasks call; the PSST_FROM_SCHEDULE line is what stops scheduled drips from re-scheduling forever | — |
| M.PSST-T9 | 2026-10-14 | chore | open | NUnit tests for EmailSmsClient failure paths (Sms/EmailSmsClient.cs): a recipient string that is blank or only commas returns a failed SmsResult without connecting; an unreachable SMTP host (closed localhost port) returns a failed SmsResult with the error message rather than throwing, and the next SendAsync retries the connection; a pre-cancelled token throws OperationCanceledException instead of being reported as a failure; DisposeAsync is safe to call twice - optionally use a local fake SMTP server to cover partial success (sent to N/M with failed list) and the single-parse malformed From error - the notifier fallback logic relies on this client reporting failures instead of crashing | — |
| M.PSST-T10 | 2026-10-15 | chore | closed | Remove duplicate command copies (/deploy, /quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
| M.PSST-T11 | 2026-10-16 | chore | closed | install.ps1 adds this repo's 8 .prose/commands stubs for the shared global commands, so any LLM client finds them | mindattic-agent-standard/install.ps1 run 2026-10-16 |
