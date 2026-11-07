# SPDX-License-Identifier: GPL-3.0-only
param([string]$Version='1.4.1', [string]$Commit='HEAD', [string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$FFmpegArchive,
    [Parameter(Mandatory=$true)][string]$FFmpegSourceKit)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath($PSScriptRoot)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use major.minor.patch for Version.' }
if (-not $OutputDirectory) { $OutputDirectory='release-v'+$Version }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory=Join-Path $root $OutputDirectory }
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must stay inside the project.' }
$revision=(& git -C $root rev-parse --verify ($Commit+'^{commit}')).Trim()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve the build commit.' }
$name='BiliCacheExtractor-v'+$Version
$sourceName=$name+'-source.zip'
$portableName=$name+'-win-x64.zip'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sourceZip=Join-Path $output $sourceName
$portableZip=Join-Path $output $portableName
$sumFile=Join-Path $output 'SHA256SUMS.txt'
foreach ($path in @($sourceZip,$portableZip,$sumFile)) { if(Test-Path -LiteralPath $path){throw 'Artifacts already exist; choose a fresh output directory.'} }
$stage=Join-Path $output ('.stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
& git -C $root -c core.autocrlf=false -c core.eol=lf archive --format=zip ('--prefix='+$name+'-source/') ('--output='+$sourceZip) $revision
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
[IO.Compression.ZipFile]::ExtractToDirectory($sourceZip,$stage)
$source=Join-Path $stage ($name+'-source')
$pin=[IO.File]::ReadAllText((Join-Path $source 'tools\ffmpeg-recommended.json')) | ConvertFrom-Json
if ($pin.archiveRoot -notmatch '^[A-Za-z0-9._-]+$' -or $pin.archiveRoot -in @('.','..') -or $pin.sourceArchive -notmatch '^[A-Za-z0-9._-]+\.zip$') { throw 'Invalid media artifact filenames.' }
$FFmpegArchive=(Resolve-Path -LiteralPath $FFmpegArchive).Path
$FFmpegSourceKit=(Resolve-Path -LiteralPath $FFmpegSourceKit).Path
if ((Get-Item -LiteralPath $FFmpegArchive).Length -ne $pin.bytes -or (Get-FileHash -LiteralPath $FFmpegArchive).Hash -ne $pin.sha256) { throw 'FFmpeg runtime differs from the pinned archive.' }
if ((Get-Item -LiteralPath $FFmpegSourceKit).Length -ne $pin.sourceBytes -or (Get-FileHash -LiteralPath $FFmpegSourceKit).Hash -ne $pin.sourceSha256) { throw 'FFmpeg source kit differs from the pinned archive.' }
$runtimeName=$pin.archiveRoot+'.zip'
$runtimeZip=Join-Path $output $runtimeName
$sourceKitZip=Join-Path $output $pin.sourceArchive
foreach ($path in @($runtimeZip,$sourceKitZip)) { if(Test-Path -LiteralPath $path){throw 'Media artifacts already exist; choose a fresh output directory.'} }
$program=[IO.File]::ReadAllText((Join-Path $source 'src\Program.cs'))
if ([regex]::Match($program,'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value -ne $Version) { throw 'Application source version differs.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $source 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$portable=Join-Path $stage ($name+'-win-x64')
New-Item -ItemType Directory -Path (Join-Path $portable 'tools'),(Join-Path $portable 'third_party') -Force | Out-Null
foreach ($file in @('BiliCacheExtractor.exe','BiliCacheExtractor.Cli.exe','README.md','README.zh-CN.md','README.zh-TW.md','START-HERE.txt','LICENSE','THIRD_PARTY_NOTICES.md','Install-FFmpeg.cmd','Install-FFmpeg.ps1')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $portable $file)
}
foreach ($file in @('README.md','ffmpeg-recommended.json','ffmpeg-bundle.lock.json')) {
    Copy-Item -LiteralPath (Join-Path $source ('tools\'+$file)) -Destination (Join-Path $portable ('tools\'+$file))
}
Copy-Item -LiteralPath (Join-Path $source 'assets') -Destination $portable -Recurse
Copy-Item -LiteralPath (Join-Path $source 'third_party\FFMPEG-SOURCES.md') -Destination (Join-Path $portable 'third_party')
Copy-Item -LiteralPath (Join-Path $source 'third_party\ffmpeg-builds.LICENSE') -Destination (Join-Path $portable 'third_party')
New-Item -ItemType Directory -Path (Join-Path $portable 'third_party\portable-build') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'third_party\portable-build\sources.json') -Destination (Join-Path $portable 'third_party\portable-build')
Copy-Item -LiteralPath $sourceZip -Destination (Join-Path $portable $sourceName)
$unpack=Join-Path $stage 'ffmpeg-distribution'
[IO.Compression.ZipFile]::ExtractToDirectory($FFmpegArchive,$unpack)
$runtimeRoot=[IO.Path]::GetFullPath((Join-Path $unpack $pin.archiveRoot))
$runtimeDestination=[IO.Path]::GetFullPath((Join-Path $portable 'tools\ffmpeg'))
$stagePrefix=[IO.Path]::GetFullPath($stage).TrimEnd('\')+'\'
if (-not $runtimeRoot.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase) -or -not $runtimeDestination.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected media staging path.' }
Move-Item -LiteralPath $runtimeRoot -Destination $runtimeDestination
foreach ($required in @('bin\ffmpeg.exe','bin\ffprobe.exe','LICENSE.txt','BUILD-INFO.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeDestination $required))) { throw ('Runtime missing '+$required) }
}
Copy-Item -LiteralPath $FFmpegArchive -Destination $runtimeZip
Copy-Item -LiteralPath $FFmpegSourceKit -Destination $sourceKitZip
$info=[ordered]@{version=$Version;commit=$revision;architecture='win-x64';sourceArchive=$sourceName;sourceSha256=(Get-FileHash -LiteralPath $sourceZip).Hash.ToLowerInvariant();ffmpeg=$pin.version;ffmpegArchive=$runtimeName;ffmpegSha256=$pin.sha256;ffmpegSourceKit=$pin.sourceArchive;ffmpegSourceSha256=$pin.sourceSha256}
[IO.File]::WriteAllText((Join-Path $portable 'BUILD-INFO.json'),($info | ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
foreach($binary in @('BiliCacheExtractor.exe','BiliCacheExtractor.Cli.exe')) {
    if([Reflection.AssemblyName]::GetAssemblyName((Join-Path $portable $binary)).Version.ToString() -ne ($Version+'.0')) { throw 'Unexpected application binary version.' }
}
[IO.Compression.ZipFile]::CreateFromDirectory($portable,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$sums=foreach($artifact in @($portableZip,$sourceZip,$runtimeZip,$sourceKitZip)) { (Get-FileHash -LiteralPath $artifact).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($artifact) }
[IO.File]::WriteAllText($sumFile,(($sums -join "`n")+"`n"),[Text.Encoding]::ASCII)
$info | ConvertTo-Json
'Portable package: '+$portableZip
'Checksums: '+$sumFile
