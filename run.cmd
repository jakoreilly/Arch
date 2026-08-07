@echo off
rem ---------------------------------------------------------------------------
rem  Runs Arch against a folder and opens the generated site in your browser.
rem
rem    run.cmd                        asks which folder to analyse
rem    run.cmd C:\path\to\project     analyses that folder
rem    (drag a folder onto run.cmd)   same thing
rem
rem  Anything after the folder is passed straight to arch, e.g.
rem    run.cmd C:\src\App --out C:\reports\app --no-open
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "EXE=%~dp0src\Arch.Cli\bin\Release\net10.0\arch.exe"

if not exist "%EXE%" (
    echo Arch has not been built yet - building it now.
    echo.
    call "%~dp0build.cmd" quiet
    if errorlevel 1 exit /b 1
    echo.
)

set "TARGET=%~1"
set "PASSTHRU=%2 %3 %4 %5 %6 %7 %8 %9"

rem "run.cmd --no-open" means "no folder given, but here are some options" - without
rem this, the flag itself would be taken as the folder to analyse and fail confusingly.
if not "%TARGET%"=="" if "%TARGET:~0,2%"=="--" (
    set "PASSTHRU=%*"
    set "TARGET="
)

if "%TARGET%"=="" (
    echo What do you want Arch to analyse?
    echo.
    echo   Drag a folder into this window, or type/paste its full path,
    echo   then press Enter. Press Enter on its own to analyse Arch itself.
    echo.
    set /p "TARGET=Folder: "
)

rem A pasted or dragged path often arrives wrapped in quotes and padded with spaces.
rem Strip both, or the "is this a folder" test below rejects a perfectly good path.
rem (This SET is deliberately unquoted - that is the idiom that removes the quotes.)
set TARGET=%TARGET:"=%
:trim_lead
if not defined TARGET goto trim_done
if "%TARGET:~0,1%"==" " (
    set "TARGET=%TARGET:~1%"
    goto trim_lead
)
:trim_tail
if "%TARGET:~-1%"==" " (
    set "TARGET=%TARGET:~0,-1%"
    goto trim_tail
)
:trim_done
if "%TARGET%"=="" set "TARGET=%~dp0"

rem Drop a trailing backslash. In "%TARGET%" a final \ escapes the closing quote, so
rem C:\some\folder\ would swallow every option after it into the path. A bare drive
rem root (C:\) keeps its backslash - there it is part of the path, not a separator.
if "%TARGET:~-1%"=="\" if not "%TARGET:~-2%"==":\" set "TARGET=%TARGET:~0,-1%"

if not exist "%TARGET%\" (
    echo.
    echo "%TARGET%" is not a folder, so there is nothing to analyse.
    echo Check the path and try again.
    echo.
    pause
    exit /b 2
)

echo.
echo Analysing "%TARGET%"
echo.
"%EXE%" "%TARGET%" %PASSTHRU%
set "CODE=%ERRORLEVEL%"

echo.
if "%CODE%"=="0" (
    echo Done. The site should have opened in your browser.
    echo If it did not, open the index.html shown above.
) else if "%CODE%"=="2" (
    echo Arch found nothing it can analyse in that folder, or an option was
    echo not recognised. See the message above.
) else if "%CODE%"=="3" (
    echo The site was written, but a --fail-on quality gate tripped.
) else (
    echo Arch stopped with an error - see the message above.
)
echo.
pause
exit /b %CODE%
