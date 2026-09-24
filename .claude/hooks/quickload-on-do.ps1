<#
  UserPromptSubmit hook - restore the /quicksave transcript when the user types a bare "do".

  After /quicksave + /clear, the next session restores by typing "do" (this hook) or running
  /quickload (the command). Any other prompt passes through untouched. Reads
  <repo>\.claude\quicksave.md, injects it as authoritative resume context, then ARCHIVES it
  (renamed to .001, shifting older archives up by one) so the refill is one-shot but nothing is
  ever deleted. Person-of-Interest protocol: the Machine reloads its printed stack, then files it.

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

# Archive instead of delete: shift any existing .NNN archives up by one, then file this save as
# .001. Nothing /quicksave ever wrote is destroyed by /quickload or the "do" hook.
function Move-ToArchive([string]$path) {
  $n = 1
  while (Test-Path -LiteralPath ('{0}.{1:D3}' -f $path, $n)) { $n++ }
  for ($i = $n - 1; $i -ge 1; $i--) {
    Move-Item -LiteralPath ('{0}.{1:D3}' -f $path, $i) -Destination ('{0}.{1:D3}' -f $path, ($i + 1)) -Force
  }
  Move-Item -LiteralPath $path -Destination ('{0}.001' -f $path) -Force
}

if ([string]::IsNullOrWhiteSpace($body)) {
  Move-ToArchive $save
  Write-Output '{}'; exit 0
}

$preamble = @'
RESUME CONTEXT (quicksave transcript, restored because the user typed "do"). The context window
was wiped since this was printed. The block below is the quicksave describing exactly what you
were doing. Treat it as your working memory for this session: pick up the Current task, honor
the Decisions locked, and continue from Next concrete steps without re-asking what was already
settled. Open by briefly confirming to the user what you're resuming, then keep going. This
transcript has been archived (renamed to .001, not deleted) - it will not refill on its own.

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

# Archive the transcript (one-shot for resume) BEFORE emitting, so a crash mid-emit can't leave
# it to ambush a later "do". Nothing is ever deleted - it becomes quicksave.md.001.
Move-ToArchive $save

$json = '{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"' + $escaped + '"}}'
Write-Output $json
exit 0
