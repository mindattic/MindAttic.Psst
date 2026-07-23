<#
  Statusline - live context-window gauge with escalating checkpoint warnings.

  Claude Code pipes a JSON status blob to this script on stdin every render. We surface the
  usual bearings (model / folder / git branch) plus a context-usage segment that escalates as
  the window fills, so there is time to run /checkpoint and /clear WITH ROOM TO SPARE before the
  harness auto-compacts.

  Thresholds (percent of context used) - tune these two numbers to taste:
    WARN  -> "[!] checkpoint soon"   (amber)
    CRIT  -> "[!!] /checkpoint NOW"  (red, bold)
  Defaults leave real headroom below the ~95% auto-compaction point.

  Notes: used_percentage counts input tokens only and is null before the first API call and
  right after a compact. Pure-ASCII markers ([!]/[!!]) so any terminal renders them.
#>
$WARN = 80
$CRIT = 90

$raw = [Console]::In.ReadToEnd()
try { $j = $raw | ConvertFrom-Json } catch { $j = $null }

$e = [char]27
$reset = "$e[0m"; $dim = "$e[2m"; $cyan = "$e[36m"; $amber = "$e[33m"; $red = "$e[1;31m"; $green = "$e[32m"

# --- model ---
$model = ''
if ($j -and $j.model -and $j.model.display_name) { $model = $j.model.display_name }

# --- current dir (leaf) ---
$cwd = ''
if ($j -and $j.workspace -and $j.workspace.current_dir) { $cwd = $j.workspace.current_dir }
elseif ($j -and $j.cwd) { $cwd = $j.cwd }
$leaf = if ($cwd) { Split-Path -Leaf $cwd } else { '' }

# --- git branch (read .git/HEAD directly; walk up; no git process) ---
$branch = ''
$dir = $cwd
for ($i = 0; $i -lt 6 -and $dir; $i++) {
  $head = Join-Path $dir '.git\HEAD'
  if (Test-Path $head) {
    $h = (Get-Content -LiteralPath $head -Raw -ErrorAction SilentlyContinue).Trim()
    if ($h -match '^ref:\s*refs/heads/(.+)$') { $branch = $Matches[1] }
    elseif ($h) { $branch = $h.Substring(0, [Math]::Min(7, $h.Length)) }
    break
  }
  $parent = Split-Path -Parent $dir
  if ($parent -eq $dir) { break }
  $dir = $parent
}

# --- context segment ---
$pct = $null
if ($j -and $j.context_window -and $null -ne $j.context_window.used_percentage) {
  $pct = [int]$j.context_window.used_percentage
}
if ($null -eq $pct) {
  $ctx = "${dim}ctx --%${reset}"
} elseif ($pct -ge $CRIT) {
  $ctx = "${red}[!!] ctx ${pct}% - /checkpoint NOW${reset}"
} elseif ($pct -ge $WARN) {
  $ctx = "${amber}[!] ctx ${pct}% - checkpoint soon${reset}"
} else {
  $ctx = "${green}ctx ${pct}%${reset}"
}

# --- assemble ---
$parts = @()
if ($model)  { $parts += "${cyan}$model${reset}" }
if ($leaf)   { $parts += "$leaf" }
if ($branch) { $parts += "${dim}($branch)${reset}" }
$parts += $ctx

Write-Output ($parts -join "  ${dim}|${reset}  ")
