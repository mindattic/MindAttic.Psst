<#
.SYNOPSIS
    MindAttic Codex tooling for MindAttic.Psst — `doctor` (lint the canon) and `digest`
    (regenerate docs/BIBLE.digest.md).

.DESCRIPTION
    Windows PowerShell 5.1 compatible. No external modules, no build step.

    Subcommands:
      doctor   Validate the Codex docs; exit non-zero on any hard error.
      digest   Regenerate docs/BIBLE.digest.md from BIBLE.md (sections 1, 3, 5, 9) plus a
               status index and the latest amendment head.

.EXAMPLE
    pwsh tools/codex.ps1 doctor
    powershell -File tools/codex.ps1 digest
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('doctor', 'digest')]
    [string]$Command = 'doctor'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$DocsDir   = Join-Path $RepoRoot 'docs'
$BiblePath = Join-Path $DocsDir 'BIBLE.md'
$StoriesPath    = Join-Path $DocsDir 'USER_STORIES.md'
$AmendmentsPath = Join-Path $DocsDir 'AMENDMENTS.md'
$DigestPath     = Join-Path $DocsDir 'BIBLE.digest.md'
$RfcDir         = Join-Path $DocsDir 'rfc'
$DataDir        = Join-Path $DocsDir 'data'

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Read-Text([string]$path) {
    return [System.IO.File]::ReadAllText($path)
}

function Get-FrontMatter([string]$text) {
    # Returns a hashtable of the leading YAML front-matter, or $null if absent.
    if ($text -notmatch "^(?s)---\s*\r?\n(.*?)\r?\n---") { return $null }
    $block = $Matches[1]
    $map = @{}
    foreach ($line in ($block -split "\r?\n")) {
        if ($line -match '^\s*([A-Za-z0-9_]+)\s*:\s*(.*?)\s*$') {
            $map[$Matches[1]] = $Matches[2]
        }
    }
    return $map
}

# Files that must carry valid codex front-matter, with the layer each expects.
function Get-CanonFiles {
    $list = @()
    $list += [pscustomobject]@{ Path = $BiblePath;      Layer = 'bible' }
    $list += [pscustomobject]@{ Path = $StoriesPath;    Layer = 'stories' }
    $list += [pscustomobject]@{ Path = $AmendmentsPath; Layer = 'amendments' }
    if (Test-Path $RfcDir) {
        Get-ChildItem -Path $RfcDir -Filter '*.md' -File | ForEach-Object {
            $list += [pscustomobject]@{ Path = $_.FullName; Layer = 'rfc' }
        }
    }
    if (Test-Path $DataDir) {
        Get-ChildItem -Path $DataDir -Filter '*.json' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/]_schema[\\/]' } |
            ForEach-Object {
                $list += [pscustomobject]@{ Path = $_.FullName; Layer = 'data' }
            }
    }
    return $list
}

