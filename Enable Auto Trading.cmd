@echo off
rem Owner action: enables verified BTC/ETH/SOL Spot markets under the saved limits.
echo Quantara: BTC / ETH / SOL, daily research and portfolio reviews.
echo Existing balances, loss history and owner Stop are kept.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\activate-live.ps1"
if errorlevel 1 pause
