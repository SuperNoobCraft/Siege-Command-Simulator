@echo off
cd /d "%~dp0"
start "" "SiegeCommandSimulator.exe" --config "%VOTANIC_PATH%\Configs\ConfigCAVE.vxrc" --setting "VotanicXR/Settings/Setting.vxrs" --option "VotanicXR/Options/Option.vxro" --ref "null" --version "2020.10.2" -popupwindow -demoDay
