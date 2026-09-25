@echo off
cd /d "%~dp0"
set "PID=%~1"
timeout /t 3 /nobreak >nul
if not "%PID%"=="" taskkill /F /PID %PID% >nul 2>&1
taskkill /F /IM SiegeCommandSimulator.exe >nul 2>&1
timeout /t 2 /nobreak >nul
start "" "%~dp0VotanicXR_[CAVE][XR] (Demo Day).bat"
exit /b 0
