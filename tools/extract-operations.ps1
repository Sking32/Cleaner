#Requires -Version 5.1
<#
.SYNOPSIS
    Extracts cleanup operation metadata from Cleanup.ps1.
.PARAMETER SourcePath
    Folder with Cleanup.ps1.
.PARAMETER OutputDir
    Output folder (default = script folder).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$srcFile = Join-Path $SourcePath 'Cleanup.ps1'
if (-not (Test-Path -LiteralPath $srcFile)) { throw "File not found: $srcFile" }
if (-not (Test-Path -LiteralPath $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

Write-Host "Reading $srcFile ..." -ForegroundColor Cyan
$raw = Get-Content -LiteralPath $srcFile -Raw -Encoding UTF8

# ---------- 1. Sections and operations from Add-Section / Add-Op calls ----------
$sequence = @()
foreach ($m in [regex]::Matches($raw, "(?m)^\s*Add-Section\s+'([^']+)'\s+'([^']+)'\s+'([^']+)'")) {
    $sequence += [ordered]@{
        kind    = 'section'
        key     = $m.Groups[1].Value
        ru      = $m.Groups[2].Value
        en      = $m.Groups[3].Value
    }
}

# Add-Op is on single line: Add-Op 'key' -Checked $true -Level 'safe'
foreach ($m in [regex]::Matches($raw, "(?m)^\s*Add-Op\s+'([^']+)'([^\r\n]*)")) {
    $key = $m.Groups[1].Value
    $rest = $m.Groups[2].Value

    $checked = $false
    if ($rest -match '-Checked\s+\`$true') { $checked = $true }

    $level = 'safe'
    $lm = [regex]::Match($rest, "-Level\s+'([^']+)'")
    if ($lm.Success) { $level = $lm.Groups[1].Value }

    $hint = ''
    $hm = [regex]::Match($rest, "-Hint\s+'([^']+)'")
    if ($hm.Success) { $hint = $hm.Groups[1].Value }

    $sequence += [ordered]@{
        kind           = 'operation'
        key            = $key
        checked        = $checked
        level          = $level
        hint           = $hint
    }
}

Write-Host "Sequence entries: $($sequence.Count)" -ForegroundColor Gray

# ---------- 2. Text block $script:OpText = @{ ... } ----------
# Each entry looks like:
#   'temp_user'    = @{ ru=@{ l='...'; d="..." }
#                       en=@{ l='...'; d="..." } }
# We parse field-by-field per key.
$textStart = $raw.IndexOf('$script:OpText = @{')
if ($textStart -lt 0) { throw "Cannot find `$script:OpText block" }

# Find matching closing brace of the outer hashtable
function Get-MatchingBrace {
    param([string]$Text, [int]$OpenPos)
    $depth = 0
    $inStr = $false
    $strCh = ''
    for ($i = $OpenPos; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($inStr) {
            if ($c -eq $strCh) { $inStr = $false }
            elseif ($c -eq '\') { $i++ }  # skip escaped char
            continue
        }
        if ($c -eq '"' -or $c -eq "'") { $inStr = $true; $strCh = $c; continue }
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth--; if ($depth -eq 0) { return $i } }
    }
    return -1
}

$braceOpen  = $textStart + '$script:OpText = '.Length
$braceClose = Get-MatchingBrace -Text $raw -OpenPos $braceOpen
$textBlock  = $raw.Substring($braceOpen + 1, $braceClose - $braceOpen - 1)

# Now split into per-key chunks:  'key' = @{ ... }
$stringsRu = [ordered]@{}
$stringsEn = [ordered]@{}
$keyChunks = [regex]::Matches($textBlock, "(?s)'([a-z_0-9]+)'\s*=\s*@\{")

for ($i = 0; $i -lt $keyChunks.Count; $i++) {
    $start = $keyChunks[$i].Index + $keyChunks[$i].Length - 1
    # find matching brace for this inner block
    $depth = 0
    $end = -1
    for ($j = $start; $j -lt $textBlock.Length; $j++) {
        $c = $textBlock[$j]
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth--; if ($depth -eq 0) { $end = $j; break } }
    }
    if ($end -lt 0) { continue }
    $chunk = $textBlock.Substring($start + 1, $end - $start - 1)
    $opKey = $keyChunks[$i].Groups[1].Value

    # ru block
    $ruMatch = [regex]::Match($chunk, "(?s)ru\s*=\s*@\{\s*l\s*=\s*'([^']*)'\s*;\s*d\s*=\s*""((?:[^""\\]|\\.)*)""\s*\}")
    if ($ruMatch.Success) {
        $stringsRu["op.$opKey.name"] = $ruMatch.Groups[1].Value
        $stringsRu["op.$opKey.desc"] = $ruMatch.Groups[2].Value -replace '\\n', "`n" -replace '\\`n', "`n"
    }

    $enMatch = [regex]::Match($chunk, "(?s)en\s*=\s*@\{\s*l\s*=\s*'([^']*)'\s*;\s*d\s*=\s*""((?:[^""\\]|\\.)*)""\s*\}")
    if ($enMatch.Success) {
        $stringsEn["op.$opKey.name"] = $enMatch.Groups[1].Value
        $stringsEn["op.$opKey.desc"] = $enMatch.Groups[2].Value -replace '\\n', "`n" -replace '\\`n', "`n"
    }
}

Write-Host "Strings RU: $($stringsRu.Count)" -ForegroundColor Gray
Write-Host "Strings EN: $($stringsEn.Count)" -ForegroundColor Gray

# ---------- 3. Build operations-meta.json ----------
$ops = @()
foreach ($entry in $sequence) {
    if ($entry.kind -eq 'section') {
        $ops += [ordered]@{
            kind    = 'section'
            key     = $entry.key
            nameKey = "section.$($entry.key).name"
        }
        $stringsRu["section.$($entry.key).name"] = $entry.ru
        $stringsEn["section.$($entry.key).name"] = $entry.en
    } else {
        $ops += [ordered]@{
            kind           = 'operation'
            key            = $entry.key
            category       = ''    # will be filled by tool later, or we leave blank
            level          = $entry.level
            defaultChecked = $entry.checked
            hint           = $entry.hint
            handler        = ''    # to fill in next step
            paths          = @()
            nameKey        = "op.$($entry.key).name"
            descKey        = "op.$($entry.key).desc"
        }
    }
}

$metaPath = Join-Path $OutputDir 'operations-meta.json'
$ruPath   = Join-Path $OutputDir 'strings-ops-ru.json'
$enPath   = Join-Path $OutputDir 'strings-ops-en.json'

[ordered]@{
    schemaVersion = 1
    entries       = $ops
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metaPath -Encoding UTF8

$stringsRu | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ruPath -Encoding UTF8
$stringsEn | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $enPath -Encoding UTF8

Write-Host ""
Write-Host "Done:" -ForegroundColor Cyan
Write-Host "  $metaPath"
Write-Host "  $ruPath ($($stringsRu.Count) strings)"
Write-Host "  $enPath ($($stringsEn.Count) strings)"