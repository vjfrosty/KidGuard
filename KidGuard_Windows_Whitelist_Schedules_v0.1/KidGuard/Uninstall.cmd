@echo off
rem Removes KidGuard (used by Apps & features). Windows asks for administrator approval.
setlocal
net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -Command "Start-Process -FilePath $env:ComSpec -ArgumentList '/c','\"%~f0\"' -Verb RunAs"
    exit /b
)
echo Removing KidGuard parental controls...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
echo.
pause
