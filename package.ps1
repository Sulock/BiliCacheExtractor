# SPDX-License-Identifier: GPL-3.0-only
param([string]$Version = '1.3.0', [string]$Commit = 'HEAD', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch.' }
if (-not $OutputDirectory) { $OutputDirectory = 'release-v' + $Version }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $packageRoot $OutputDirectory }
$packageOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $packageOutput.StartsWith($packageRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output directory must be inside the project directory.'
}
$packageCommit = (& git -C $packageRoot rev-parse --verify ($Commit + '^{commit}')).Trim()
if ($LASTEXITCODE -ne 0 -or $packageCommit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve the source commit.' }
$packageName = 'BiliCacheExtractor-v' + $Version
$sourceName = $packageName + '-source.zip'
$portableName = $packageName + '-win-x64.zip'
$sourceZip = Join-Path $packageOutput $sourceName
$portableZip = Join-Path $packageOutput $portableName
$checksumPath = Join-Path $packageOutput 'SHA256SUMS.txt'
foreach ($outputFile in @($sourceZip, $portableZip, $checksumPath)) {
    if (Test-Path -LiteralPath $outputFile) { throw 'Output artifacts already exist. Choose another output directory to preserve them.' }
}
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
$stage = Join-Path $packageOutput ('.stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
& git -C $packageRoot -c core.autocrlf=false -c core.eol=lf archive --format=zip ('--prefix=' + $packageName + '-source/') ('--output=' + $sourceZip) $packageCommit
if ($LASTEXITCODE -ne 0) { throw 'Source archive creation failed.' }
Expand-Archive -LiteralPath $sourceZip -DestinationPath $stage
$sourceRoot = Join-Path $stage ($packageName + '-source')
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
    'LICENSE', 'THIRD_PARTY_NOTICES.md', 'Install-FFmpeg.ps1', 'Install-FFmpeg.cmd'
)
foreach ($portableFile in $portableFiles) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $portableFile) -Destination (Join-Path $portableRoot $portableFile)
}
foreach ($toolFile in @('README.md', 'ffmpeg.lock.json')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot ('tools\' + $toolFile)) -Destination (Join-Path $portableRoot ('tools\' + $toolFile))
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'assets') -Destination $portableRoot -Recurse
Copy-Item -LiteralPath $sourceZip -Destination (Join-Path $portableRoot $sourceName)
$buildInfo = @{
    version = $Version
    commit = $packageCommit
    architecture = 'win-x64'
    sourceArchive = $sourceName
    sourceSha256 = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
    ffmpeg = 'Installed separately from the upstream build using Install-FFmpeg.cmd.'
    ffmpegLockSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'tools\ffmpeg.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $portableRoot 'BUILD-INFO.json'), $buildInfo, (New-Object Text.UTF8Encoding($false)))
foreach ($binary in @('BiliCacheExtractor.exe', 'BiliCacheExtractor.Cli.exe')) {
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $portableRoot $binary)).Version.ToString() -ne ($Version + '.0')) {
        throw ('Unexpected version in ' + $binary)
    }
}
Compress-Archive -LiteralPath $portableRoot -DestinationPath $portableZip -CompressionLevel Optimal
$checksumLines = foreach ($artifact in @($portableZip, $sourceZip)) {
    (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($artifact)
}
[IO.File]::WriteAllText($checksumPath, (($checksumLines -join "`n") + "`n"), [Text.Encoding]::ASCII)
[pscustomobject]@{ version = $Version; commit = $packageCommit; portable = $portableZip; source = $sourceZip; checksums = $checksumPath } | ConvertTo-Json
