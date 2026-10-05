# Forward paper company — approved integration

Status: user approved. Implementation and verification evidence are recorded in `docs/research/2026-09-14-forward-paper-company.md`. Hosted AI activation and real-money execution are not included in this approval.

## Goal and boundaries

Run a backend-owned company workflow without repeated Run clicks: month-end CFO allocations, scheduled research and specialist meetings, non-binding proposals, deterministic risk checks, paper fills and reconciliation. Closing the browser must not stop the backend. No live broker, bank connection, leverage, paid-model activation or local-AI runtime is included.

Keep the current THB 70,000 monthly salary, THB 14,000 housing and saved CFO mandate unchanged. Do not import backtest profits or the legacy THB 100,000 demo capital. Only approved investment releases fund trading. HOLD and no-trade days are valid outcomes, not failures. Positive returns are not an acceptance criterion for software correctness.

## Approaches

1. Extend the isolated forward account with an execution journal (recommended). Keeps personal reserves separate and gives cash movements and fills one auditable owner.
2. Deposit into the legacy demo portfolio. Less code initially, but mixes initial capital, historical experiments and forward deposits; rejected.
3. Build another standalone trading service. Adds deployment and reconciliation complexity without helping this bounded paper workflow; deferred.

## Account and persistence

Introduce an explicit versioned forward execution state containing source funding identity, immutable contribution receipts, paper fill/rejection receipts, positions, cash and valuation provenance. PnL is equity minus cumulative contributions, never salary or emergency reserves.

Preserve the existing v1 source and cash-only files. A one-time migration must verify every contribution against CFO postings, preserve their identifiers and amounts, create no positions and prove unchanged total cash. Retain original files for audit. Never silently reset incompatible or corrupt state. Once trading is enabled, the old cash-only synchronizer must not remain a competing writer.

Use one execution-state transaction for order identity, risk outcome, fill and resulting account. Repeated order identities return their original receipt. Crashes before commit have no account effect; crashes after commit return the saved receipt. Treasury-to-investment synchronization remains resumable by source receipt identity and is not claimed to be cross-file atomic.

## Market evidence and execution

Reuse the existing historical evidence provider, including its native prices and historical USD/THB conversion. Preserve data source, instrument, currency, FX date, observation time and availability cutoff in artifacts. Missing, future, stale or mismatched execution evidence blocks fills; it must not trigger synthetic fallback.

Separate research prices from execution quotes. A decision made after a completed bar cannot be filled retrospectively at that bar's close. In replay, execute only at the next eligible observed bar under a declared fee/slippage model; in forward paper mode, wait for suitable post-decision evidence. Unsupported sessions, instruments or corporate-action accounting remain research-only. Do not advertise broad-market execution merely because a symbol can be scanned.

## Company workflow

The backend owns scheduling and durable business-day claims. Reuse the existing ordered eight-role meeting runner and structured artifacts. Roles cannot write the account. Cache identity includes all supplied context, including portfolio, mandate, limits and earlier role outputs.

Sequence: synchronize salary releases → reconcile opening account → collect cutoff-safe evidence → CEO objective → Chief of Staff assignments → research and specialist reports → portfolio draft → risk critique → final proposal → deterministic validation → eligible paper execution → closing reconciliation and report.

Claims and execution receipts survive restart. An interrupted meeting without a verifiable terminal outcome stops for reconciliation rather than silently submitting a second set of orders. Market-data and account failures stop affected execution. Research may report why no action was possible. No requirement to trade daily.

## Risk and authority

Use the existing RiskEngine as final authority, evaluated against the current isolated account immediately before a fill. Validate holdings for sells, available cash including costs for buys, instrument policy, exposure, position count and stop conditions. A proposal or model confidence cannot override any restriction. Do not relax existing risk limits to improve a test result.

## AI activation

Keep hosted AI disabled until the user explicitly authorizes a spending ceiling. Offline acceptance tests may replace the external gateway with labeled scripted responses while exercising the real meeting runner, contract validation, risk and account code. Such tests demonstrate orchestration only, not AI investment ability or profitability. Never present scripted fixtures as live AI analysis.

## UI

Retain the existing office and operations views. Distinguish treasury allocations, cumulative contributions, unspent portfolio cash, positions, realized/unrealized PnL and pending orders. Show meeting evidence and rejection reasons. Display model-disabled, data-unavailable, awaiting-execution and reconciliation-required states truthfully. All displayed fills and balances come from saved account events.

## Acceptance and stop condition

- Controlled-clock salary posting funds only the approved allocation, once across restart and concurrent attempts.
- Actual meeting orchestration with a labeled test gateway produces valid BUY, HOLD and SELL proposals; invalid output produces no order.
- Risk rejection and missing/stale data leave cash and positions unchanged.
- A later eligible execution price, costs and FX produce hand-calculated cash, holdings and PnL.
- Repeat ticks and crash-boundary recovery never duplicate contributions or fills.
- Reconciliation detects missing receipts, incompatible identities and altered balances and stops execution.
- Existing backtests and demo portfolios remain unchanged.
- UI/API integration reflects saved outcomes, with offline versus hosted AI clearly labeled.
- Backend restart and browser-close tests demonstrate scheduling independence.
- Full relevant tests and a browser inspection pass before declaring the sandbox integration complete.

Stop implementation at this verified sandbox outcome. Live money, profit guarantees, new paid data subscriptions, banking and an autonomous strategy-optimization loop are outside this approval.
