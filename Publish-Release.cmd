@echo off
setlocal
cd /d "%~dp0"
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Release.ps1" -Version "%VERSION%"
exit /b %errorlevel%
