#Requires -Version 5.1
<#
.SYNOPSIS
    Извлекает метаданные твиков из Optimize-Defs.ps1.
.DESCRIPTION
    Парсит Key/Cat/Level/Name/Short/Desc/Warn для всех блоков $defs += @{ }.
    Генерирует:
      - tweaks-meta.json     — структура (key, category, level, ...)
      - strings-ru.json      — тексты на русском
      - strings-en.json      — тексты на английском
      - unparsed-actions.txt — сырые блоки Apply/Unapply/GetState для ручного переноса
.PARAMETER SourcePath
    Путь к папке с Optimize-Defs.ps1
.PARAMETER OutputDir
    Куда положить результат (по умолчанию — папка скрипта)
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$srcFile = Join-Path $SourcePath 'Optimize-Defs.ps1'
if (-not (Test-Path -LiteralPath $srcFile)) {
    throw "Файл не найден: $srcFile"
}

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "Читаю $srcFile..." -ForegroundColor Cyan
$raw = Get-Content -LiteralPath $srcFile -Raw -Encoding UTF8

# --- Найти все стартовые позиции блоков $defs += @{
$pattern = '(?m)^\s*\$defs\s*\+=\s*@\{'
$starts = [regex]::Matches($raw, $pattern)
Write-Host "Найдено блоков: $($starts.Count)" -ForegroundColor Gray
if ($starts.Count -eq 0) { throw "Не найдено ни одного блока" }

# --- Хелпер: найти парную закрывающую скобку
function Get-MatchingBrace {
    param([string]$Text, [int]$OpenPos)
    $depth = 0
    for ($i = $OpenPos; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }
    return -1
}

# --- Хелпер: извлечь значение после Name/Short/Desc/Warn в формате if ($isEn) { en } else { ru }
function Get-CondValue {
    param([string]$Block, [string]$FieldName)
    $rx = [regex]("(?s)" + [regex]::Escape($FieldName) + "\s*=\s*if\s*\(\`$isEn\)\s*\{\s*'(.*?)'\s*\}\s*else\s*\{\s*'(.*?)'\s*\}")
    $m = $rx.Match($Block)
    if (-not $m.Success) { return @{ En = ''; Ru = '' } }
    return @{
        En = $m.Groups[1].Value -replace "''", "'"
        Ru = $m.Groups[2].Value -replace "''", "'"
    }
}

function Get-SimpleValue {
    param([string]$Block, [string]$FieldName)
    $rx = [regex]("(?m)^\s*" + [regex]::Escape($FieldName) + "\s*=\s*'([^']+)'")
    $m = $rx.Match($Block)
    if ($m.Success) { return $m.Groups[1].Value }
    return ''
}

# --- Пройти по всем блокам
$tweaks   = @()
$stringsRu = @{}
$stringsEn = @{}
$unparsed  = New-Object System.Text.StringBuilder

for ($i = 0; $i -lt $starts.Count; $i++) {
    $startMatch = $starts[$i]
    $openBracePos = $startMatch.Index + $startMatch.Length - 1
    $closeBracePos = Get-MatchingBrace -Text $raw -OpenPos $openBracePos
    if ($closeBracePos -lt 0) {
        Write-Warning "Не найдена закрывающая скобка для блока #$i"
        continue
    }
    $block = $raw.Substring($openBracePos + 1, $closeBracePos - $openBracePos - 1)

    $key = Get-SimpleValue $block 'Key'
    if (-not $key) { continue }

    $cat   = Get-SimpleValue $block 'Cat'
    $level = Get-SimpleValue $block 'Level'

    $nameV  = Get-CondValue $block 'Name'
    $shortV = Get-CondValue $block 'Short'
    $descV  = Get-CondValue $block 'Desc'
    $warnV  = Get-CondValue $block 'Warn'

    $nameKey  = "tweak.$key.name"
    $shortKey = "tweak.$key.short"
    $descKey  = "tweak.$key.desc"
    $warnKey  = "tweak.$key.warn"

    $stringsRu[$nameKey]  = $nameV.Ru
    $stringsEn[$nameKey]  = $nameV.En
    $stringsRu[$shortKey] = $shortV.Ru
    $stringsEn[$shortKey] = $shortV.En
    $stringsRu[$descKey]  = $descV.Ru
    $stringsEn[$descKey]  = $descV.En
    $stringsRu[$warnKey]  = $warnV.Ru
    $stringsEn[$warnKey]  = $warnV.En

    $tweaks += [ordered]@{
        key             = $key
        category        = $cat
        level           = $level
        requiresReboot  = $false
        conflictsWith   = @()
        nameKey         = $nameKey
        shortKey        = $shortKey
        descKey         = $descKey
        warnKey         = $warnKey
        apply           = @()
        unapply         = @()
        state           = @()
    }

    # --- Сырой блок для ручного разбора
    [void]$unparsed.AppendLine("=" * 70)
    [void]$unparsed.AppendLine("TWEAK: $key  (Cat=$cat, Level=$level)")
    [void]$unparsed.AppendLine("=" * 70)
    [void]$unparsed.AppendLine($block.Trim())
    [void]$unparsed.AppendLine("")

    Write-Host "  [OK] $key" -ForegroundColor Green
}

# --- Записать файлы
$metaPath = Join-Path $OutputDir 'tweaks-meta.json'
$ruPath   = Join-Path $OutputDir 'strings-ru.json'
$enPath   = Join-Path $OutputDir 'strings-en.json'
$unPath   = Join-Path $OutputDir 'unparsed-actions.txt'

$metaDoc = [ordered]@{
    schemaVersion = 1
    tweaks = $tweaks
}

$metaDoc | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metaPath -Encoding UTF8
$stringsRu | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ruPath -Encoding UTF8
$stringsEn | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $enPath -Encoding UTF8
$unparsed.ToString() | Set-Content -LiteralPath $unPath -Encoding UTF8

Write-Host ""
Write-Host "Готово:" -ForegroundColor Cyan
Write-Host "  $metaPath ($($tweaks.Count) твиков)"
Write-Host "  $ruPath ($($stringsRu.Count) строк)"
Write-Host "  $enPath ($($stringsEn.Count) строк)"
Write-Host "  $unPath"