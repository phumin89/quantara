# Quantara — Implementation Plan: 24/7 Rule-Based Trading

**Repo:** `phumin89/quantara` · reviewed `main@0beb475` (2026-10-15)

## Goal

Quantara trades **Bitkub THB spot automatically, 24/7**, using deterministic, owner-configured rules. **The owner approves the live-trading mandate once at activation; there is NO per-order Approve/Review thereafter while the mandate remains valid.** The owner primarily receives one **Thai daily report at 23:59 Asia/Bangkok**, with critical alerts when needed. Codex/LLMs may analyze and report but **never choose or authorize real-money orders**.

## Reuse existing code

- **MarketData:** `LiveMarketFeed.cs`, `IntradaySignalEvaluator.cs`, `AdaptiveMarketEvaluator.cs` — live quotes, 5-minute completed candles, EMA/ATR, breakout signals.
- **Portfolio:** `BitkubOrderBroker.cs`, `BitkubOrderInspector.cs`, `SqlOrderStore.cs`, `OrderStatusMonitor.cs` — signed exchange requests, `client_id`, durable claims, reconciliation. Keep `Unknown` outcomes blocked; never blindly resend.
- **Team:** `CompanyLoop.cs`, `TeamCoordinator.cs` — research only; not order authority.
- **Legacy approval:** `ApproveOrderHandler.cs`, `TelegramOrderReviews.cs` — preserve as a separate, optional manual mode; **not on the autonomous trading path**.
- **Runtime/UI:** `compose.yaml`, `docs/docker-runtime.md`, `DailyBriefPage.tsx`. **Use existing SQL Server databases**, not PostgreSQL; preserve retained data/account isolation.

## Implement in order

### 1. Trading engine + paper mode

- Add a **Portfolio-owned .NET BackgroundService**, independent of browser, Team and Codex, consuming MarketData via HTTP.
- Evaluate versioned, deterministic **buy / sell / wait** rules on completed 5-minute candles; monitor broker orders and positions more frequently. Add configurable entry, position sizing, take profit, stop loss, trailing stop, time exit, cooldown, fees, spread/slippage and market minimums.
- Build one explicit initial strategy from existing indicators and validate it; **do not blindly turn every research alert into a trade**.
- Modes: `Stopped`, `Paper`, `ManualReview` (existing behavior), `AutoLive`. **Default to Paper**. Paper mode models fees, fills, partial fills and unfilled limit orders; no real orders.

### 2. State, risk and recovery

- Add **additive, per-account SQL tables** for rule versions, approved mandate, signals/decisions, order intents, fills, positions, risk counters and report delivery. Do not reset existing history.
- Owner-configurable caps: total allocated THB, per-order amount, allowed pairs, max simultaneous positions, daily loss, spread/slippage, data freshness, mandate expiry and emergency stop. **Spot only; no leverage, withdrawals or transfers.**
- One durable intent per strategy/symbol/candle; one execution claim per intent. Handle reserved balances, partial fills, order cancellation and exact exchange reconciliation after crashes/restarts. Never double-buy, oversell or retry uncertain POSTs.
- Fail closed when state, identity, authorization, available funds, market data or exchange status is uncertain. Stop **new entries** and alert; do not assume exits or stops executed without broker confirmation.

### 3. One-time live activation; autonomous execution thereafter

- Add a dedicated `AutoLive` server-side execution path. The owner reviews **account + strategy version/hash + symbols + max capital + risk limits + expiry**, then presses **Enable Auto Trading ONCE**. Persist an auditable mandate tied to that exact configuration.
- **After activation:** the deterministic worker independently places, tracks, cancels and exits orders **within the mandate 24/7 with no Approve/Review for each trade**. It must not call `/approve`, simulate Telegram clicks, forge `Confirmed=true`, or give exchange credentials to AI/Team.
- Changing material rules, account or risk caps, mandate expiry, owner disable or emergency stop disarms `AutoLive`; reactivation is a **single owner action** on the new mandate, **not approval per trade**. ManualReview continues to work independently.
- Implement separate broker cancellation/exit handling only against verified Bitkub API behavior; maintain idempotent client IDs and accurate fill/fee accounting. Follow venue/platform requirements; **do not bypass any mandatory external per-order control**. If required by the venue, surface that exact blocker rather than secretly circumventing it.
- **During development:** implement and exercise this path with fake/shadow exchange only. Never enable `AutoLive`, place a real order or change the live account as part of Codex implementation. After successful checks, **the owner alone activates it once in the application**.

### 4. Daily reporting + controls

- Portfolio computes **verified realized gross/net P&L**, fees, open-position/unrealized P&L, cash and equity from actual fills and cost basis. Unknown/incomplete results remain **Unknown**, not zero or invented profit.
- At **23:59 Asia/Bangkok**, send **one concise Thai Telegram summary** and show it on Daily Brief: starting/ending equity, buy/sell activity, fills, net profit, fees, win/loss, open orders/positions, skipped signals, faults and operating mode. Persist delivery state; prevent duplicates; handle late fills with corrected reports.
- Immediate Telegram alert **only** for emergency stop, risk breach, uncertain submission, reconciliation mismatch, stale feed or prolonged outage.
- UI: Paper/ManualReview/AutoLive status, mandate configuration, **Enable once / Disable / Emergency Stop**, current exposure, open orders, last heartbeat. Secure these endpoints; keep credentials only in Portfolio.
- Use current Docker Compose deployment and health checks; document that a powered-off/sleeping PC, lost network or stopped Docker **cannot trade**.

## Acceptance criteria

- **No per-trade approval in enabled `AutoLive`.** Owner approval is **one-time per unchanged, unexpired mandate**.
- Deterministic rules alone send live orders; AI/Codex cannot execute or grant authority. Existing manual review still works.
- Simulated end-to-end entry → partial/full fill → take-profit/stop-loss → reconciliation → daily report passes, including duplicate events, uncertain POST, restart, Bangkok midnight and owner stop.
- CI/Paper/shadow tests make **zero real Bitkub POST requests**. `AutoLive` is **OFF after implementation** until the owner explicitly enables it.
- All retained databases and account boundaries remain intact; report P&L from evidence, never guesses.

## Codex instructions

1. Read `AGENTS.md`, `docs/architecture.md`, `README.md`, `docs/docker-runtime.md` and current source first; apply repo conventions (C# CQRS/Mediator, one type per file, class-based React, SQL Server ownership).
2. **Implement working code in the order above**, not another plan. Add deterministic Portfolio/MarketData/UI tests; preserve safe, existing behavior.
3. Run formatter, `npm run check`, `npm run build`, `dotnet build Quantara.sln`, `npm run test:architecture`, `npm run test:orders`, `npm run test:team` and relevant Portfolio/MarketData checks. Report actual results.
4. Report changed files, paper-test results, **how the owner activates AutoLive once**, and blockers. **Do not activate live trading or submit real orders during this task.**
