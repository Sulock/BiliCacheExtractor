$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.Management.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
$icon = Join-Path $projectRoot 'assets\app-icon.ico'
$iconArgs = @("/win32icon:$icon", "/resource:$icon,BiliExtractor.AppIcon")
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ("/win32manifest:" + (Join-Path $projectRoot 'src\app.manifest')) ("/out:" + (Join-Path $projectRoot 'BiliCacheExtractor.exe')) @references @iconArgs @sources
if ($LASTEXITCODE -ne 0) { throw 'GUI compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output ("/out:" + (Join-Path $projectRoot 'BiliCacheExtractor.Cli.exe')) @references @iconArgs @sources
if ($LASTEXITCODE -ne 0) { throw 'CLI compilation failed.' }
Write-Output 'Built BiliCacheExtractor.exe and BiliCacheExtractor.Cli.exe'
