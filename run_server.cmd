@echo off
set SCRIPT_DIR=%~dp0
set EXE=%SCRIPT_DIR%bin\publish\RenegadeServer.exe
if not exist "%EXE%" (
	echo Executable not found. Run publish.cmd first.
	exit /b 1
)

if "%~1"=="" (
	"%EXE%" --port 3420
) else (
	"%EXE%" --port %1
)
