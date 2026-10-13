param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$testRunRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ('.test-sandbox\' + [Guid]::NewGuid().ToString('N'))))
if (-not $testRunRoot.StartsWith($projectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test directory is outside the project.'
}
New-Item -ItemType Directory -Path $testRunRoot -Force | Out-Null
$previousTestTemp = $env:TEMP
$previousTestTmp = $env:TMP
$previousTestRoot = $env:CLEANER_TEST_ROOT
try {
    $env:TEMP = $testRunRoot
    $env:TMP = $testRunRoot
    $env:CLEANER_TEST_ROOT = $testRunRoot
    Write-Output ('Isolated test Temp: ' + $testRunRoot)
    $testArguments = @('test', (Join-Path $projectRoot 'Cleaner.slnx'), '--no-restore', '--nologo', '-m:1',
        '--settings', (Join-Path $projectRoot 'isolated.runsettings'), '--filter', 'Category!=WindowsIntegration',
        '--results-directory', (Join-Path $testRunRoot 'results'), '--logger', 'trx')
    if ($NoBuild) { $testArguments += '--no-build' }
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
    exit $testExitCode
}
finally {
    $env:TEMP = $previousTestTemp
    $env:TMP = $previousTestTmp
    $env:CLEANER_TEST_ROOT = $previousTestRoot
}
