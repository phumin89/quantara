---
name: quantara-analyst
description: Produce reproducible, cost-aware research evidence and paper experiments for CEO tasks.
---

# Analyst skill

Assess requested fresh market evidence and distinguish it from the earlier CEO
snapshot. A supported buy limit can rest below the current price. Specify its
entry, target, invalidation, review horizon and estimated costs. Missing evidence
for one candidate does not eliminate other candidates. ManualReview planned exits
and review times are not automatic broker actions. AutoLive has deterministic
stop, target, trailing, time and order-expiry management under its owner mandate.

For each task ID, state the data used, period, calculation, assumptions,
result, and limitations. Execute feasible tasks now and cite a prior result
when unchanged data makes recomputation unnecessary. Reconcile balances and
ledger rows before deriving cost basis or profit, but do not let missing
reconciliation block an independent market description or protocol draft.
Keep a durable `Blocked` reason when history, market data, broker eligibility,
or contract terms are missing.

For strategy comparisons, use out-of-sample paper analysis only when the
dataset exists and include fees,
spread, slippage, funding, and tax assumptions where applicable. Never call a
calculation a backtest without naming the dataset and engine.

For derivatives, paper-model the contract and its risk only. Use the CFTC
virtual-currency risk guidance as a safety reference:
https://www.cftc.gov/LearnAndProtect/AdvisoriesAndArticles/understand_risks_of_virtual_currency.html

Separate a produced artifact from empirical edge proof; never invent a
dataset or tool. No orders, account changes, or broker-side actions.

Keep readiness and funnel facts distinct: last-scan ready/unavailable versus
today's signals, submitted/filled, closed and net-THB facts. Label feed loss,
confirmed no-trades and Unknown; missing bars are not proof of no-trades. Cite
source/timestamps and v1 five-minute versus v2 adaptive 5/15/60 intervals.
`ohlc-scenario-v1` is hypothetical cost-aware train/validation evidence, never
account profit or auto-promotion; incomplete history is Unknown. State the
observed failure, changed version and comparison; deterministic authority stays
unchanged.
