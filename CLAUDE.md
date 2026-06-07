# MindAttic.Psst Project Rules

## Conversation
- A bare "do" / "do it" / "yes" from the user means "continue", "keep going", "proceed". Resume the current task without asking for clarification.

## What this is
- A standalone notifier — like someone tapping your shoulder when a CLI process finishes. Plays a short attention-getter clip locally and sends an SMS.
- Usage: `psst -- <command> [args...]` — wraps the command, captures exit code, fires sound + SMS on exit.
- SMS path: email-to-SMS carrier fanout by default; Twilio A2P 10DLC when selected via `--via twilio` / `PSST_VIA` / per-contact default. Creds from MindAttic.Vault (`MindAttic:Vault:Notifications`).
- Sound: embedded MP3 played via NAudio (primary), falls back to embedded WAV via `System.Media.SoundPlayer`; degrades silently off-Windows.

## Codex — how to work in this repo
- **Source of truth lives in `docs/`** (MindAttic Codex standard). Read these before changing behavior:
  - `docs/BIBLE.md` (L0) — what Psst IS / is NOT, architecture, and the Laws (`PST-LAW-n`). It **inherits** the org-wide `../MindAttic.HouseRules.md` (`HOUSE-LAW-n`) by reference.
  - `docs/AMENDMENTS.md` (L1) — append-only change log. **Amendment wins** over the bible.
  - `docs/USER_STORIES.md` (L2) — test-cited stories; every `✅` names its xUnit test.
  - `docs/rfc/` — design notes that graduate into the bible + stories.
- **Conventions.** A fact lives in exactly one layer; cross-reference by stable `{#PST-...}` anchor, never by line number. Mark a story `✅` only when a test/build proves it (`HOUSE-LAW-8`) — otherwise `🟡`/`⬜`.
- **Tooling.** `tools/codex.ps1 doctor` lints the canon (front-matter, anchor/cross-ref integrity, story↔test citations, cited paths, digest freshness). `tools/codex.ps1 digest` regenerates `docs/BIBLE.digest.md` (never hand-edit it). Run `digest` after editing `BIBLE.md`, then `doctor` before committing.
- **Session context.** `.claude/hooks/inject-digest.ps1` (SessionStart, wired in `.claude/settings.json`) injects `docs/BIBLE.digest.md` as authoritative context at the start of each session.
