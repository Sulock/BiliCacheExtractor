@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-FFmpeg.ps1"
if errorlevel 1 (
    echo.
    echo FFmpeg setup did not finish. See the message above.
    pause
    exit /b 1
)
echo.
echo You can now start BiliCacheExtractor.exe.
pause
