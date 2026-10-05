# Forward funding implementation plan

> **For agentic workers:** Use superpowers:executing-plans task by task.

**Goal:** Persist an isolated, month-end funded paper treasury without importing historical profits.
**Architecture:** Reuse PersonalCfoAllocationEngine for each month's allocation. An atomic file store serializes month-end postings under an exclusive file lock. A background worker checks the Bangkok calendar; GET exposes the persisted account without posting money.
**Tech Stack:** Existing .NET/JSON and React. No new dependencies.
**Spec:** docs/superpowers/specs/2026-09-08-frozen-replay-design.md; user clarification: salary once each month, at month-end.

## Global constraints

- First payday September 30, 2026; salary 70000, essentials 24400, housing 14000, reserve target 115200, emergency rate .25, investing rate .70. Opening balances zero.
- Month-end means beginning of the final Bangkok calendar date; no weekend adjustment. Persist actual posting timestamp separately from due date.
- After downtime post elapsed due months once, never future months or pre-activation months. Restart must reuse the original opening timestamp and mandate.
- This funding slice has no execution path. Show TRADING_NOT_ENABLED; do not represent missing AI as an AI decision. Subsequent proposal/next-price execution integration is separate.
- Preserve the existing shared portfolio, frozen snapshot and its exact build. No live money or paid model calls.

## Task 1: Durable month-end ledger

Files: Application/ForwardFunding.cs; Infrastructure/JsonForwardFundingStore.cs; tests/TradingCompany.Domain.Tests/ForwardFundingTests.cs.
- [x] Write tests for zero opening balances, September 29 versus 30, duplicate ticks/restart/concurrent stores, year boundary, leap February, corruption rejection and conservation.
- [x] Run `dotnet test tests/TradingCompany.Domain.Tests --filter ForwardFundingTests` to establish missing implementation.
- [x] Implement `IForwardFundingStore.Read()` and `Advance(DateTimeOffset now)`. Use Bangkok UTC+7, immutable monthly entries, one-month CFO engine inputs, a lock file and atomic replace. Recalculate saved allocations before accepting state; reject corruption rather than resetting.
- [x] Run focused tests; inspect arithmetic, serialized state and error paths directly.

## Task 2: Runtime and visible status

Files: Api/ForwardFundingService.cs; Api/Program.cs; web/app/ForwardFundingPanel.tsx; web/app/page.tsx; relevant tests; docker-compose.yml; README.md.
- [x] Add API tests proving GET does not fund, disabled state cannot auto-create an account, and shared portfolio is unchanged.
- [x] Worker runs only when explicitly enabled; stores actual timestamps using TimeProvider. Fail closed on corrupt storage, with visible unavailable status. Stop worker retries from creating replacement balances.
- [x] Render account identity, next payday, allocated reserves/investment cash, posting count and explicit trading-disabled status. Poll read-only status; handle unavailable API without showing fabricated zero balances.
- [x] Run .NET suite, UI tests, typecheck/build and inspect localhost. Enable funding locally only after tests. Do not move the clock forward to create fake real-time salary.

## Stop condition

Real app shows zero-funded separate account awaiting September 30; deterministic tests prove subsequent salary cycles and restarts. This does not activate trading or complete the full AI-company runtime.
