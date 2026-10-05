# Forward investment cash funding

## Implemented

- The existing month-end CFO worker synchronizes approved investment allocations into a separate cash-only paper ledger.
- Salary, housing, essentials, discretionary funds and protected reserves are not credited to that ledger.
- Each receipt references its source salary date, amount and funding account. Repeated ticks, concurrent synchronization and process restart do not duplicate receipts.
- The destination is stored beside the configured forward funding file, with the suffix `.investment.json`. The existing treasury schema and legacy demo portfolio are unchanged.
- The status endpoint exposes cumulative investment released, pending transfer and destination portfolio separately. Cumulative release is not additional cash and must not be added to destination cash.
- The UI displays receipts, missing/unavailable states and the explicit trading-disabled boundary.

## Recovery and limits

Treasury and investment writes are separate, not one atomic transaction. If treasury posting succeeds before synchronization, a later successful synchronization credits missing receipts. A caught worker error stops further funding until inspection and restart. Corrupt, mismatched or missing initialized destination state fails closed instead of resetting balances.

This ledger has no holdings or execution adapter. Its equity equals contributed cash and its PnL is zero. It is not connected to a bank or live broker. AI proposals and RiskEngine-approved paper orders still need integration with this ledger before an end-to-end forward trading claim is valid.

## Verification

On 2026-09-12: 658 .NET tests passed, 8 web rendering tests passed, TypeScript checking passed, and the focused UI detector reported no findings. No browser visual inspection or running-app deployment was verified in this completion check.

The controlled-clock integration test opens in September 2026 and advances to November month-end: salary postings total THB 210,000, protected reserve THB 65,175, destination cash THB 8,295 and PnL THB 0. These are test-fixture values, not current account balances or investment returns.
