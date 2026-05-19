Stage and commit the current working-tree changes.

Run these in parallel first:
- `git status` (no `-uall` flag — large repos)
- `git diff` (staged + unstaged)
- `git log -n 10 --oneline` (match the repo's commit-message style)

Then:
1. Draft a concise (1-2 sentence) commit message focused on the *why*, not the *what*. Match the recent log's tone.
2. Stage relevant files by name — never `git add -A` or `git add .` (avoid pulling in `.env`, credentials, large binaries).
3. Skip secrets: warn before committing anything that looks like a credential file.
4. Create a NEW commit (never `--amend`) with this footer:
   ```
   Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
   ```
   Pass the message via heredoc so multi-line formatting is preserved.
5. Run `git status` after the commit and confirm it succeeded.

Do NOT push. Do NOT skip hooks (`--no-verify`). If a pre-commit hook fails, fix the underlying issue and create a fresh commit — never amend.

If there are no changes, say so and exit without creating an empty commit.
