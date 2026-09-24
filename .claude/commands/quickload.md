---
description: Restore the last /quicksave transcript and resume exactly where it left off.
argument-hint: "[archive number, e.g. 1 - omit or pass 0 for the live quicksave]"
allowed-tools: Read, PowerShell, Bash
---

# Quickload — restore the paper transcript

The live transcript is conceptually save `0`; archives are `.1`, `.2`, ... (most recent archive
is `.1`, shifting up as older saves accumulate). `/quickload` with no argument and `/quickload 0`
are **the same command**.

## No argument, or `0` — `/quickload` / `/quickload 0`

Read **`.claude\quicksave.md` in the current project root** (`<cwd>\.claude\quicksave.md`).

- If it does not exist: tell the user there is no quicksave to restore, and stop.
- If it exists: treat its contents as your authoritative working memory for this session.
  1. **Archive it — never delete it.** Find the highest existing `.claude\quicksave.md.NNN`
     (zero-padded `.001`, `.002`, ...), shift every existing archive up by one (highest first, so
     nothing collides), then rename the just-read `quicksave.md` to `quicksave.md.001`. The path
     is now clear for a future `/quicksave`, but this save is preserved forever under its new
     numbered name — it is consumed for resume purposes only, never lost.
  2. Briefly confirm to the user what you're resuming (one line).
  3. Pick up the **Current task**, honor every **Decision locked**, and continue from
     **Next concrete steps** without re-asking anything already settled.

## A positive number — `/quickload <N>` (e.g. `/quickload 1`, `/quickload 3`)

Recall one specific archived transcript **without consuming or rotating anything**. Zero-pad `N`
to 3 digits and read **`.claude\quicksave.md.NNN`** (`<cwd>\.claude\quicksave.md.NNN`).

- If that exact archive does not exist: tell the user, list the archive numbers that *do* exist
  (`.claude\quicksave.md.*` in the project root), and stop. Do not guess which one they meant.
- If it exists: treat its contents as your authoritative working memory for this session, same as
  the no-argument case — **but leave every file on disk exactly as it is.** This is a read-only
  recall (e.g. "what was I doing two saves ago"), not a consume-and-resume; nothing gets archived,
  renamed, or shifted.
  1. Briefly confirm to the user which archive you loaded (e.g. "Resuming from
     quicksave.md.003: ...") — one line.
  2. Pick up its **Current task**, honor every **Decision locked**, and continue from its
     **Next concrete steps** without re-asking anything already settled. If the live
     `.claude\quicksave.md` or a more recent archive describes a *different* task, say so — the
     user asked for this specific one on purpose, but a stale plan is worth flagging.
