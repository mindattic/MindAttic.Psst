Stage, commit, and push the current working-tree changes.

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
6. Push to the upstream remote (`git push`). If the branch has no upstream yet, push with `-u origin <branch>`. Surface the remote's response — and if the push is rejected (e.g. non-fast-forward), STOP and report to the user instead of force-pushing.
7. Print the commit hash + a one-line summary.

Do NOT skip hooks (`--no-verify`). Do NOT force-push. If a pre-commit hook fails, fix the underlying issue and create a fresh commit — never amend. If a push to `main`/`master` is rejected, warn the user before any recovery action; do not force-push protected branches.

If there are no changes, say so and exit without creating an empty commit (and skip the push).
