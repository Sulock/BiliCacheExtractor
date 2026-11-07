# SPDX-License-Identifier: GPL-3.0-only
param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$setupRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$setupLock = Get-Content -LiteralPath (Join-Path $setupRoot 'tools\ffmpeg.lock.json') -Raw | ConvertFrom-Json
$setupTools = Join-Path $setupRoot 'tools'
$setupDestination = Join-Path $setupTools 'ffmpeg'
$setupMarkerName = 'BiliCacheExtractor-install.json'
$setupTemporary = $null

function Get-ChildPath([string]$Parent, [string]$Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The setup path must remain inside its parent directory.'
    }
    return $childFull
}

try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
    foreach ($manualTool in @('ffmpeg.exe', 'ffprobe.exe')) {
        if (Test-Path -LiteralPath (Join-Path $setupTools $manualTool)) {
            throw 'FFmpeg was installed manually in tools. Keep that installation and skip setup, or move it to a backup folder before using this installer.'
        }
    }
    if (Test-Path -LiteralPath $setupDestination) {
        $markerPath = Join-Path $setupDestination $setupMarkerName
        if (-not (Test-Path -LiteralPath $markerPath)) { throw 'tools\ffmpeg already exists. Move it to a backup folder before installing.' }
        $installed = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($installed.archiveSha256 -ne $setupLock.sha256 -or @($installed.files).Count -lt 3) {
            throw 'A different or incomplete FFmpeg installation exists. Move tools\ffmpeg to a backup folder before installing.'
        }
        foreach ($installedFile in $installed.files) {
            $installedPath = Get-ChildPath $setupDestination (Join-Path $setupDestination $installedFile.path)
            if (-not (Test-Path -LiteralPath $installedPath -PathType Leaf) -or
                (Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash -ne $installedFile.sha256) {
                throw 'An installed FFmpeg file has changed. Move tools\ffmpeg to a backup folder before installing again.'
            }
        }
        Write-Output ('FFmpeg ' + $setupLock.version + ' is already installed and verified.')
        exit 0
    }

    $setupTemporary = Join-Path $setupRoot ('.ffmpeg-setup-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $setupTemporary | Out-Null
    $archive = Join-Path $setupTemporary 'ffmpeg.zip'
    if ($ArchivePath) {
        $archive = (Resolve-Path -LiteralPath $ArchivePath).Path
    } else {
        Write-Output ('Downloading FFmpeg ' + $setupLock.version + ' from ' + $setupLock.provider + ' (about 86 MiB)...')
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $setupLock.url -OutFile $archive -TimeoutSec 600
    }
    if ((Get-Item -LiteralPath $archive).Length -ne $setupLock.bytes -or
        (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $setupLock.sha256) {
        throw 'FFmpeg archive verification failed. Nothing was installed.'
    }
    $unpack = Join-Path $setupTemporary 'unpacked'
    Expand-Archive -LiteralPath $archive -DestinationPath $unpack
    $distribution = Get-ChildPath $setupTemporary (Join-Path $unpack $setupLock.archiveRoot)
    foreach ($required in @('bin\ffmpeg.exe', 'bin\ffprobe.exe', 'LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $distribution $required) -PathType Leaf)) {
            throw ('The verified distribution is missing ' + $required)
        }
    }
    $ffmpegVersion = @(& (Join-Path $distribution 'bin\ffmpeg.exe') -version)
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg failed to start on this computer.' }
    $ffprobeVersion = @(& (Join-Path $distribution 'bin\ffprobe.exe') -version)
    if ($LASTEXITCODE -ne 0) { throw 'FFprobe failed to start on this computer.' }
    $sourceInfo = @(
        'FFmpeg: ' + $setupLock.version
        'License: ' + $setupLock.license
        'Copyright: the FFmpeg developers and contributors to the included libraries.'
        'Original distribution: ' + $setupLock.releaseUrl
        'Download: ' + $setupLock.url
        'Archive SHA-256: ' + $setupLock.sha256
        'FFmpeg source: ' + $setupLock.sourceUrl
        'Build scripts and dependency recipes: ' + $setupLock.buildScriptsUrl
        'This distribution was downloaded separately from its upstream provider.'
        'The original LICENSE.txt and distribution files are preserved alongside this notice.'
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $distribution 'SOURCE-INFO.txt'), $sourceInfo, (New-Object Text.UTF8Encoding($false)))
    $installedFiles = @(@(Get-ChildItem -LiteralPath (Join-Path $distribution 'bin') -File) + @(Get-Item -LiteralPath (Join-Path $distribution 'LICENSE.txt')) | ForEach-Object {
        [pscustomobject]@{
            path = $_.FullName.Substring($distribution.Length + 1).Replace('\','/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $marker = @{ archiveSha256 = $setupLock.sha256; version = $setupLock.version; files = $installedFiles } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $distribution $setupMarkerName), $marker, (New-Object Text.UTF8Encoding($false)))
    $distribution = Get-ChildPath $setupTemporary $distribution
    $setupDestination = Get-ChildPath $setupRoot $setupDestination
    Move-Item -LiteralPath $distribution -Destination $setupDestination
    Write-Output $ffmpegVersion[0]
    Write-Output 'FFmpeg and FFprobe are ready. Start BiliCacheExtractor.exe.'
    Write-Output 'License and source information: tools\ffmpeg\LICENSE.txt and SOURCE-INFO.txt.'
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    if ($setupTemporary -and (Test-Path -LiteralPath $setupTemporary)) {
        $cleanupPath = Get-ChildPath $setupRoot $setupTemporary
        if ([IO.Path]::GetFileName($cleanupPath) -notmatch '^\.ffmpeg-setup-[0-9a-f]{32}$') { throw 'Unexpected setup cleanup directory.' }
        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}