# ===========================================================================
# DOCTOR
# ===========================================================================
function Invoke-Doctor {
    $script:errors = @()
    $script:warnings = @()
    $script:checks = @()

    function Pass($m) { $script:checks += "  [PASS] $m" }
    function Fail($m) { $script:errors += $m; $script:checks += "  [FAIL] $m" }
    function Warn($m) { $script:warnings += $m; $script:checks += "  [WARN] $m" }

    # --- 1. Required files exist -------------------------------------------
    foreach ($req in @($BiblePath, $StoriesPath, $AmendmentsPath)) {
        if (Test-Path $req) { Pass "exists: $(Resolve-Path $req -Relative)" }
        else { Fail "missing required file: $req" }
    }

    # --- 2. Front-matter on every canon file -------------------------------
    $today = (Get-Date).ToString('yyyy-MM-dd')
    foreach ($cf in (Get-CanonFiles)) {
        if (-not (Test-Path $cf.Path)) { continue }
        $rel = (Resolve-Path $cf.Path -Relative)
        if ($cf.Path -like '*.json') {
            # JSON data files carry front-matter as a top-level "_codex" object — but per Phase 2
            # this repo intentionally has no L5 data, so this branch is defensive only.
            continue
        }
        $text = Read-Text $cf.Path
        $fm = Get-FrontMatter $text
        if ($null -eq $fm) { Fail "no codex front-matter: $rel"; continue }
        if (-not $fm.ContainsKey('codex')) { Fail "front-matter missing 'codex': $rel" }
        foreach ($key in @('project', 'code', 'layer', 'status', 'updated')) {
            if (-not $fm.ContainsKey($key)) { Fail "front-matter missing '$key': $rel" }
        }
        if ($fm.ContainsKey('layer') -and $fm.layer -ne $cf.Layer) {
            Fail "front-matter layer '$($fm.layer)' != expected '$($cf.Layer)': $rel"
        }
        if ($fm.ContainsKey('code') -and $fm.code -ne 'PST') {
            Warn "front-matter code '$($fm.code)' != 'PST': $rel"
        }
        if ($fm.ContainsKey('updated') -and $fm.updated -notmatch '^\d{4}-\d{2}-\d{2}$') {
            Fail "front-matter 'updated' not YYYY-MM-DD: $rel"
        }
    }
    if ($errors.Count -eq 0) { Pass 'front-matter valid on all canon files' }

    # --- 3. Anchor IDs unique + cross-refs resolve -------------------------
    $allText = @{}
    foreach ($f in @($BiblePath, $StoriesPath, $AmendmentsPath)) {
        if (Test-Path $f) { $allText[$f] = Read-Text $f }
    }
    if (Test-Path $RfcDir) {
        Get-ChildItem $RfcDir -Filter '*.md' -File | ForEach-Object { $allText[$_.FullName] = Read-Text $_.FullName }
    }

    $anchors = @{}
    foreach ($kv in $allText.GetEnumerator()) {
        $rel = (Resolve-Path $kv.Key -Relative)
        $m = [regex]::Matches($kv.Value, '\{#([^}]+)\}')
        foreach ($match in $m) {
            $id = $match.Groups[1].Value
            if ($anchors.ContainsKey($id)) {
                Fail "duplicate anchor id '{#$id}' (in $rel and $($anchors[$id]))"
            } else {
                $anchors[$id] = $rel
            }
        }
    }
    if ($anchors.Count -gt 0) { Pass "$($anchors.Count) anchor id(s), all unique" }

    # Cross-ref links of the form [text](...#anchor). Resolve same-doc (#x) and
    # cross-doc (file.md#x) links into a #PST- / #HOUSE- anchor namespace.
    $linkRefs = 0
    foreach ($kv in $allText.GetEnumerator()) {
        $rel = (Resolve-Path $kv.Key -Relative)
        $links = [regex]::Matches($kv.Value, '\]\(([^)]*?#[^) ]+)\)')
        foreach ($lk in $links) {
            $target = $lk.Groups[1].Value
            $hashIdx = $target.IndexOf('#')
            $anchor = $target.Substring($hashIdx + 1)
            $linkRefs++
            # House-rules anchors live in the external (pre-existing) file — verify there.
            if ($anchor -like 'HOUSE-*') {
                $housePath = Join-Path $RepoRoot '..\MindAttic.HouseRules.md'
                if (Test-Path $housePath) {
                    $houseText = Read-Text (Resolve-Path $housePath)
                    if ($houseText -notmatch "\{#$([regex]::Escape($anchor))\}") {
                        Fail "cross-ref '#$anchor' in $rel not found in MindAttic.HouseRules.md"
                    }
                } else {
                    Warn "cross-ref '#$anchor' in $rel — MindAttic.HouseRules.md not found to verify"
                }
                continue
            }
            # Only validate intra-codex anchors (those we mint as {#...}).
            if ($anchor -match '^(PST|HOUSE)-') {
                if (-not $anchors.ContainsKey($anchor)) {
                    Fail "cross-ref '#$anchor' in $rel does not resolve to any {#$anchor}"
                }
            }
        }
    }
    if ($errors.Count -eq 0) { Pass "$linkRefs cross-ref link(s) resolve" }

    # --- 4. L5 data files validate against schema (none expected) ----------
    if (Test-Path $DataDir) {
        $dataFiles = Get-ChildItem $DataDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/]_schema[\\/]' }
        if ($dataFiles) {
            $ids = @{}
            foreach ($df in $dataFiles) {
                try { $obj = (Read-Text $df.FullName) | ConvertFrom-Json } catch { Fail "invalid JSON: $($df.Name)"; continue }
                foreach ($e in @($obj)) {
                    if ($e.PSObject.Properties.Name -contains 'id') {
                        if ($ids.ContainsKey($e.id)) { Fail "duplicate data id '$($e.id)'" } else { $ids[$e.id] = $df.Name }
                    }
                }
            }
            Pass "$($dataFiles.Count) data file(s) checked; $($ids.Count) entity id(s)"
        } else { Pass 'no L5 data files (none expected for this domain)' }
    } else { Pass 'no docs/data (none expected for this domain)' }

    # --- 5. Every done story names a test that exists ----------------------
    if (Test-Path $StoriesPath) {
        $storyText = $allText[$StoriesPath]
        # Gather test tokens from the test tree once.
        $testTokens = New-Object 'System.Collections.Generic.HashSet[string]'
        $testRoot = Join-Path $RepoRoot 'MindAttic.Psst.Tests'
        if (Test-Path $testRoot) {
            Get-ChildItem $testRoot -Filter '*.cs' -File -Recurse | ForEach-Object {
                $src = Read-Text $_.FullName
                foreach ($mm in [regex]::Matches($src, '(?:public|internal)\s+(?:async\s+)?[A-Za-z<>\[\]]+\s+([A-Za-z0-9_]+)\s*\(')) {
                    [void]$testTokens.Add($mm.Groups[1].Value)
                }
            }
        }
        # Evaluate per story *block* (a story bullet may wrap across several physical lines, so
        # its test citation can land on a different line than the PST-US-id/done marker). Split on
        # the bullet boundary and check the whole block: a done story must cite >=1 real test
        # token somewhere in its block.
        $blocks = [regex]::Split($storyText, '(?m)(?=^\s*-\s+\*\*PST-US-)')
        $missingCited = @()
        foreach ($block in $blocks) {
            $idMatch = [regex]::Match($block, 'PST-US-[A-Za-z0-9]+')
            if (-not $idMatch.Success) { continue }
            # A "done" story has the check-mark on the SAME bullet as its id (header line).
            $headLine = ($block -split "\r?\n")[0]
            if ($headLine -notmatch '✅') { continue }
            $cited = [regex]::Matches($block, '`([A-Za-z_][A-Za-z0-9_]+)`') | ForEach-Object { $_.Groups[1].Value }
            # Only tokens that look like test method names (underscore-bearing) are candidates.
            $testish = @($cited | Where-Object { $_ -match '_' -and $_ -cne 'PSST_VIA' -and $_ -cne 'PSST_FROM_SCHEDULE' })
            if ($testish.Count -eq 0) { continue }
            $found = $false
            foreach ($t in $testish) { if ($testTokens.Contains($t)) { $found = $true; break } }
            if (-not $found) {
                $joined = ($testish -join ', ')
                $missingCited += "$($idMatch.Value): none of [$joined] found in test tree"
            }
        }
        if ($missingCited.Count -gt 0) {
            foreach ($mc in $missingCited) { Fail "story cites test not found — $mc" }
        } else {
            $tokCount = $testTokens.Count
            Pass "every done story with cited tests resolves against the test tree; $tokCount test methods scanned"
        }
    }

    # --- 6. Every code path cited in the bible exists ----------------------
    if (Test-Path $BiblePath) {
        $bibleText = $allText[$BiblePath]
        $paths = [regex]::Matches($bibleText, '`((?:MindAttic\.Psst[^`]*?)\.(?:cs|csproj|json|md))`') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
        $missingPaths = @()
        foreach ($p in $paths) {
            $full = Join-Path $RepoRoot ($p -replace '/', '\')
            if (-not (Test-Path $full)) { $missingPaths += $p }
        }
        if ($missingPaths.Count -gt 0) {
            foreach ($mp in $missingPaths) { Fail "bible cites missing path: $mp" }
        } else {
            Pass "$($paths.Count) code path(s) cited in bible all exist"
        }
    }

    # --- 7. generatedFrom artifacts not stale ------------------------------
    # The digest derives from BIBLE.md. If the bible is newer than the digest, the digest is stale.
    if (Test-Path $DigestPath) {
        if (Test-Path $BiblePath) {
            $bibleMtime  = (Get-Item $BiblePath).LastWriteTimeUtc
            $digestMtime = (Get-Item $DigestPath).LastWriteTimeUtc
            if ($bibleMtime -gt $digestMtime) {
                Fail 'BIBLE.digest.md is stale (BIBLE.md is newer) — run: codex.ps1 digest'
            } else {
                Pass 'BIBLE.digest.md is up to date with BIBLE.md'
            }
        }
    } else {
        Warn 'BIBLE.digest.md missing — run: codex.ps1 digest'
    }

    # --- Report ------------------------------------------------------------
    Write-Host ''
    Write-Host 'Codex doctor — MindAttic.Psst (PST)' -ForegroundColor Cyan
    Write-Host ('-' * 60)
    $checks | ForEach-Object {
        if ($_ -like '*[FAIL]*') { Write-Host $_ -ForegroundColor Red }
        elseif ($_ -like '*[WARN]*') { Write-Host $_ -ForegroundColor Yellow }
        else { Write-Host $_ -ForegroundColor Green }
    }
    Write-Host ('-' * 60)
    if ($warnings.Count -gt 0) { Write-Host "$($warnings.Count) warning(s)." -ForegroundColor Yellow }
    if ($errors.Count -gt 0) {
        Write-Host "FAILED: $($errors.Count) hard error(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host 'OK: no hard errors.' -ForegroundColor Green
    exit 0
}

# ===========================================================================
# DIGEST
# ===========================================================================
function Get-Section {
    param([string]$text, [int]$number)
    # Capture from "## <number>. " up to the next "## " (or EOF).
    $pattern = "(?ms)^##\s+$number\.\s+.*?(?=^##\s+\d+\.|\z)"
    $m = [regex]::Match($text, $pattern)
    if ($m.Success) { return $m.Value.TrimEnd() }
    return ''
}

function Invoke-Digest {
    if (-not (Test-Path $BiblePath)) { throw "BIBLE.md not found at $BiblePath" }
    $bible = Read-Text $BiblePath

    $s1 = Get-Section $bible 1
    $s3 = Get-Section $bible 3
    $s5 = Get-Section $bible 5
    $s9 = Get-Section $bible 9

    # Status index from USER_STORIES.md.
    $done = 0; $partial = 0; $planned = 0; $cut = 0
    if (Test-Path $StoriesPath) {
        $st = Read-Text $StoriesPath
        # Count glyph occurrences by literal substring (avoids regex escape pitfalls with
        # surrogate-pair emoji like the partial/cut markers).
        function Count-Sub([string]$h, [string]$n) {
            if ([string]::IsNullOrEmpty($n)) { return 0 }
            $c = 0; $i = 0
            while (($i = $h.IndexOf($n, $i)) -ge 0) { $c++; $i += $n.Length }
            return $c
        }
        $done    = Count-Sub $st ([char]0x2705)                       # white heavy check mark
        $partial = Count-Sub $st ([string]([char]0xD83D) + [char]0xDFE1) # large yellow circle
        $planned = Count-Sub $st ([char]0x2B1C)                       # white large square
        $cut     = Count-Sub $st ([string]([char]0xD83D) + [char]0xDDD1) # wastebasket
    }

    # Latest amendment head.
    $amendHead = ''
    if (Test-Path $AmendmentsPath) {
        $am = Read-Text $AmendmentsPath
        $am = $am -replace "(?s)^---.*?---\r?\n", ''
        $m = [regex]::Match($am, '(?ms)^##\s+PST-A\d+.*?(?=^##\s+PST-A\d+|\z)')
        if ($m.Success) { $amendHead = $m.Value.TrimEnd() }
    }

    $today = (Get-Date).ToString('yyyy-MM-dd')
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('codex: 1')
    [void]$sb.AppendLine('project: MindAttic.Psst')
    [void]$sb.AppendLine('code: PST')
    [void]$sb.AppendLine('layer: digest')
    [void]$sb.AppendLine('status: living')
    [void]$sb.AppendLine("updated: $today")
    [void]$sb.AppendLine('generatedFrom: docs/BIBLE.md')
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('# MindAttic.Psst — Bible Digest')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('> AUTHORITATIVE — full detail in docs/BIBLE.md. This digest is GENERATED by')
    [void]$sb.AppendLine('> tools/codex.ps1; do not hand-edit. It distills BIBLE.md sections 1, 3, 5, 9.')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine($s1)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine($s3)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine($s5)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine($s9)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Status index')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("- done: $done   partial: $partial   planned: $planned   cut: $cut")
    [void]$sb.AppendLine('- (counts are glyph occurrences in docs/USER_STORIES.md)')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Latest amendment')
    [void]$sb.AppendLine('')
    if ($amendHead) { [void]$sb.AppendLine($amendHead) } else { [void]$sb.AppendLine('(none)') }
    [void]$sb.AppendLine('')

    # Write UTF-8 without BOM.
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($DigestPath, $sb.ToString(), $enc)
    Write-Host "Wrote $((Resolve-Path $DigestPath -Relative))" -ForegroundColor Green
    Write-Host "  status index: done=$done partial=$partial planned=$planned cut=$cut"
}

# ---------------------------------------------------------------------------
switch ($Command) {
    'doctor' { Invoke-Doctor }
    'digest' { Invoke-Digest }
}
