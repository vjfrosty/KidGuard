@echo off
rem Double-click to install KidGuard. Windows asks for administrator approval.
setlocal
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -Command "Start-Process -FilePath $env:ComSpec -ArgumentList '/c','\"%~f0\"' -Verb RunAs"
    exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
if errorlevel 1 pause
