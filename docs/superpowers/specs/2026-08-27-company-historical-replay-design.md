# Company Historical Replay Design

## Goal

Add a deterministic historical company simulation that replays Quantara Capital from 2024-01-01 through 2025-12-31 with THB 100,000 initial capital. The run must behave like leaving the autonomous paper company running, while preventing every form of look-ahead access.

## Scope

The simulation is paper-only and isolated from the persisted sandbox portfolio. It uses the existing public historical market-data providers, deterministic expert roles, strategy tournament, RiskEngine, and accounting rules. It adds a multi-asset replay coordinator, immutable audit evidence, an API endpoint, and an Operations-view simulation panel.

Live broker execution, leverage, local AI, intraday replay, historical news sentiment, and options replay are outside this increment.

## Historical Clock and Information Boundary

`HistoricalSimulationClock` advances monotonically through calendar dates. Each decision records:

- simulation date;
- decision timestamp convention;
- market-data cutoff;
- latest bar available to each symbol;
- execution date.

Agents receive data sliced at or before the recorded cutoff. A decision made after a market close may execute no earlier than the next available bar for that market. No component receives the complete future series through its decision interface.

`LookaheadAudit` verifies every decision and fill. Any observation later than its cutoff, or any fill earlier than its permitted execution date, invalidates the whole run. Invalid runs retain diagnostic evidence but never present their profit as valid.

## Universe and Calendars

The first replay uses the current explicitly configured sandbox universe supported by the market-data provider. Symbols without sufficient data are skipped and reported; their absence does not silently change prior decisions. US equities and THB crypto keep separate available-bar calendars. The coordinator acts only on bars available for each symbol at that replay date.

Because point-in-time index membership is not yet available, the UI labels this as a fixed sandbox universe rather than claiming a survivorship-bias-free S&P 500, Nasdaq-100, or SET50 constituent replay. Cash remains a valid allocation.

## Company Workflow

The replay models distinct responsibilities instead of running every role on every clock tick:

1. Treasury initializes the isolated THB 100,000 mandate.
2. CEO establishes capital-preservation and return objectives at simulation start and at periodic reviews.
3. Chief of Staff creates the daily agenda and assigns only due work.
4. Market Research updates the eligible universe from point-in-time price evidence.
5. Equity & Crypto Analysis calculates technical evidence.
6. Quant Research performs walk-forward validation using only the visible historical slice.
7. Portfolio Manager ranks promoted candidates and proposes target allocations or HOLD.
8. RiskEngine deterministically approves, resizes, or rejects every proposed order.
9. Paper Trader executes approved orders on the next eligible bar with configured fees and slippage.
10. Controller reconciles cash, positions, equity, and invariants after fills.
11. Audit records human-readable summaries but cannot mutate the ledger.

The default cadence is daily market-data advancement, weekly portfolio review, and monthly mandate review. Risk and execution run only when proposals exist. No-trade days are explicit outcomes.

## Portfolio and Risk

The replay is long-only, unlevered, and cash-backed. Existing deterministic RiskEngine remains final authority. The simulation maintains an isolated multi-asset ledger with cash, positions, cost basis, realized PnL, marked equity, fees, slippage, and drawdown.

Candidate strategies remain cash, SMA trend, breakout, and mean reversion. Walk-forward promotion requires positive out-of-sample net return after costs and compliance with the existing drawdown and minimum-trade gates. Portfolio construction may allocate only to promoted candidates. If evidence is insufficient or risk fails, capital stays in cash.

## Results and Audit Artifacts

`CompanySimulationResult` contains:

- start, end, initial capital, final equity, total PnL, and return;
- maximum drawdown, fees, slippage, trade count, HOLD-day count, and rejected proposal count;
- daily equity curve;
- per-symbol contribution and final positions;
- cash and buy-and-hold benchmark evidence where comparable;
- workflow timeline with role, task, decision, cutoff, and execution date;
- skipped-symbol diagnostics;
- look-ahead verification status and violations.

Results are historical paper evidence, not a prediction or profit guarantee.

## API and UI

Add `POST /simulations/company` accepting optional start, end, and initial capital. Defaults are 2024-01-01, 2025-12-31, and THB 100,000. Input validation rejects inverted dates, future end dates, non-positive capital, and excessively long runs.

Operations view gains a `Company Simulation` card with the fixed defaults and a `Run 2024-2025 simulation` action. While running, it shows progress and current historical date. On completion it shows final equity, PnL, return, maximum drawdown, fees, trades, HOLD days, benchmark, equity curve, per-symbol contributions, and a workflow timeline. Valid results show `NO LOOKAHEAD VERIFIED`; invalid results show `INVALID` and suppress profit emphasis.

The simulation does not change autonomous-company state or the persisted paper portfolio.

## Failure Handling

- Market-data failure for one symbol: skip it, record reason, continue if at least one asset remains.
- No eligible assets: complete validly in cash with explicit reason.
- Accounting invariant failure: invalidate and stop the run.
- Look-ahead violation: invalidate and stop the run.
- Request cancellation: stop without publishing a completed result.
- Concurrent simulation request: reject while one run is active to protect resources and event ordering.

## Testing

Domain tests prove clock monotonicity, cutoff slicing, next-bar execution, no leverage, cash conservation, fee accounting, drawdown, HOLD behavior, and deliberate look-ahead invalidation. Application tests prove role scheduling, event order, isolated portfolio state, skipped-symbol handling, and deterministic repeatability. API tests prove defaults and validation. Web tests or build-time component checks cover valid, invalid, loading, and error states.

Acceptance requires all existing tests plus new deterministic tests, clean API and web builds, a successful Docker run, a completed real-public 2024-2025 replay, unchanged persisted sandbox portfolio, and visible results in Operations view.
