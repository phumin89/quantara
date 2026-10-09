@echo off
rem Explicit owner action. Run only when ready to allow real Spot orders.
echo Quantara: allow verified native THB Spot orders up to 100 THB including fee reserve.
echo Existing smaller limits, balances, loss history and owner Stop are preserved.
echo CEO may hold when evidence or trading conditions do not justify a new order.
echo Verifying software with isolated fixtures before owner activation...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\test-trading.ps1"
if errorlevel 1 goto verification_failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\test-live-integration.ps1"
if errorlevel 1 goto verification_failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\activate-live.ps1" -SmallOrders
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
