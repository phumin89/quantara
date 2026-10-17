# Quantara — Real-time Event-driven Trading Implementation Plan

**Baseline:** `main` @ `361893a` (2026-10-16). Implement in the existing .NET services; do not create another trading engine.

## Goal

Replace the 10-second decision polling with event-driven **BUY / SELL** for Bitkub THB Spot, 24/7 while the service is running. Keep hourly/signal-driven AI for **research only**. The deterministic strategy + Adaptive Risk Engine own execution; AutoLive uses the existing one-time owner mandate, not per-order approval.

## Implementation

1. **Market events — `services/market-data/`**
   - Reuse `LiveMarketFeed`'s Bitkub ticker/order-book WebSockets and persisted completed 5m candles.
   - Publish typed `QuoteChanged`, `BookChanged`, `CandleClosed`, `FeedUnavailable` events to Portfolio via a **private, reconnecting HTTP SSE stream**; bounded channel, per-symbol sequence/timestamps, latest-value coalescing and heartbeat. No new paid provider or broker credentials in MarketData.
   - Ensure active holdings/open orders are always in the watched set; rotate remaining candidate markets within exchange limits. Do not claim full-universe real-time coverage if subscriptions are bounded.

2. **Priority event dispatcher — `TradingWorker` / `TradingEngine`**
   - Replace the fixed `Task.Delay(10s)` *decision trigger* with a reconnecting event listener and bounded priority queue: **EXIT > order/fill reconciliation > ENTRY > universe/research**.
   - Split the existing `TradingEngine.Tick()` into small reusable decision paths; keep one account-scoped serial execution gate and durable SQL claims. Slow universe scans, AI work or report delivery must never block a fresh owned-position exit evaluation.
   - Coalesce noisy entry ticks, deduplicate by symbol + rule hash + signal/candle ID, and rate-limit re-evaluations. **Never discard a pending exit trigger**; if overflow occurs, mark symbols dirty and immediately re-read their latest quotes.
   - Keep independent timers for broker status reconciliation (existing >=30s throttle), order expiry, heartbeat, mandate expiry, daily report and a bounded fallback snapshot check when streams disconnect. Reconnect with backoff; recover state from SQL, not in-memory events.

3. **Real-time trading rules**
   - Evaluate **Stop Loss, Take Profit, Trailing Stop, time exits** on fresh bid/ask updates for owned positions; exits take priority and can use fresh quotes without waiting for a new 5m candle or full-universe scan.
   - Evaluate new entries on `CandleClosed` using existing `adaptive-intraday-v1` evidence. Optionally enable intrabar price-cross triggers **only as a separately versioned, tested strategy** supported by completed-candle context; never silently change existing rule hashes.
   - Reuse `AutonomousRiskService` / `AdaptiveRiskEngine`: size each BUY from verified current equity, available THB, existing exposure, stop/ATR, spread, fees and liquidity. New deposits scale sizing automatically; no fixed 1,000 THB order.
   - Revalidate account, mandate, market rules, balance, latest price and risk **immediately before broker submission**. LIMIT intents are not guaranteed fills or stop prices.

4. **Safety and observability**
   - Preserve `Paper`, `ManualReview`, `AutoLive`, Emergency Stop, account partitions, legacy rule hashes, frozen entry plans, and all existing ledger/history.
   - Persist intent + submission claim **before** POST; never resend ambiguous/`Unknown` outcomes. Reconcile partial fills and cancellations read-only; prevent duplicate orders on reconnect/restart/multi-host execution.
   - Fail closed for stale/missing data, invalid mandate, uncertain orders and risk breach. Expose feed health, watched/dirty symbols, event-to-decision latency, decision-to-submit latency, dropped/coalesced events, last exit evaluation and reconciliation age in Trading status.
   - Retain Thai confirmed-fill alerts, critical error alerts and 23:59 Asia/Bangkok daily report; AI cannot authorize or submit orders.

## Verification / Definition of Done

- Fake WebSocket/SSE + broker tests: tick burst, candle boundary, disconnect/reconnect, stream backlog, stale prices, multiple symbols, held coin outside candidate shortlist, stop/TP during slow scans, partial fill, uncertain POST, restart and duplicate events.
- Demonstrate in **Paper** that an owned-position exit is evaluated on the first fresh quote event, without waiting 10 seconds or for a candle close; entries remain evidence-driven and risk-sized.
- Benchmark **event received -> local decision** (p50/p95) separately from exchange/API latency; target p95 <2s under deterministic test load, report actual measured results and any misses (not a live fill guarantee).
- Run repository format/check/build, .NET and architecture checks, Portfolio/MarketData deterministic tests and applicable UI checks. Update `docs/trading.md`.
- **Do not activate AutoLive, change an existing owner's mandate, or place real orders during development/tests.** Any material rule changes require explicit owner review and a new activation; retain safe rollback and SQL compatibility.

**Codex:** Implement this plan end-to-end, in small verified commits. Reuse the current infrastructure. Report completed work, measured latency and remaining blockers; do not stop at writing another plan.
