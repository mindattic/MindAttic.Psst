<#
  SessionStart hook (matcher: clear) - re-ingest the checkpoint transcript after /clear.

  Reads <repo>\.claude\checkpoint.md (written by the /checkpoint command), injects it as
  authoritative resume context, then DELETES it so the refill is one-shot (subsequent /clears
  stay clean). Person-of-Interest protocol: the Machine reloads its printed stack to survive
  the memory wipe.

  Emits Claude Code hook JSON on stdout. PowerShell 5.1 / Win-1252 safe: every non-ASCII char
  is escaped to \uXXXX so the JSON is pure ASCII. If no transcript exists, emits {}.
#>
$ErrorActionPreference = 'Stop'

# repo root = two levels up from this script (<repo>\.claude\hooks\restore-handoff.ps1)
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$handoff  = Join-Path $repoRoot '.claude\checkpoint.md'

if (-not (Test-Path $handoff)) { Write-Output '{}'; exit 0 }
$body = Get-Content -LiteralPath $handoff -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($body)) {
  Remove-Item -LiteralPath $handoff -Force -ErrorAction SilentlyContinue
  Write-Output '{}'; exit 0
}

$preamble = @'
RESUME CONTEXT (paper transcript, re-ingested after /clear). The context window was just wiped.
The block below is the checkpoint you printed before the wipe describing exactly what you were
doing. Treat it as your working memory for this session: pick up the Current task, honor the
Decisions locked, and continue from Next concrete steps without re-asking what was already
settled. Open by briefly confirming to the user what you're resuming, then keep going. This
transcript has been consumed (deleted) - it will not refill again.

'@

$text = $preamble + $body

# JSON-escape to pure ASCII.
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $text.ToCharArray()) {
  $code = [int][char]$ch
  switch ($ch) {
    '"'  { [void]$sb.Append('\"') }
    '\'  { [void]$sb.Append('\\') }
    "`b" { [void]$sb.Append('\b') }
    "`f" { [void]$sb.Append('\f') }
    "`n" { [void]$sb.Append('\n') }
    "`r" { [void]$sb.Append('\r') }
    "`t" { [void]$sb.Append('\t') }
    default {
      if ($code -lt 32 -or $code -gt 126) {
        [void]$sb.Append('\u' + $code.ToString('x4'))
      } else {
        [void]$sb.Append($ch)
      }
    }
  }
}
$escaped = $sb.ToString()

# Consume the transcript (one-shot) BEFORE emitting, so a crash mid-emit can't leave it to
# ambush a later /clear.
Remove-Item -LiteralPath $handoff -Force -ErrorAction SilentlyContinue

$json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"' + $escaped + '"}}'
Write-Output $json
exit 0
