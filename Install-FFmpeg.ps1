# SPDX-License-Identifier: GPL-3.0-only
param([string]$ArchivePath, [switch]$Download)
$ErrorActionPreference='Stop'
$application=Join-Path $PSScriptRoot 'BiliCacheExtractor.exe'
$cli=Join-Path $PSScriptRoot 'BiliCacheExtractor.Cli.exe'
if (-not (Test-Path -LiteralPath $application) -or -not (Test-Path -LiteralPath $cli)) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($ArchivePath) {
    & $cli --install-ffmpeg (Resolve-Path -LiteralPath $ArchivePath).Path
    exit $LASTEXITCODE
}
if ($Download) {
    & $cli --install-ffmpeg
    exit $LASTEXITCODE
}
# This is the user-facing interactive configuration window.
$process=Start-Process -FilePath $application -ArgumentList '--configure-ffmpeg' -PassThru -Wait
exit $process.ExitCode
