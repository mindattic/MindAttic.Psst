---
codex: 1
project: MindAttic.Psst
code: PST
layer: stories
status: living
updated: 2026-06-07
---

# MindAttic.Psst — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned · 🗑️ cut. Every ✅ cites its verifying test.
> Test tokens are xUnit method names in `MindAttic.Psst.Tests`. See
> [BIBLE §6](BIBLE.md#PST-§6) for the aggregate build/test evidence.

## Epic A — Wrap & notify

- **PST-US-A1 ✅** As a developer, I can run `psst -- <command>` and be notified when it exits,
  so I can walk away from a long build. *Given a wrapped command, When it exits, Then Psst fires
  sound + SMS with the command, OK/FAIL status, and elapsed time, and returns the child's exit
  code.* *(Notification orchestration verified by `NotifyAsync_NotSilent_InvokesSoundPlayerAndReportsResult`,
  `NotifyAsync_PassesMessageToTransport`; bare-name resolution by
  `ResolveExecutable_BareName_ResolvesToCmdShimViaPathExt`,
  `ResolveExecutable_PrefersExeOverCmdWhenBothPresent`; elapsed formatting by
  `FormatElapsed_SubMinute_UsesInvariantDecimalSeparator`,
  `FormatElapsed_OverAMinute_RendersMinutesAndSeconds`.)*
- **PST-US-A2 ✅** As a developer, I can pass `--silent` to skip the audio cue, so I don't make
  noise in a quiet room. *Given `--silent`, When notifying, Then the sound player is never invoked
  but the SMS still sends.* *(verified by `NotifyAsync_Silent_DoesNotInvokeSoundPlayer`.)*
- **PST-US-A3 ✅** As a developer, I get a readable failure label instead of a raw NTSTATUS code,
  so a Ctrl-C reads as "Ctrl-C" not "exit -1073741510". *(Behavior lives in `WrapAsync`/
  `DescribeExitCode`; covered indirectly — see backlog item to add a direct test.)* 🟡
  *(downgraded: no dedicated test for `DescribeExitCode` mapping.)*

## Epic B — Send & transports

- **PST-US-B1 ✅** As a user, I can send a one-off SMS with `psst sms <to> <message>`, resolving
  the recipient from the contact book or a bare US number. *(Contact resolution verified by
  `ContactBook.Find` behavior; phone normalization by `CarrierGateways` usage — exercised through
  config/notifier tests.)* 🟡 *(downgraded: `SmsAsync` argv→send happy path has no direct
  end-to-end test; flag parsing and transports are tested in isolation.)*
- **PST-US-B2 ✅** As a user, my message reaches an unknown-carrier phone via email-to-SMS
  fanout across every known US gateway, so I don't have to know the carrier. *Given a 10-digit
  number, When sending via email, Then one message goes to each carrier gateway.* *(Fanout/normalize
  logic verified through `PsstConfigurationTests` recipient handling and `PsstNotifierTests`
  transport dispatch; `CarrierGateways` is pure and deterministic.)* 🟡 *(downgraded: no test
  file dedicated to `CarrierGateways` directly.)*
- **PST-US-B3 ✅** As a user, I can pick the transport with `--via twilio|email`, `PSST_VIA`, or a
  per-contact default, with documented precedence. *Given competing sources, When resolving,
  Then `--via` beats env beats contact-default beats project default (email).* *(precedence
  verified by `PsstViaResolver` usage in `PsstNotifierTests`; transport gating by
  `NotifyAsync_NoTransports_ReturnsEmptyAttempts`.)* 🟡 *(downgraded: precedence chain has no
  dedicated `PsstViaResolverTests`; covered indirectly.)*
- **PST-US-B4 ✅** As a user, Twilio sends go to the correct REST endpoint with basic auth and the
  right form fields, and surface a useful error on failure. *Given a 2xx, Then success; Given a
  non-2xx or network error, Then a failure with a (truncated) detail.* *(verified by
  `SendAsync_2xxResponse_ReturnsSuccess`, `SendAsync_HitsCorrectUrl`,
  `SendAsync_SendsBasicAuthHeader`, `SendAsync_PostsFromToAndBodyFormFields`,
  `SendAsync_NonSuccessStatus_ReturnsFailureWithDetail`,
  `SendAsync_NetworkException_ReturnsFailureWithMessage`, `SendAsync_TruncatesLongErrorBody`,
  `TransportName_IsTwilio`.)*
- **PST-US-B5 ✅** As a user, exactly one transport is attempted per send (no surprise fallback),
  matching the resolved `via`. *(verified by `NotifyAsync_FirstTransportSucceeds_DoesNotCallSecond`,
  `NotifyAsync_NoTransports_ReturnsEmptyAttempts`, and `NotifyResult` accounting in
  `AnySmsSent_FalseWhenEmpty`, `AnySmsSent_FalseWhenAllFailed`,
  `FirstSuccess_ReturnsFirstSuccessfulAttempt`.)*
  > Note: the notifier *internally* supports a client list and falls back across it
  > (`NotifyAsync_FirstTransportFails_FallsBackToSecond`,
  > `NotifyAsync_AllTransportsFail_ReportsAllAttempts`), but `BuildClients` supplies exactly one
  > client per resolved `via` ([PST-LAW-4](BIBLE.md#PST-LAW-4)).

## Epic C — Repeat & schedule

- **PST-US-C1 ✅** As a user, I can repeat a message `N` times at an interval (`--repeat`,
  `--interval/--every`), with durations like `30s/5m/2h/1d`. *Given a duration string, Then it
  parses to the right span; bad forms are rejected.* *(verified by `TryParse_AcceptsSecondForms`,
  `TryParse_AcceptsMinuteForms`, `TryParse_AcceptsHourForms`, `TryParse_AcceptsDayForms`,
  `TryParse_RejectsMalformed`, `Parse_ThrowsOnGarbage`, `Format_RoundTripsAndPicksLargestExactUnit`,
  `Format_RoundTripsThroughParse`.)*
- **PST-US-C2 ✅** As a user, I can defer the first send to a wall-clock time
  (`--schedule/--start`), always resolving to the next future occurrence. *Given 12h/24h/whole-hour
  forms, Then they parse; a past time rolls to tomorrow.* *(verified by `TryParse_TwelveHour`,
  `TryParse_TwentyFourHour`, `TryParse_WholeHourShortcut`, `TryParse_NextOccurrence_TodayWhenFuture`,
  `TryParse_NextOccurrence_TomorrowWhenPast`, `TryParse_NextOccurrence_TomorrowWhenSameMinute`,
  `TimeOfDayParser` `TryParse_RejectsMalformed`/`Parse_ThrowsOnGarbage`.)*
- **PST-US-C3 ✅** As a user, a deferred/repeat send detaches to Windows Task Scheduler with a
  correctly-quoted launcher, so cmd metacharacters and `%` in my message don't break the task.
  *(verified by `BuildInvocationLine_DoublesLiteralPercent`,
  `BuildInvocationLine_DoublesEnvVarPercentsSoNothingLeaks`,
  `QuoteForWindowsCommandLine_QuotesCmdMetacharacters`,
  `QuoteForWindowsCommandLine_PassesPlainTokensVerbatim`,
  `BuildInvocationLine_QuotesExePathAndCombinesWithMetacharArgs`.)*
- **PST-US-C4 🟡** As a user, I can `psst scheduled list/cancel/clear` to inspect and cancel
  pending sends. *Listing/cancel logic shells out to `schtasks` (`ScheduledTaskLister`); the
  quoting/registrar half is tested but the list/cancel OS round-trip is verified manually only.*
- **PST-US-C5 🟡** As a user, a successful scheduled send self-cleans (launcher deletes its task +
  sidecar) so nothing lingers in Task Scheduler ([PST-LAW-6](BIBLE.md#PST-LAW-6)). *Verified
  manually; no automated test exercises the real `schtasks /Delete` round-trip.*

## Epic D — Configuration & contacts

- **PST-US-D1 ✅** As a user, my notifier reads credentials from the `MindAttic.Vault` chain
  (vault files → appsettings → `%APPDATA%/MindAttic/Psst/settings.json` → env vars), never the
  repo. *(verified by `PsstConfigurationTests` — `Load_FullTwilio_PopulatesTwilioRecord`,
  `Load_FullEmail_PopulatesEmailRecord`, `Load_EmptyConfiguration_ReturnsAllNullsAndNoErrors`,
  `HasAnySmsTransport_TrueWhenOnlyEmailConfigured`; path resolution by
  `GetSettingsPath_IsSettingsJsonUnderAppData`, `GetAppDataDirectory_LandsUnderRoamingMindAtticPsst`,
  `GetAppDataDirectory_RespectsVaultRoamingRootOverride`.)*
- **PST-US-D2 ✅** As a user, partial/misconfigured credentials produce a clear diagnostic instead
  of a silent failure. *Given a Twilio block missing a field, Then `Twilio` is null and an error
  is recorded.* *(verified by `Load_TwilioMissingAuthToken_ReturnsNullTwilio_AndAddsError`,
  `Load_TwilioWhitespaceField_ReturnsNullTwilio`, `Load_TwilioFullButRecipientMissing_AddsError`,
  `Load_EmailMissingRequiredField_ReturnsNullEmail_AndAddsError`.)*
- **PST-US-D3 🟡** As a user, I can manage a contact book (`psst contacts list/add/rm`) with
  case-insensitive name collision suffixing and optional per-contact `--via`. *`ContactBook`
  add/remove/find semantics are exercised through the notifier/config tests, but there is no
  dedicated `ContactBookTests`/`ContactStoreTests` and the `Contacts` CLI handler is untested.*
- **PST-US-D4 ✅** As a user, `psst ping` shows which transports are configured and what would
  fire, without sending. *Print-only diagnostics (`Ping`); relies on `PsstConfiguration` +
  `CarrierGateways`, both verified above.* 🟡 *(downgraded: `Ping` output itself is not
  asserted by a test.)*

## Epic E — Sound

- **PST-US-E1 ✅** As a user, `psst sound` plays the embedded clip, preferring MP3 (NAudio) and
  falling back to WAV (SoundPlayer), degrading silently off-Windows. *Pipeline integration
  verified by `NotifyAsync_SoundPlayerFails_PropagatesFalseAndKeepsError`,
  `SoundPlayed_FalseWhenSoundFailed`.* 🟡 *(downgraded: actual audio output / NAudio-vs-WAV
  fallback on hardware is verified manually; only the result-propagation seam is unit-tested.)*

## Priority backlog

Dependency-ordered toward "every documented behavior has a direct test" (the headline quality goal):

1. ⬜ **PST-US-B6** Add `CarrierGatewaysTests` — direct coverage of `NormalizeTo10Digits`,
   `BuildFanout`, `Combine` (promotes B2 to a clean ✅).
2. ⬜ **PST-US-B7** Add `PsstViaResolverTests` — precedence chain `--via` > env > contact > default
   (promotes B3).
3. ⬜ **PST-US-D5** Add `ContactBookTests` + `ContactStoreTests` — add/remove/find, collision
   suffixing, round-trip persistence (promotes D3).
4. ⬜ **PST-US-A4** Add a `DescribeExitCode` mapping test (promotes A3).
5. ⬜ **PST-US-C6** Integration-test the `schtasks` list/cancel round-trip behind a Windows-only
   trait (promotes C4/C5).
6. ⬜ **PST-US-D6** Assert `psst ping` output shape (promotes D4).

### Audit log

No story has had its original ask rewritten since Codex adoption (2026-06-07); the stories above
were reverse-engineered from the existing shipped code and `README.md`. Future changes that alter
a story's intent must preserve the original ask here, marked "(original spec — audit log)".
