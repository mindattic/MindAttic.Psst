<#
  UserPromptSubmit hook - restore the /quicksave transcript when the user types a bare "do".

  After /quicksave + /clear, the next session restores by typing "do" (this hook) or running
  /quickload (the command). Any other prompt passes through untouched. Reads
  <repo>\.claude\quicksave.md, injects it as authoritative resume context, then DELETES it so
  the refill is one-shot. Person-of-Interest protocol: the Machine reloads its printed stack.

  Emits Claude Code hook JSON on stdout. PowerShell 5.1 / Win-1252 safe: every non-ASCII char
  is escaped to \uXXXX so the JSON is pure ASCII. Emits {} whenever there is nothing to do.
#>
$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
try { $j = $raw | ConvertFrom-Json } catch { $j = $null }
$prompt = if ($j -and $j.prompt) { [string]$j.prompt } else { '' }

# Only a bare "do" / "do it" triggers the restore - everything else passes through.
if ($prompt -notmatch '^\s*(do|do it)\s*[.!]*\s*$') { Write-Output '{}'; exit 0 }

# repo root = two levels up from this script (<repo>\.claude\hooks\quickload-on-do.ps1)
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$save     = Join-Path $repoRoot '.claude\quicksave.md'

if (-not (Test-Path $save)) { Write-Output '{}'; exit 0 }
$body = Get-Content -LiteralPath $save -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($body)) {
  Remove-Item -LiteralPath $save -Force -ErrorAction SilentlyContinue
  Write-Output '{}'; exit 0
}

$preamble = @'
RESUME CONTEXT (quicksave transcript, restored because the user typed "do"). The context window
was wiped since this was printed. The block below is the quicksave describing exactly what you
were doing. Treat it as your working memory for this session: pick up the Current task, honor
the Decisions locked, and continue from Next concrete steps without re-asking what was already
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
# ambush a later "do".
Remove-Item -LiteralPath $save -Force -ErrorAction SilentlyContinue

$json = '{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"' + $escaped + '"}}'
Write-Output $json
exit 0
