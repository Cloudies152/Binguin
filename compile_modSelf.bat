@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
set "BINGUIN_EXIT_CODE=1"
pushd "%~dp0"
if errorlevel 1 goto directory_failed

set "BINGUIN_CONFIGURATION=%~1"
if not defined BINGUIN_CONFIGURATION set "BINGUIN_CONFIGURATION=Release"
if "%~2"=="" goto default_output
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0Source\build.ps1" -Configuration "%BINGUIN_CONFIGURATION%" -OutputRoot "%~2"
goto build_finished

:default_output
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0Source\build.ps1" -Configuration "%BINGUIN_CONFIGURATION%"

:build_finished
set "BINGUIN_EXIT_CODE=%ERRORLEVEL%"
popd
goto show_result

:directory_failed
echo [ERROR] Cannot open the project directory.

:show_result
echo(
if "%BINGUIN_EXIT_CODE%"=="0" (
    echo [SUCCESS] Binguin build completed.
) else (
    echo [ERROR] Binguin build failed. Exit code: %BINGUIN_EXIT_CODE%
)
echo The window will stay open until you press a key.
pause
exit /b %BINGUIN_EXIT_CODE%
