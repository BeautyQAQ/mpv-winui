@echo off
setlocal
if "%~1"=="" goto noMedia
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Debug.ps1" -MediaPath "%~f1"
set "result=%ERRORLEVEL%"
goto finished
:noMedia
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Debug.ps1"
set "result=%ERRORLEVEL%"
:finished
echo.
echo After closing the player, run Collect-Logs.cmd to export the logs.
pause
exit /b %result%
