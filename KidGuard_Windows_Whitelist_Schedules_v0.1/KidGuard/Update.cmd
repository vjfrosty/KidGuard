@echo off
rem Double-click to update an installed KidGuard to this package. Windows asks for administrator approval.
setlocal
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -Command "Start-Process -FilePath $env:ComSpec -ArgumentList '/c','\"%~f0\"' -Verb RunAs"
    exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update.ps1"
echo.
pause
