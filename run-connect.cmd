@echo off
rem ---------------------------------------------------------------------------
rem  Runs Arch's SQL analyser against a LIVE database, via "arch connect".
rem  Unlike run.cmd / run-sql.cmd, there is no folder to scan - the connection
rem  string comes from a file (--conn-file) or the ARCHSQL_CONNECTION
rem  environment variable (--env), never typed on the command line.
rem
rem    run-connect.cmd                      asks for a conn-file path
rem    run-connect.cmd db.json              connects using that conn-file
rem    run-connect.cmd --env                connects using ARCHSQL_CONNECTION
rem
rem  Anything after that is passed straight to "arch connect", e.g.
rem    run-connect.cmd db.json --out C:\reports\db --no-open
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

set "MODE=%~1"
set "PASSTHRU=%2 %3 %4 %5 %6 %7 %8 %9"

if "%MODE%"=="--env" goto have_mode
if not "%MODE%"=="" goto have_conn_file

echo How do you want Arch to connect to the database?
echo.
echo   1. A conn-file (a small JSON file holding the connection string)
echo   2. The ARCHSQL_CONNECTION environment variable
echo.
set /p "CHOICE=Choice (1 or 2): "
if "%CHOICE%"=="2" (
    set "MODE=--env"
    goto have_mode
)

echo.
echo   Drag a conn-file into this window, or type/paste its full path,
echo   then press Enter.
echo.
set /p "MODE=Conn-file: "

:have_conn_file
rem A pasted or dragged path often arrives wrapped in quotes and padded with spaces.
if defined MODE set MODE=%MODE:"=%
:trim_lead
if not defined MODE goto trim_done
if "%MODE:~0,1%"==" " (
    set "MODE=%MODE:~1%"
    goto trim_lead
)
:trim_tail
if "%MODE:~-1%"==" " (
    set "MODE=%MODE:~0,-1%"
    goto trim_tail
)
:trim_done

if "%MODE%"=="" (
    echo.
    echo No conn-file given, so there is nothing to connect with.
    echo.
    pause
    exit /b 2
)

if not exist "%MODE%" (
    echo.
    echo "%MODE%" is not a file, so there is nothing to connect with.
    echo Check the path and try again.
    echo.
    pause
    exit /b 2
)

echo.
echo Connecting via conn-file "%MODE%"
echo.
"%EXE%" connect --conn-file "%MODE%" %PASSTHRU%
goto ran

:have_mode
echo.
echo Connecting via ARCHSQL_CONNECTION
echo.
"%EXE%" connect --env %PASSTHRU%

:ran
set "CODE=%ERRORLEVEL%"

echo.
if "%CODE%"=="0" (
    echo Done. The site should have opened in your browser.
    echo If it did not, open the index.html shown above.
) else if "%CODE%"=="2" (
    echo Arch could not connect, or an option was not recognised.
    echo See the message above.
) else if "%CODE%"=="3" (
    echo The site was written, but a --fail-on quality gate tripped.
) else (
    echo Arch stopped with an error - see the message above.
)
echo.
pause
exit /b %CODE%
