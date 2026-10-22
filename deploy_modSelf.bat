@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
set "BINGUIN_EXIT_CODE=1"
pushd "%~dp0"
if errorlevel 1 goto directory_failed

powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0deploy_modSelf.ps1" %*
set "BINGUIN_EXIT_CODE=%ERRORLEVEL%"
popd
goto show_result

:directory_failed
echo [ERROR] Cannot open the project directory.

:show_result
echo(
if "%BINGUIN_EXIT_CODE%"=="0" (
    echo [SUCCESS] Binguin deployment completed.
) else (
    echo [ERROR] Binguin deployment failed. Exit code: %BINGUIN_EXIT_CODE%
)
echo The window will stay open until you press a key.
pause
exit /b %BINGUIN_EXIT_CODE%
