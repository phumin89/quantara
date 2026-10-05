# Market context: shadow review slice

## Purpose

Distinguish business-supported enthusiasm from expectations running ahead of fundamentals.
Popularity alone is neither a sell instruction nor proof of mispricing. Quiet coverage is not
an automatic buying opportunity. This report is separate from trading and CFO allocation.

## Delivered boundary

`POST /research/market-context/review` accepts a symbol, evaluation timestamp (`asOf`), and
an evidence array. Every evidence item has `id`, `category` (`sentiment`, `fundamentals`,
or `valuation`), `source`, `publishedAt`, `availableAt`, and `content`.

The review needs all three categories. Source timestamps must satisfy
`publishedAt <= availableAt <= asOf`; the endpoint refuses future evaluation times.
These are caller-supplied claims, not independent verification of source authenticity.
Use the timestamp when the exact version became available, not an earlier publication
date for a subsequently revised value. Sources are references only; this endpoint does
not fetch URLs or execute source instructions.

The existing hosted gateway generates a structured assessment, rationale, counterargument,
and evidence references. Validated output is saved using the existing agent artifact store.
The report has `shadowOnly: true`; it cannot submit orders, mutate portfolios, change
funding allocations, or override RiskEngine. No active trading prompt is modified.

Explicit opt-in is required: `TradingAgents:MarketContext:Enabled=true` AND the existing
`TradingAgents:Company:Enabled=true`. The existing company model, token limits and timeout
are reused. This change does not enable either setting, supply credentials, or invoke a
paid model during development. Each enabled request may incur a model call. Do not expose
this unauthenticated development API publicly; access control and aggregate spend limits
are required before deployment.

## Limitations / next acceptance gate

- This is a backend, on-demand shadow report, not an automatic news collector or new UI.
- No social/news data subscription or historical sentiment dataset has been added.
- Missing evidence produces no inferred sentiment score. Model failures produce no trade.
- A cited evidence ID does not prove a narrative claim; source quality and interpretation
  still need review. Counterargument is from the same model call, not an independent agent.
- A modern model may remember historical events despite instructions. Historical LLM
  reviews are not certified blind backtests. Prefer forward paper observation.
- No return improvement is claimed. Next measure paired decisions (with/without context),
  premature exits, cost-adjusted returns and drawdown on timestamped forward evidence,
  before considering any influence on order proposals.
- Salary, mortgage, emergency reserves and existing backtest results are unchanged.
