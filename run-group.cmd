@echo off
rem ---------------------------------------------------------------------------
rem  Runs Arch against a group config (multiple repos -> one landscape).
rem
rem    run-group.cmd                   lists the *.json configs here, asks which
rem    run-group.cmd my-groups.json    runs that config directly
rem    (drag a .json onto run-group.cmd)  same thing
rem
rem  Press Enter with no config picked to fall back to run.cmd (single-folder
rem  analysis) instead. Anything after the config is passed straight to arch,
rem  e.g. run-group.cmd payzone-groups.json --no-open
rem ---------------------------------------------------------------------------
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "EXE=%~dp0src\Arch.Cli\bin\Release\net10.0\arch.exe"

if not exist "%EXE%" (
    echo Arch has not been built yet - building it now.
    echo.
    call "%~dp0build.cmd" quiet
    if errorlevel 1 exit /b 1
    echo.
)

set "CONFIG=%~1"
set "PASSTHRU=%2 %3 %4 %5 %6 %7 %8 %9"

if not "%CONFIG%"=="" goto have_config

set "count=0"
for %%F in ("%~dp0*.json") do (
    set /a count+=1
    set "file!count!=%%~nxF"
    echo   !count!. %%~nxF
)

if "%count%"=="0" (
    echo No .json group configs found next to run-group.cmd.
    echo Falling back to run.cmd for a single-folder analysis.
    echo.
    call "%~dp0run.cmd" %*
    exit /b %ERRORLEVEL%
)

echo.
set /p "CHOICE=Which config? (number, or Enter for run.cmd): "

if "%CHOICE%"=="" (
    call "%~dp0run.cmd" %*
    exit /b %ERRORLEVEL%
)

set "PICKED="
for /l %%i in (1,1,%count%) do (
    if "%CHOICE%"=="%%i" set "PICKED=!file%%i!"
)

if "%PICKED%"=="" (
    echo.
    echo "%CHOICE%" is not one of the numbers above.
    echo.
    pause
    exit /b 2
)

set "CONFIG=%~dp0%PICKED%"

:have_config
set CONFIG=%CONFIG:"=%

if not exist "%CONFIG%" ( 
    echo.
    echo "%CONFIG%" is not a file, so there is nothing to run.
    echo Check the path and try again.
    echo.
    pause
    exit /b 2
)

echo.
echo Running group "%CONFIG%"
echo.
"%EXE%" group "%CONFIG%" %PASSTHRU%
set "CODE=%ERRORLEVEL%"

echo.
if "%CODE%"=="0" (
    echo Done. The generated sites and landscape should have opened in your browser.
) else (
    echo arch group stopped with an error - see the message above.
)
echo.
pause
exit /b %CODE%
