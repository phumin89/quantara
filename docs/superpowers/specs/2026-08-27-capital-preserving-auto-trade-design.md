# Capital-Preserving Auto Trade Design

## Goal

Quantara V1 runs unattended as a paper-trading company using real public market data. It seeks long-term growth but defaults to cash when evidence is weak. No design can guarantee profit or prevent every loss; V1 limits loss deterministically and fails closed.

## Mandate

- Initial capital: THB 100,000.
- Paper trading only. No live broker execution path.
- No leverage, short selling, naked options, or AI authority over money.
- Long options are defined-risk: maximum 0.5% equity loss per trade and 5% aggregate option loss.
- Company protection begins at 5% loss. A 10% drawdown activates the kill switch.
- Trading is optional. Cash is a valid allocation.
- `RiskEngine` remains final authority. Model/agent output is untrusted context only.

## Strategy Validation

Create deterministic strategy candidates behind one interface: SMA trend, price breakout, mean reversion, and a higher-allocation trend-regime candidate with a bounded rolling trailing exit. Every simulation includes configurable fees and slippage. A walk-forward evaluator supplies pre-validation bars only as signal warmup, then scores the candidate across the entire unseen validation window.

A strategy is promotable only when all gates pass on out-of-sample data:

- positive net return after costs;
- maximum drawdown no greater than 10%;
- at least two completed round trips, avoiding promotion from one lucky trade;
- positive return-to-drawdown score;
- no validation error or incomplete market data.

If no candidate passes, selection is `Cash`; the company records HOLD and creates no order. Buy-and-hold and cash results remain benchmarks, never automatic promotion criteria that force risk-taking.

## Autonomous Flow

For each completed daily bar, Market Research loads real history. Quant Research runs walk-forward evaluation. Portfolio Manager may act only on the selected promotable strategy. `RiskEngine` validates each proposal against the shared portfolio. Paper Trader writes fills. Controller reconciles the ledger. Duplicate-bar and post-sale cooldown rules remain.

Historical validation failure, unavailable data, no eligible strategy, stale bar, or thrown exception produces no trade. Company-level loss/drawdown checks can pause future buys without blocking risk-reducing sells.

## Evidence and UI

Company status exposes strategy name, validation state, out-of-sample return, maximum drawdown, trade count, cash benchmark, buy-and-hold benchmark, fee/slippage assumptions, and rejection reason. Operations view labels results as paper evidence, not predictions or guaranteed profit.

An explicit deterministic stress evaluation replays the active champion over four fixed two-year regimes: 2018–19, 2020–21, 2022–23, and 2024–25. Every regime runs at normal costs and doubled costs. The report exposes return on deposits, a display-only unconstrained fractional market reference, an executable passive benchmark, alpha over that executable benchmark, contribution-adjusted drawdown, trades, accounting validity, lookahead validity, and failed gates. The executable benchmark receives the same cash flows and costs, holds at most four warmup-liquidity-selected symbols, applies the same 19% target allocation per position, uses whole equity units and fractional crypto units, and never uses replay-period data for selection. A scenario passes only when return and executable alpha are positive, drawdown is at most 25%, accounting reconciles, and lookahead remains clean. All eight scenarios must pass before the report may say `STRESS_PASSED`; failure never mutates the champion or paper portfolio.

## Acceptance

- Deterministic tests prove no candidate means cash/HOLD.
- Tests prove future bars cannot affect earlier strategy selection.
- Tests prove costs reduce returns and can reject a candidate.
- Tests prove drawdown over 10% rejects promotion.
- Autonomous company consumes validated selection rather than raw one-year SMA profit.
- Existing deterministic `RiskEngine`, shared paper ledger, Bitkub THB data, US/SET data, options caps, and kill switch remain intact.
- API build, domain tests, web type-check/build, and a real-public BTC-THB smoke test pass.
- Stress evaluation uses the full configured sandbox universe, fixed cash flows, fixed periods, and both normal and doubled costs without changing the active strategy.

## Non-goals

- Live orders, broker credentials, deposits, withdrawals, leverage, guaranteed profit, LLM/local-AI runtime wiring, intraday/HFT, or automatic strategy code generation.
