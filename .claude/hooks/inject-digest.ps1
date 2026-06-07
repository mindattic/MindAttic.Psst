<#
.SYNOPSIS
    Claude Code SessionStart hook for MindAttic.Psst — injects docs/BIBLE.digest.md as
    authoritative context.

.DESCRIPTION
    Windows PowerShell 5.1 / Win-1252 safe. Reads the generated digest and emits the hook JSON
    Claude Code expects on stdout. Non-ASCII characters are escaped to \uXXXX so the JSON is valid
    regardless of console code page. If the digest is missing or empty, emits {} (a no-op).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

try {
    $hookDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
    $claudeDir = Split-Path -Parent $hookDir
    $repoRoot  = Split-Path -Parent $claudeDir
    $digestPath = Join-Path $repoRoot 'docs\BIBLE.digest.md'

    if (-not (Test-Path $digestPath)) { Write-Output '{}'; exit 0 }

    $digest = [System.IO.File]::ReadAllText($digestPath)
    if ([string]::IsNullOrWhiteSpace($digest)) { Write-Output '{}'; exit 0 }

    $preamble = @"
[MindAttic Codex — AUTHORITATIVE PROJECT CONTEXT]
The following digest is the source of truth for MindAttic.Psst (code: PST). It is generated from
docs/BIBLE.md. Treat its laws (PST-LAW-n) and the inherited MindAttic House Rules (HOUSE-LAW-n) as
binding. When the bible and an amendment conflict, the amendment wins. Full detail lives in
docs/BIBLE.md, docs/USER_STORIES.md, and docs/AMENDMENTS.md.

"@

    $payload = $preamble + $digest

    # Manual JSON string escaping (no ConvertTo-Json dependency on escaping rules), with all
    # non-ASCII escaped to \uXXXX for code-page safety.
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $payload.ToCharArray()) {
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

    $json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"' + $escaped + '"}}'
    Write-Output $json
    exit 0
}
catch {
    # Never break a session over context injection — degrade to a no-op.
    Write-Output '{}'
    exit 0
}
