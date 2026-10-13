param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
[xml]$versionDocument = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw -Encoding UTF8
$version = [string]$versionDocument.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid application version.' }
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts\preview\' + $version + '-' + $Runtime + '-' + [Guid]::NewGuid().ToString('N'))))
if (-not $outputRoot.StartsWith($projectRoot.TrimEnd('\') + '\artifacts\preview\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected output path.' }
for ($ancestor = $outputRoot; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Packaging may not use junctions or symbolic links.'
    }
}
$packageDirectory = Join-Path $outputRoot ('Cleaner-' + $version + '-' + $Runtime)
New-Item -ItemType Directory -Path $packageDirectory | Out-Null
$publishArguments = @('publish', (Join-Path $projectRoot 'Cleaner.App\Cleaner.App.csproj'), '-c', 'Release', '-r', $Runtime,
    '--self-contained', 'true', '-o', $packageDirectory, '--nologo', '-m:1',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishTrimmed=false',
    '-p:DebugType=None', '-p:DebugSymbols=false')
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; no distributable archive was created.' }
foreach ($required in @('Cleaner.App.exe', 'Data\operations.json', 'Data\tweaks.json', 'Data\presets.json', 'Data\releases.json', 'Data\localization\ru.json', 'Data\localization\en.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $required) -PathType Leaf)) { throw ('Missing packaged file: ' + $required) }
}
# Only reviewed source documentation is copied. No profile data, settings, quarantine or logs are bundled.
foreach ($document in @('FRIEND_TESTING.md', 'RELEASE_GUIDE.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $packageDirectory
}
if (Test-Path -LiteralPath (Join-Path $projectRoot 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory }
# Include license metadata and the license/notice files shipped by NuGet packages and the .NET runtime.
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'Cleaner.App\obj\project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$libraries = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Value.path })
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($download in $framework.Value.downloadDependencies) {
        $packVersion = ($download.version -replace '[\[\] ]', '').Split(',')[0]
        $libraries += $download.name.ToLowerInvariant() + '/' + $packVersion
    }
}
$notices = [Collections.Generic.List[string]]::new()
$notices.Add('# Third-party packages and runtime notices')
foreach ($library in ($libraries | Sort-Object -Unique)) {
    $packagePath = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = [IO.Path]::GetFullPath((Join-Path $folder $library))
        if (-not $candidate.StartsWith([IO.Path]::GetFullPath($folder).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected NuGet package path.' }
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packagePath = $candidate; break }
    }
    if (-not $packagePath) { throw ('Package license source missing: ' + $library) }
    $spec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File | Select-Object -First 1
    if (-not $spec) { throw ('NuGet metadata missing: ' + $library) }
    [xml]$metadata = Get-Content -LiteralPath $spec.FullName -Raw -Encoding UTF8
    $node = $metadata.SelectSingleNode('//*[local-name()="metadata"]')
    $license = $node.SelectSingleNode('*[local-name()="license"]')
    $licenseUrl = $node.SelectSingleNode('*[local-name()="licenseUrl"]')
    $copyright = $node.SelectSingleNode('*[local-name()="copyright"]')
    $notices.Add('')
    $notices.Add('## ' + $library)
    if ($copyright) { $notices.Add($copyright.InnerText) }
    if ($license) { $notices.Add('License: ' + $license.InnerText) }
    if ($licenseUrl) { $notices.Add('License URL: ' + $licenseUrl.InnerText) }
    $licenseDirectory = Join-Path $packageDirectory ('Licenses\' + ($library -replace '/', '-'))
    New-Item -ItemType Directory -Path $licenseDirectory | Out-Null
    foreach ($source in (Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '(?i)license|notice' })) {
        if ($source.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'License sources may not be symbolic links.' }
        Copy-Item -LiteralPath $source.FullName -Destination $licenseDirectory
    }
    # MIT expressions sometimes have no LICENSE file in the package. Preserve its copyright alongside the full permission text.
    if ($license -and $license.InnerText -eq 'MIT' -and -not (Get-ChildItem -LiteralPath $licenseDirectory -File | Where-Object { $_.Name -match '(?i)license' })) {
        $runtimeSource = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $runtimeSource = Get-ChildItem -LiteralPath (Join-Path $folder ('microsoft.netcore.app.runtime.' + $Runtime)) -Filter LICENSE.TXT -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($runtimeSource) { break }
        }
        if (-not $runtimeSource) { throw 'MIT runtime license text missing.' }
        $fullText = [IO.File]::ReadAllText($runtimeSource.FullName)
        $permission = $fullText.IndexOf('Permission is hereby granted', [StringComparison]::Ordinal)
        if ($permission -lt 0) { throw 'Unexpected MIT license text.' }
        [IO.File]::WriteAllText((Join-Path $licenseDirectory 'LICENSE-MIT.txt'), ($copyright.InnerText + [Environment]::NewLine + [Environment]::NewLine + $fullText.Substring($permission)))
    }
}
[IO.File]::WriteAllLines((Join-Path $packageDirectory 'THIRD_PARTY_NOTICES.md'), $notices)
$zipPath = Join-Path $outputRoot ('Cleaner-' + $version + '-' + $Runtime + '.zip')
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText($zipPath + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($zipPath) + [Environment]::NewLine)
Write-Output ('Package: ' + $zipPath)
Write-Output ('SHA256: ' + $hash)
Write-Output 'Extract the entire archive before starting Cleaner.App.exe. Data must remain next to the EXE.'
