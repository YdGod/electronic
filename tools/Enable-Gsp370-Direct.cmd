@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Gsp370Mode.ps1" -Mode Enable
if errorlevel 1 pause
