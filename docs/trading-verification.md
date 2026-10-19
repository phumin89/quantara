# Trading verification — 2026-10-16

The owner alone enables AutoLive through [the mandate screen](trading.md).
Development and deterministic checks sent no real broker orders. These results
describe software verification, not a deployed account's mode or authority.

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

| Gate                                                                    | Result                                        |
| ----------------------------------------------------------------------- | --------------------------------------------- |
| `npm run check`, `npm run build`                                        | Passed                                        |
| `npm run format:check`                                                  | Passed                                        |
| `dotnet build Quantara.sln`                                             | Passed, zero build warnings/errors            |
| Portfolio, Portfolio API and MarketData checks                          | Passed                                        |
| `npm run test:architecture`, `npm run test:orders`, `npm run test:team` | Passed                                        |
| Services, lazy DI, AI-worker and Docker checks                          | Passed                                        |
| Trading and Daily Brief browser fixtures                                | Passed; every trading fixture API intercepted |
| Desktop, tablet and phone inspection                                    | Passed; no horizontal overflow                |

| Test evidence            | Coverage                                                                                                                                                                                                                   |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Fake exchanges           | Entry, partial/full fills, TP/SL, fee credits, retained cost basis, the last unfilled base unit, reconciliation, restart, uncertain submission/cancellation, expiry during a durable claim, owner stop and capital guards. |
| Fake clocks and Telegram | Bangkok midnight, one-message delivery claims and corrections for late fills.                                                                                                                                              |
| Limits                   | Live end-of-day delivery and a continuous 24-hour soak were not used as evidence of these deterministic checks.                                                                                                            |

- Operational checks, database-backup results and account evidence stay in ignored
  local storage such as `.runtime/docker`; they are outside this public verification record.

- Paper models observed top liquidity, not exchange queue priority.
- Entry signals are deduplicated by rules, symbol and completed candle; protective exits have their own typed durable claim so a newly filled position can exit promptly.
- Disabling authority does not prove existing broker orders were cancelled.

## Daily Brief redesign

| Area             | Behavior                                                                                                            |
| ---------------- | ------------------------------------------------------------------------------------------------------------------- |
| Initial view     | Trading status is now the initial tab; Team chat and research controls are separate.                                |
| Settings         | Open in a side panel with advanced execution fields collapsed.                                                      |
| Retained details | Completed manual proposals, daily reports, research briefs and learning notes remain available through disclosures. |
| Live review      | Keeps the exact account, rules, limits and required expiry; date shortcuts never submit an activation.              |

The UI update passed lint/typecheck, production build, format checks, solution
build (zero warnings/errors), architecture, Team, Portfolio, services and lazy DI
checks. Trading, manual-order and Team browser fixtures passed with intercepted
APIs, including expiry validation, duplicate-click protection, tab/date changes,
error recovery and desktop/tablet/phone inspection. Implementation checks did not
send real orders or enable live trading.

The verification record does not authorize or claim AutoLive activation. Rollback
artifacts and runtime comparisons belong in ignored local storage. Activation
remains an explicit owner action after the applicable evidence gates and
canonical-backup review.
