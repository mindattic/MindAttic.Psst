<#
.SYNOPSIS
    Regenerates this repo's README.htm from README.md.
.DESCRIPTION
    Thin wrapper only. The real README -> HTML translation layer lives once, at the
    workspace root, in codex-standard/build-readme.ps1 -- every MindAttic repo shares
    this exact same engine so all README.htm files look and behave identically. Do not
    duplicate that logic here; edit the shared engine and every repo's wrapper picks up
    the change on its next run.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-readme.ps1
#>
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$engine   = Join-Path $PSScriptRoot '..\..\codex-standard\build-readme.ps1'
& $engine -Root $repoRoot
