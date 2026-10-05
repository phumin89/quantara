@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start-live-readonly.ps1"
if errorlevel 1 pause
