# SPDX-License-Identifier: GPL-3.0-only
param([string]$Version = '1.3.1', [string]$Commit = 'HEAD', [string]$OutputDirectory, [switch]$LegacySetup,
    [string]$FFmpegDirectory, [Parameter(Mandatory=$true)][string]$FFmpegSourceArchive)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch.' }
$releaseVersion = if ($LegacySetup) { $Version + '-legacy' } else { $Version }
if (-not $OutputDirectory) { $OutputDirectory = 'release-v' + $releaseVersion }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $packageRoot $OutputDirectory }
$packageOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $packageOutput.StartsWith($packageRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output directory must be inside the project directory.'
}
$packageCommit = (& git -C $packageRoot rev-parse --verify ($Commit + '^{commit}')).Trim()
if ($LASTEXITCODE -ne 0 -or $packageCommit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve the source commit.' }
$packageName = 'BiliCacheExtractor-v' + $releaseVersion
$sourceName = $packageName + '-source.zip'
$portableName = $packageName + '-win-x64.zip'
$ffmpegSourceName = 'FFmpeg-N-128122-source-build-kit.zip'
$sourceZip = Join-Path $packageOutput $sourceName
$portableZip = Join-Path $packageOutput $portableName
$ffmpegSourceZip = Join-Path $packageOutput $ffmpegSourceName
$checksumPath = Join-Path $packageOutput 'SHA256SUMS.txt'
foreach ($outputFile in @($sourceZip, $portableZip, $ffmpegSourceZip, $checksumPath)) {
    if (Test-Path -LiteralPath $outputFile) { throw 'Output artifacts already exist. Choose another output directory to preserve them.' }
}
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
$stage = Join-Path $packageOutput ('.stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
& git -C $packageRoot -c core.autocrlf=false -c core.eol=lf archive --format=zip ('--prefix=' + $packageName + '-source/') ('--output=' + $sourceZip) $packageCommit
if ($LASTEXITCODE -ne 0) { throw 'Source archive creation failed.' }
Expand-Archive -LiteralPath $sourceZip -DestinationPath $stage
$sourceRoot = Join-Path $stage ($packageName + '-source')
$bundleLock = Get-Content -LiteralPath (Join-Path $sourceRoot 'tools\ffmpeg-bundle.lock.json') -Raw | ConvertFrom-Json
if (-not $FFmpegDirectory) { $FFmpegDirectory = Join-Path $packageRoot 'ffmpeg-master-latest-win64-gpl-shared' }
$FFmpegDirectory = (Resolve-Path -LiteralPath $FFmpegDirectory).Path
$FFmpegSourceArchive = (Resolve-Path -LiteralPath $FFmpegSourceArchive).Path
if ((Get-FileHash -LiteralPath $FFmpegSourceArchive -Algorithm SHA256).Hash -ne $bundleLock.sourceArchiveSha256) { throw 'FFmpeg source archive checksum differs from the pinned source.' }
if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot $bundleLock.buildRecipes) -Algorithm SHA256).Hash -ne $bundleLock.buildRecipesSha256) { throw 'FFmpeg build recipe archive checksum differs.' }
$sourceProgram = Get-Content -LiteralPath (Join-Path $sourceRoot 'src\Program.cs') -Raw
$sourceVersion = [regex]::Match($sourceProgram, 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
if ($sourceVersion -ne $Version) { throw 'The requested version differs from the committed application version.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $sourceRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Building the committed source failed.' }
$portableRoot = Join-Path $stage ($packageName + '-win-x64')
New-Item -ItemType Directory -Path (Join-Path $portableRoot 'tools') -Force | Out-Null
$portableFiles = @(
    'BiliCacheExtractor.exe', 'BiliCacheExtractor.Cli.exe',
    'README.md', 'README.zh-CN.md', 'README.zh-TW.md', 'START-HERE.txt',
    'LICENSE', 'THIRD_PARTY_NOTICES.md', 'LEGACY-FFMPEG.md'
)
if ($LegacySetup) { $portableFiles += @('Install-Legacy-FFmpeg.cmd', 'Install-Legacy-FFmpeg.ps1') }
foreach ($portableFile in $portableFiles) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $portableFile) -Destination (Join-Path $portableRoot $portableFile)
}
foreach ($toolFile in @('README.md', 'ffmpeg.lock.json', 'ffmpeg-bundle.lock.json', 'ffmpeg-compat.lock.json')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot ('tools\' + $toolFile)) -Destination (Join-Path $portableRoot ('tools\' + $toolFile))
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'assets') -Destination $portableRoot -Recurse
Copy-Item -LiteralPath (Join-Path $sourceRoot 'third_party') -Destination $portableRoot -Recurse
Copy-Item -LiteralPath $sourceZip -Destination (Join-Path $portableRoot $sourceName)
if ($LegacySetup) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'START-LEGACY.txt') -Destination (Join-Path $portableRoot 'START-HERE.txt') -Force
}
foreach ($runtimeFile in $bundleLock.runtimeFiles) {
    $inputPath = [IO.Path]::GetFullPath((Join-Path $FFmpegDirectory $runtimeFile.path))
    if (-not $inputPath.StartsWith($FFmpegDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'FFmpeg runtime file escapes its input directory.' }
    if ((Get-Item -LiteralPath $inputPath).Length -ne $runtimeFile.bytes -or (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ne $runtimeFile.sha256) { throw ('FFmpeg runtime checksum differs: ' + $runtimeFile.path) }
    $outputPath = Join-Path (Join-Path $portableRoot 'tools\ffmpeg') $runtimeFile.path
    New-Item -ItemType Directory -Path (Split-Path $outputPath -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $inputPath -Destination $outputPath
}
$ffmpegSourceRoot = Join-Path $stage 'FFmpeg-N-128122-source-build-kit'
New-Item -ItemType Directory -Path $ffmpegSourceRoot | Out-Null
Copy-Item -LiteralPath $FFmpegSourceArchive -Destination (Join-Path $ffmpegSourceRoot 'FFmpeg-215351c1c3-source.tar.gz')
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'third_party') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $ffmpegSourceRoot }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools\ffmpeg-bundle.lock.json') -Destination $ffmpegSourceRoot
Compress-Archive -LiteralPath $ffmpegSourceRoot -DestinationPath $ffmpegSourceZip -CompressionLevel Optimal
$ffmpegSourceInfo = @(
    ('FFmpeg ' + $bundleLock.version)
    ('License: ' + $bundleLock.license)
    ('FFmpeg source commit: ' + $bundleLock.sourceCommit)
    ('Source and build kit: https://github.com/Sulock/BiliCacheExtractor/releases/download/v' + $releaseVersion + '/' + $ffmpegSourceName)
    'Dependency source repositories and revisions: ../../third_party/ffmpeg-dependencies.json'
    'Build instructions: ../../third_party/FFMPEG-SOURCES.md'
) -join "`r`n"
[IO.File]::WriteAllText((Join-Path $portableRoot 'tools\ffmpeg\SOURCE-INFO.txt'), $ffmpegSourceInfo, (New-Object Text.UTF8Encoding($false)))
$buildInfo = @{
    version = $Version
    releaseTag = 'v' + $releaseVersion
    variant = $(if ($LegacySetup) { 'legacy-driver-setup' } else { 'standard' })
    commit = $packageCommit
    architecture = 'win-x64'
    sourceArchive = $sourceName
    sourceSha256 = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
    ffmpeg = $bundleLock.version
    ffmpegSourceKit = $ffmpegSourceName
    ffmpegLockSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'tools\ffmpeg-bundle.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    compatibilitySetup = $(if ($LegacySetup) { 'Install-Legacy-FFmpeg.cmd' } else { $null })
    compatibilityLockSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'tools\ffmpeg-compat.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $portableRoot 'BUILD-INFO.json'), $buildInfo, (New-Object Text.UTF8Encoding($false)))
foreach ($binary in @('BiliCacheExtractor.exe', 'BiliCacheExtractor.Cli.exe')) {
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $portableRoot $binary)).Version.ToString() -ne ($Version + '.0')) {
        throw ('Unexpected version in ' + $binary)
    }
}
Compress-Archive -LiteralPath $portableRoot -DestinationPath $portableZip -CompressionLevel Optimal
$checksumLines = foreach ($artifact in @($portableZip, $sourceZip, $ffmpegSourceZip)) {
    (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($artifact)
}
[IO.File]::WriteAllText($checksumPath, (($checksumLines -join "`n") + "`n"), [Text.Encoding]::ASCII)
[pscustomobject]@{ version = $Version; commit = $packageCommit; portable = $portableZip; source = $sourceZip; ffmpegSourceKit = $ffmpegSourceZip; checksums = $checksumPath } | ConvertTo-Json
