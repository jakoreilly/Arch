@echo off
rem ---------------------------------------------------------------------------
rem  Runs the standalone T-SQL formatter (sqlfmt-tsql) against a .sql file or a
rem  folder of them. This is the tool behind "archsql --format" - it can also
rem  be built and used entirely on its own, without the rest of Arch. See
rem  src\Arch.Sql.Format.Cli\README.md.
rem
rem    run-format.cmd                       asks which file or folder to format
rem    run-format.cmd C:\path\to\scripts    formats every .sql file under it
rem    run-format.cmd C:\path\to\file.sql   formats that one file
rem    (drag a file or folder onto this)    same thing
rem
rem  This reformats files IN PLACE. Add --check to only report which files
rem  would change, without writing anything, e.g.
rem    run-format.cmd C:\src\App --check
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "EXE=%~dp0src\Arch.Sql.Format.Cli\bin\Release\net10.0\sqlfmt-tsql.exe"

if not exist "%EXE%" (
    echo sqlfmt-tsql has not been built yet - building it now.
    echo.
    call "%~dp0build.cmd" quiet
    if errorlevel 1 exit /b 1
    echo.
)

set "TARGET=%~1"
set "PASSTHRU=%2 %3 %4 %5 %6 %7 %8 %9"

rem "run-format.cmd --check" means "no path given, but here are some options" -
rem without this, the flag itself would be taken as the path and fail confusingly.
if not "%TARGET%"=="" if "%TARGET:~0,2%"=="--" (
    set "PASSTHRU=%*"
    set "TARGET="
)

if "%TARGET%"=="" (
    echo What .sql file or folder do you want to format?
    echo.
    echo   Drag a file or folder into this window, or type/paste its full path,
    echo   then press Enter.
    echo.
    echo   This reformats files IN PLACE. Add --check afterwards to only report
    echo   which files would change, without writing anything.
    echo.
    set /p "TARGET=Path: "
)

rem A pasted or dragged path often arrives wrapped in quotes and padded with spaces.
rem (This SET is deliberately unquoted - that is the idiom that removes the quotes.)
if defined TARGET set TARGET=%TARGET:"=%
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

rem Drop a trailing backslash on a folder path (not a bare drive root) - a final \
rem in "%TARGET%" escapes the closing quote and swallows the options after it.
if "%TARGET:~-1%"=="\" if not "%TARGET:~-2%"==":\" set "TARGET=%TARGET:~0,-1%"

if "%TARGET%"=="" (
    echo.
    echo No path given, so there is nothing to format.
    echo.
    pause
    exit /b 2
)

if not exist "%TARGET%" (
    echo.
    echo "%TARGET%" does not exist, so there is nothing to format.
    echo Check the path and try again.
    echo.
    pause
    exit /b 2
)

echo.
echo Formatting "%TARGET%"
echo.
"%EXE%" "%TARGET%" %PASSTHRU%
set "CODE=%ERRORLEVEL%"

echo.
if "%CODE%"=="0" (
    echo Done.
) else if "%CODE%"=="2" (
    echo No path was given, or an option was not recognised. See the message above.
) else if "%CODE%"=="3" (
    echo --check found file^(s^) that would be reformatted. See the message above.
) else (
    echo sqlfmt-tsql stopped with an error - see the message above.
)
echo.
pause
exit /b %CODE%
