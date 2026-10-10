@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\pair-live-owner.ps1"
if errorlevel 1 pause
