@echo off
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build_x64_release.ps1" %*
exit /b %ERRORLEVEL%
