# Trading verification — 2026-10-16

At the initial feature handoff, Docker ran in Paper mode with no live mandate or
autonomous live order intents. The owner alone enables AutoLive through
[the mandate screen](trading.md). Development and deterministic checks sent no
real broker orders.

## Changed areas

- [Strategy and Paper fills](../services/portfolio/Portfolio.Domain/Trading/)
- [Engine, owner controls and report delivery](../services/portfolio/Portfolio.Application/Trading/)
- [Account-isolated SQL and Bitkub adapters](../services/portfolio/Portfolio.Infrastructure/Trading/)
- [Thin trading API](../services/portfolio/Portfolio.Api/Controllers/TradingController.cs)
- [Public execution evidence](../services/market-data/MarketData.Infrastructure/BitkubExecutionMarketReader.cs)
- [Daily Brief controls](../apps/web/features/trading/)
- Team research/manual-mode boundaries, private Docker control-token preparation
  and CI checks.

## Local results

| Gate | Result |
| --- | --- |
| `npm run check`, `npm run build` | Passed |
| `npm run format:check` | Passed |
| `dotnet build Quantara.sln` | Passed, zero build warnings/errors |
| Portfolio, Portfolio API and MarketData checks | Passed |
| `npm run test:architecture`, `npm run test:orders`, `npm run test:team` | Passed |
| Services, lazy DI, AI-worker and Docker checks | Passed |
| Trading and Daily Brief browser fixtures | Passed; every trading fixture API intercepted |
| Desktop, tablet and phone inspection | Passed; no horizontal overflow |

Fake exchanges exercise entry, partial/full fills, TP/SL, fee credits, retained
cost basis, the last unfilled base unit, reconciliation, restart, uncertain
submission/cancellation, expiry during a durable claim, owner stop and capital
guards. Fake clocks and Telegram verify Bangkok midnight, one-message delivery
claims and corrections for late fills. Live end-of-day delivery and a continuous
24-hour soak were not used as evidence of these deterministic checks.

Read-only runtime checks confirmed fresh public evidence, a current heartbeat
and healthy containers. All three canonical databases received verified
COPY_ONLY/CHECKSUM backups; copied backup hashes and retained account/trade/coin
evidence were checked. Private evidence stays under ignored `.runtime/docker`.
The repository `.env` hash remained unchanged.

Paper models observed top liquidity, not exchange queue priority. Entry signals
are deduplicated by rules, symbol and completed candle; protective exits have
their own typed durable claim so a newly filled position can exit promptly.
Disabling authority does not prove existing broker orders were cancelled.

## Daily Brief redesign

Trading status is now the initial tab; Team chat and research controls are
separate. Settings open in a side panel with advanced execution fields collapsed.
Completed manual proposals, daily reports, research briefs and learning notes
remain available through disclosures. Live review keeps the exact account, rules,
limits and required expiry; date shortcuts never submit an activation.

The UI update passed lint/typecheck, production build, format checks, solution
build (zero warnings/errors), architecture, Team, Portfolio, services and lazy DI
checks. Trading, manual-order and Team browser fixtures passed with intercepted
APIs, including expiry validation, duplicate-click protection, tab/date changes,
error recovery and desktop/tablet/phone inspection. Implementation checks did not
send real orders or enable live trading.

The owner had independently enabled AutoLive before this UI deployment. Only the
web container was replaced. Portfolio, Team, MarketData and SQL container IDs
remained unchanged; account, mode, rules hash, mandate and emergency-stop state
matched before and after the update. The live heartbeat continued. A private
rollback image and runtime comparison are retained under `.runtime/docker`.
