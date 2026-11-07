param([string]$CachePath, [switch]$Recycle)
$ErrorActionPreference = 'Stop'
if ($CachePath -and -not (Test-Path -LiteralPath $CachePath -PathType Container)) {
    throw 'CachePath must point to an existing cache directory. Omit it to use generated test media.'
}
$projectRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $projectRoot 'build.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $projectRoot 'verification\IntegrationTests.exe'
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'verification') -Force | Out-Null
$coreSources = @('Core.cs','Encoding.cs','HardwareInfo.cs','Localization.cs','CacheRecycle.cs','MediaTools.cs','MediaToolText.cs') | ForEach-Object { Join-Path (Join-Path $projectRoot 'src') $_ }
$toolsTest = Join-Path $projectRoot 'verification\MediaToolsTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output ("/out:" + $toolsTest) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @coreSources (Join-Path $PSScriptRoot 'MediaToolsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Media tools test compilation failed.' }
& $toolsTest $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Media tools tests failed.' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output ("/out:" + $testExe) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @coreSources (Join-Path $PSScriptRoot 'IntegrationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
$testArgs = @($projectRoot)
if ($CachePath) { $testArgs += (Resolve-Path -LiteralPath $CachePath).Path }
& $testExe @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
$uiExe = Join-Path $projectRoot 'verification\UiTests.exe'
$icon = Join-Path $projectRoot 'assets\app-icon.ico'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output ("/out:" + $uiExe) "/win32icon:$icon" "/resource:$icon,BiliExtractor.AppIcon" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @coreSources (Join-Path $projectRoot 'src\MainForm.cs') (Join-Path $projectRoot 'src\DeleteConfirmationDialog.cs') (Join-Path $projectRoot 'src\MediaToolsDialog.cs') (Join-Path $PSScriptRoot 'UiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
& $uiExe $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
$recycleExe = Join-Path $projectRoot 'verification\RecycleTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output ("/out:" + $recycleExe) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll @coreSources (Join-Path $PSScriptRoot 'RecycleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Recycle test compilation failed.' }
if ($Recycle) { & $recycleExe $projectRoot --recycle } else { & $recycleExe $projectRoot }
if ($LASTEXITCODE -ne 0) { throw 'Recycle tests failed.' }
