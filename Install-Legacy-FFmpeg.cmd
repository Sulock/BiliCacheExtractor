@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Legacy-FFmpeg.ps1"
if errorlevel 1 (
    echo Compatibility encoder setup failed. Please read the error above.
    pause
    exit /b 1
)
echo Compatibility encoder is ready.
pause
