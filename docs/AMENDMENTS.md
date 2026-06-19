---
codex: 1
project: MindAttic.Psst
code: PST
layer: amendments
status: living
updated: 2026-06-19
---

# MindAttic.Psst — Amendments (append-only; amendment wins over the bible)

> Append-only change log. Never rewrite an amendment — supersede it with a new one. Beyond ~25,
> fold the settled ones into [BIBLE.md](BIBLE.md) and start a new epoch (note the git tag).

## PST-A1 — Adopt the Codex documentation standard (supersedes —)

**What changed.** Installed the MindAttic Codex canonical-documentation layout for this repo:
`docs/BIBLE.md` (L0), `docs/USER_STORIES.md` (L2), this `docs/AMENDMENTS.md` (L1),
`docs/rfc/0001-example.md`, the generated `docs/BIBLE.digest.md`, the `tools/codex.ps1`
doctor/digest CLI, and the `.claude/hooks/inject-digest.ps1` SessionStart hook wired into
`.claude/settings.json`. Added a Codex section to `CLAUDE.md`.

**Why.** Give the project a single source of truth with stable IDs, test-cited stories, and an
auto-injected digest so every Claude session starts from authoritative context.

**Migration.** None — this repo had no prior canon docs (`game_bible.md`, `ARCHITECTURE.md`,
etc.). All content was reverse-engineered from the existing source, `README.md`, and `CLAUDE.md`;
no source/application code was modified. The bible **inherits** the pre-existing org-wide
[`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference (not copied, not modified).

**Defaults chosen under ambiguity.**
- **CODE = `PST`** (PascalCase initials of "Psst"; also matches the "first 3 letters" fallback).
- **Domain class = `library`** — the shippable artifact is the `MindAttic.Psst` NuGet package;
  `psst.exe` is a CLI front door over it ([HOUSE-LAW-6](../../MindAttic.HouseRules.md#HOUSE-LAW-6)).
- **No L5 canon-as-data** — the only catalog (`CarrierGateways.UnitedStatesDomains`) is small and
  deliberately code-resident per [PST-LAW-3](BIBLE.md#PST-LAW-3).

## PST-A2 — Codex full-sync: reconcile config-source docs against reality (2026-06-07)

**What changed.** `README.md` and `CLAUDE.md` contained stale references to a retired
configuration pattern:

1. **User Secrets retired.** Three README mentions of "User Secrets" as a live credential
   source replaced with the actual APPDATA bucket paths. `CLAUDE.md`'s "SMS path: Twilio first"
   summary corrected to reflect that email-to-SMS fanout is the project default and Twilio
   requires explicit opt-in (`--via twilio` / `PSST_VIA`).

2. **Non-existent `.env` option removed.** README documented an "Option B — `.env` fallback"
   (`%APPDATA%\MindAttic\Psst\.env`) and a table row for it; inspecting
   `PsstCli.BuildConfiguration()` confirms no `.env` loader exists in the chain. The option and
   table row were removed; former Option C/D renumbered to B/C.

3. **Sound description updated.** `CLAUDE.md` described sound as "WAV via SoundPlayer" only;
   the actual `PsstSoundPlayer` tries MP3 via NAudio first and falls back to WAV.

**Why.** Docs follow code; these drifts would have given any reader (or Claude session) a
false picture of the credential chain and transport defaults. The Vault `CLAUDE.md` explicitly
states "User Secrets is retired — do not reintroduce it."

**BIBLE canon impact.** None — `docs/BIBLE.md` already described the correct Vault APPDATA
Notifications chain and email-first transport semantics. Only `README.md` and `CLAUDE.md`
were out of sync.

## PST-A3 — Remove Twilio; email-only SMS transport (2026-06-19)

**What changed.** Twilio support has been removed from the codebase. Email-to-SMS carrier
fanout is now the only SMS transport.

**Code removed:**
- `MindAttic.Psst/Sms/TwilioSmsClient.cs` — deleted.
- `MindAttic.Psst/PsstFeatures.cs` — deleted (`TwilioEnabled` compile-time gate no longer needed).
- `MindAttic.Psst.Tests/Sms/TwilioSmsClientTests.cs` — deleted (9 tests).
- `TwilioSettings` record removed from `PsstConfiguration.cs`.
- `PsstVia.Twilio` enum value removed; `PsstVia` now contains only `Email`.
- `--via twilio|email` CLI flag removed from `sms` and `contacts add` subcommands.
- `PsstViaResolver.Resolve` signature simplified (no `cliFlagValue` parameter).
- `PsstNotifier` public constructor no longer takes `HttpClient?` (Twilio was the only HTTP consumer).
- Twilio configuration template removed from `psst ping` output.

**Extensibility preserved.** `ISmsClient`, `PsstVia`, `PsstViaResolver`, and `PsstNotifier.BuildClients`
retain their abstraction shapes so a future alternative transport (e.g. a direct carrier API or a
different SMS service) slots in by adding an enum value, a `TryParse` branch, a `BuildClients`
case, and re-introducing `--via` to the CLI.

**Why.** Twilio was not the right system for this use case.

**Migration.** Any `settings.json` or `providers.json` with a `twilio:` block can be left in place
or cleaned up — the config loader now silently ignores unknown keys. `PSST_VIA=twilio` in the
environment will be treated as an unrecognized value and fall through to the Email default.

**Test count.** 114 → 100 (9 TwilioSmsClientTests removed, 5 TwilioSettings config tests removed).

**BIBLE canon impact.** `docs/BIBLE.md` updated: architecture diagram, §4.2 domain model,
§4.3 key services, PST-LAW-4, §6 verified state (test count), §9 glossary.
