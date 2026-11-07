# SPDX-License-Identifier: GPL-3.0-only
param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$setupRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$destination = Join-Path $setupRoot 'tools\ffmpeg-compat.exe'
$pin = [IO.File]::ReadAllText((Join-Path $setupRoot 'tools\ffmpeg-compat.lock.json')) | ConvertFrom-Json
$expectedBinary = $pin.executableSha256
$expectedArchive = $pin.archiveSha256
$url = $pin.url
$temporary = $null
try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expectedBinary) {
            throw 'A different tools\ffmpeg-compat.exe already exists. It has been preserved. Move it to a backup folder before installing this version.'
        }
        Write-Output 'FFmpeg 4.2.2 compatibility encoder is already installed and verified.'
        exit 0
    }
    $temporary = Join-Path $setupRoot ('.ffmpeg-setup-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary | Out-Null
    if ($ArchivePath) {
        $archive = (Resolve-Path -LiteralPath $ArchivePath).Path
    } else {
        $archive = Join-Path $temporary 'imageio-ffmpeg.whl'
        Write-Output 'Downloading FFmpeg 4.2.2 from imageio-ffmpeg on PyPI (22 MB)...'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive -TimeoutSec 600
    }
    if ((Get-Item -LiteralPath $archive).Length -ne $pin.archiveBytes -or
        (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedArchive) {
        throw 'Downloaded archive checksum verification failed. Nothing was installed.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entry = $zip.GetEntry($pin.archiveEntry)
        if ($null -eq $entry -or $entry.Length -ne $pin.executableBytes) { throw 'The expected FFmpeg executable is missing from the archive.' }
        $candidate = Join-Path $temporary 'ffmpeg-compat.exe'
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $candidate)
    } finally { $zip.Dispose() }
    if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $expectedBinary) {
        throw 'Extracted FFmpeg checksum verification failed. Nothing was installed.'
    }
    $version = @(& $candidate -version)
    if ($LASTEXITCODE -ne 0 -or $version[0] -notmatch '^ffmpeg version 4\.2\.2 ') { throw 'FFmpeg did not start successfully.' }
    New-Item -ItemType Directory -Path (Join-Path $setupRoot 'tools') -Force | Out-Null
    # File.Move refuses to replace a concurrently created existing file.
    [IO.File]::Move($candidate, $destination)
    Write-Output 'FFmpeg 4.2.2 installed. Open BiliCacheExtractor.exe and select H.264 or H.265 with Auto or NVIDIA.'
    Write-Output 'License: GPL-3.0-or-later. Provenance: LEGACY-FFMPEG.md.'
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($temporary -and (Test-Path -LiteralPath $temporary)) {
        $resolved = [IO.Path]::GetFullPath($temporary)
        if (-not $resolved.StartsWith($setupRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notmatch '^\.ffmpeg-setup-[0-9a-f]{32}$') { throw 'Unexpected temporary directory.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
