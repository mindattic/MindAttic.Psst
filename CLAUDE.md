# MindAttic.Psst Project Rules

## Conversation
- A bare "do" / "do it" / "yes" from the user means "continue", "keep going", "proceed". Resume the current task without asking for clarification.

## What this is
- A standalone notifier — like someone tapping your shoulder when a CLI process finishes. Plays a short attention-getter clip locally and sends an SMS.
- Usage: `psst -- <command> [args...]` — wraps the command, captures exit code, fires sound + SMS on exit.
- SMS path: Twilio first (creds from MindAttic.Vault), email-to-SMS gateway as fallback.
- Sound: short WAV clip embedded as a resource, played via `System.Media.SoundPlayer`.
