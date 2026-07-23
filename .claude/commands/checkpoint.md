---
description: Print the current discussion to a paper transcript, so it survives /clear and auto-refills on the next session.
argument-hint: "[optional note to emphasize what matters most]"
allowed-tools: Write
---

# Checkpoint — the paper transcript

Person-of-Interest protocol: the context window is about to be wiped by `/clear`. Before it is,
print the live discussion to a "paper transcript" on disk. A SessionStart hook (matcher `clear`)
re-ingests that transcript the instant `/clear` runs, so the next session wakes up already
knowing what we were doing. The transcript is consumed on read — one-shot, no residue.

## Do this now

Write a handoff file to **`.claude/checkpoint.md` in the current project root** — construct the
absolute path from your current working directory (`<cwd>\.claude\checkpoint.md`). Overwrite it if
it exists.

Capture the *current* discussion — not the whole session, just what a fresh instance of you needs
to resume seamlessly. Be concrete: names, ids, file paths, exact commands. No vague summaries.

Use this structure:

```markdown
# Checkpoint — <one-line title of what we're doing>
_Printed: <fill the actual date>_

## Current task
<The single thing we are mid-work on, stated as a resumable instruction. If the user gave a note
in $ARGUMENTS, lead with it.>

## Decisions locked this session
- <decisions already made that must not be re-litigated>

## State / where we are
- <what's done, what's in flight, last action taken and its result>

## Open questions / pending
- <anything unresolved the next session must decide or ask>

## Next concrete steps
1. <the very next action to take on resume>
2. ...

## Anchors
- Files: <paths touched or relevant>
- Names / ids: <symbols, tickets, entities relevant to the work>
- Commands to re-run: <exact CLI/build/test calls>
```

If `$ARGUMENTS` is non-empty, weave that emphasis into **Current task** so the most important
thread is unmistakable after the wipe.

## After writing

Tell the user, in one line, that the transcript is printed and armed, and to run `/clear` now —
the discussion will refill automatically on the other side. Do not do anything else.
