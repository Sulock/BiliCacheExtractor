# SPDX-License-Identifier: GPL-3.0-only
param([Parameter(Mandatory=$true)][string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$pin = [IO.File]::ReadAllText((Join-Path $root 'tools\ffmpeg-compat.lock.json')) | ConvertFrom-Json
$testRoot = Join-Path $root ('verification\legacy-setup-' + [Guid]::NewGuid().ToString('N'))
function New-Fixture([string]$Name) {
    $path = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $path 'tools') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'Install-Legacy-FFmpeg.ps1') -Destination $path
    Copy-Item -LiteralPath (Join-Path $root 'tools\ffmpeg-compat.lock.json') -Destination (Join-Path $path 'tools')
    return $path
}
function Run-Setup([string]$Path, [string]$InputArchive) {
    # Windows PowerShell wraps native stderr as errors, including expected failures.
    $ErrorActionPreference = 'Continue'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Path 'Install-Legacy-FFmpeg.ps1') -ArchivePath $InputArchive *> (Join-Path $Path 'setup.log')
    return $LASTEXITCODE
}
$good = New-Fixture 'valid'
if ((Run-Setup $good $archive) -ne 0) { throw 'Verified archive did not install.' }
$installed = Join-Path $good 'tools\ffmpeg-compat.exe'
if ((Get-FileHash -LiteralPath $installed).Hash -ne $pin.executableSha256) { throw 'Installed executable hash differs.' }
'PASS: verified archive installs the exact compatibility executable'
if ((Run-Setup $good (Join-Path $testRoot 'does-not-exist.whl')) -ne 0) { throw 'Repeat installation tried to use an archive.' }
'PASS: repeat installation works offline'
$bad = New-Fixture 'corrupt'
$corrupt = Join-Path $bad 'corrupt.whl'
Copy-Item -LiteralPath $archive -Destination $corrupt
$stream = [IO.File]::Open($corrupt, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
try { $value = $stream.ReadByte(); $stream.Position = 0; $stream.WriteByte($value -bxor 255) } finally { $stream.Dispose() }
if ((Run-Setup $bad $corrupt) -eq 0 -or (Test-Path -LiteralPath (Join-Path $bad 'tools\ffmpeg-compat.exe'))) { throw 'Same-size corrupt archive was accepted.' }
'PASS: same-size archive tampering is rejected before installation'
$existing = New-Fixture 'existing'
$existingExe = Join-Path $existing 'tools\ffmpeg-compat.exe'
[IO.File]::WriteAllText($existingExe, 'preserve this existing file')
$before = (Get-FileHash -LiteralPath $existingExe).Hash
if ((Run-Setup $existing $archive) -eq 0 -or (Get-FileHash -LiteralPath $existingExe).Hash -ne $before) { throw 'Existing different executable was replaced.' }
'PASS: existing different compatibility executable is preserved'
Write-Output ('LEGACY SETUP RESULT: 4 checks passed. Fixtures: ' + $testRoot)
