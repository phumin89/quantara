@echo off
rem Owner action: enables real BTC Spot trading under the saved mandate.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\activate-live.ps1"
if errorlevel 1 pause
