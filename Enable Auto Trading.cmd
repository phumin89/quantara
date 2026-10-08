@echo off
rem Owner action: allow CEO sizing within existing funded capital after verification.
echo Quantara: BTC / ETH / SOL, daily research and portfolio reviews.
echo CEO may size orders up to the existing funded capital, including fee reserve.
echo Existing balances, loss history and owner Stop are kept.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\test-trading.ps1"
if errorlevel 1 goto verification_failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\test-live-integration.ps1"
if errorlevel 1 goto verification_failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\activate-live.ps1"
if errorlevel 1 goto activation_failed
exit /b 0

:activation_failed
echo Owner activation did not complete. Inspect the reported reason before retrying.
pause
exit /b 1

:verification_failed
echo Software verification failed. Financial activation was not attempted.
pause
exit /b 1
