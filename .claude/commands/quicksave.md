---
description: Print the current discussion to a paper transcript, so it survives /clear and restores on 'do' or /quickload.
argument-hint: "[optional note to emphasize what matters most]"
allowed-tools: Write
---

# Quicksave — the paper transcript

Person-of-Interest protocol: the context window is about to be wiped by `/clear`. Before it is,
print the live discussion to a "paper transcript" on disk. On the other side of the wipe, the
next session restores it when the user types a bare `do` (a UserPromptSubmit hook injects the
transcript) or runs `/quickload`. The transcript is consumed on read — one-shot, no residue.

## Do this now

Write a handoff file to **`.claude/quicksave.md` in the current project root** — construct the
absolute path from your current working directory (`<cwd>\.claude\quicksave.md`). Overwrite it if
it exists.

Capture the *current* discussion — not the whole session, just what a fresh instance of you needs
to resume seamlessly. Be concrete: names, ids, file paths, exact commands. No vague summaries.

Use this structure:

```markdown
# Quicksave — <one-line title of what we're doing>
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

Tell the user, in one line, that the quicksave is written and armed: run `/clear`, then type
`do` (or `/quickload`) on the other side to restore. Do not do anything else.
