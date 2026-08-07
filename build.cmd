@echo off
rem ---------------------------------------------------------------------------
rem  Builds Arch. Double-click this once; after that, use run.cmd.
rem  Pass "quiet" as the first argument to skip the pause (run.cmd does this).
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo Cannot find "dotnet".
    echo.
    echo Arch needs the .NET SDK 10.0 or newer. Install it from:
    echo     https://dotnet.microsoft.com/download
    echo Then close this window, open a new one, and run build.cmd again.
    echo.
    if not "%~1"=="quiet" pause
    exit /b 1
)

echo Building Arch ^(Release^). The first build takes about a minute...
echo.
dotnet build Arch.slnx -c Release --nologo
if errorlevel 1 (
    echo.
    echo BUILD FAILED - see the messages above.
    echo.
    echo If it mentions a file being locked, close any open arch.exe window or
    echo IDE that is holding the output, then run build.cmd again.
    echo.
    if not "%~1"=="quiet" pause
    exit /b 1
)

echo.
echo Build OK.
echo.
echo Next step: run.cmd
echo   - drag a folder onto run.cmd, or
echo   - double-click run.cmd and paste a folder path when it asks.
echo.
if not "%~1"=="quiet" pause
exit /b 0
