# 100-THB Auto Spot review — 2026-10-08

## Change and scope

Owner requested a small trial: buy/sell orders capped at 100 THB. The new owner-only launcher first requires both isolated suites to pass, then uses the existing verified activation and reconciliation flow with a persisted **fee-inclusive maximum of 100 THB**. A failed software check prevents financial activation. It preserves a smaller saved order cap, other mandate limits, account binding, opening inventory, losses and owner Stop. Reducing the cap or expanding markets is refused while an order remains unresolved. Repeating the same cap does not create a new mandate version.

The application execution universe remains verified BTC/THB, ETH/THB and SOL/THB. Research may inspect other coins; this change does not authorize orders in them. No activation, placement or cancellation was performed during this work. The ordinary startup launcher remains separate from owner activation. CEO may HOLD; passing tests never forces a trade.

## Review and fixes

- Independently inspected the actual activation, command routing, PowerShell launch/rollback flow and added tests. Invalid non-positive requested caps fail before private broker reads. A requested cap never raises an existing smaller cap.
- Added deterministic fee-inclusive sizing, pending-cash and owned/unreserved sell checks. Synthetic BTC/ETH/SOL round trips serialize/restore the ledger, reconcile purchases and exits, retain Armed on successful ordinary reconciliation, release reservations and reject duplicate accounting. The first test fixture incorrectly added a fee beyond its buy budget; corrected the received quantity while preserving the gross budget. This is normalized synthetic evidence, not exchange settlement proof.
- Added conflicting same-day market-bar regressions. Existing code rejects contradictory prices instead of inventing a history. No history parsing or execution authority was loosened.
- Added a reusable isolated database runner. Both verification runners fail on zero executed tests. Local worker bridge failed because its configured Codex executable was missing; bounded tooling edits used the prescribed fallback and were inspected by the lead. No UI changed, so no repeated responsive passes were needed.
- The first PostgreSQL run passed 106/108. Two lead-added Stop checks compared a deserialized symbol list by reference. Replaced that assertion with structural serialization equality over the complete persisted mandate, retaining the Stop/Armed assertions. Independently inspected the worker correction and reran all 108 checks successfully.

## Evidence

| Check | Result |
| --- | --- |
| Focused sizing, synthetic round trips and conflicting bars | 12 passed, 0 failed, 0 skipped |
| Isolated activation launcher cases, fake Docker only | 14 passed |
| Headless trading selection on Windows | 667 passed, 169 failed, 836 executed |
| Failure classification from TRX | All 169 contain Application Control blocking `TradingCompany.LiveExecution.dll` (`0x800711C7`); 15 additionally fail their expected-exception assertion |
| Latest integration build | 0 warnings, 0 errors |
| Empty test-selection regression | Exit 1 as required |
| Final Docker headless trading selection, Windows PowerShell 5.1 launcher | 836 passed, 0 failed, 0 skipped |
| Final isolated PostgreSQL integration, Windows PowerShell 5.1 launcher | 108 passed, 0 failed, 0 skipped |
| CMD verification-gate control flow, all commands replaced by harmless stubs | 4 passed; failed checks never reach activation |
| Release owner image | Built successfully from the reviewed source |
| Current Bitkub profile, key binding and Trade access | Read-only production check passed; no activation or order |
| Isolated test container/network cleanup | No matching test containers or networks remained |

Raw ignored evidence is under `work/hundred-baht-*.log` and `work/hundred-baht-*-results/*.trx`, including `hundred-baht-docker-integration-final.log`, `hundred-baht-owner-release-build.log` and `hundred-baht-live-readonly-preflight.log`. The twelve focused cases are also part of the larger selection; do not add those counts together. Compilation and fixture success do not prove real execution or investment performance. Windows protection was not weakened.

Public API inspection found two contradictory SOL daily bars at UTC 2026-05-09 (`1778284800`). BTC/ETH histories returned sufficient bars. The public symbol list exposed 247 active unfrozen THB markets with nominal minimum quote size at most 100 THB; that count does not account for fee reserve, spread, depth, verified settlement or execution permissions and is not an all-market trading claim. [Official Bitkub API reference](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md).

## Owner handoff and remaining Live evidence

1. Software acceptance is complete through the existing isolated Docker workflow. The owner can choose `Enable 100 Baht Auto Trading.cmd` while Docker Desktop is running. It repeats the two isolated suites and stops on failure before entering the existing activation path.
2. The owner initiates financial activation and any real transaction; this review performed neither. The launcher verifies current rules, receipts, wallet and saved limits before starting CEO. HOLD remains a valid conclusion.
3. Observe a real application order, its authenticated receipt, exact wallet reconciliation and durable recovery without duplicate dispatch. Until that evidence exists, real autonomous trading remains unproven. SOL remains excluded when its required history conflicts; BTC/ETH may continue only within existing verified authority and complete held-asset evidence. No all-market trial or additional execution permission was added.

Automatic approval review rejected the earlier attempt to start Docker with the reason `blocked by policy`. The start was not retried through an alternate path. Docker became available later; the final verification and read-only broker check above then completed successfully. Running owner/company containers were not replaced or activated during this review.
