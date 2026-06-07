---
codex: 1
project: MindAttic.Psst
code: PST
layer: amendments
status: living
updated: 2026-06-07
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
