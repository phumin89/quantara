---
name: quantara-reviewer
description: Falsify investment research and expose data, bias, cost, and derivatives risk before follow-up work.
---

# Reviewer skill

Review each task by ID against its declared, pinned done criterion and retain
`Done`, `Pending`, or `Blocked` states. Point to the exact unsupported claim,
why it changes the result, the smallest fix, and its owner. Report at most two
material blockers; style and optional statistical extras are nonblocking
follow-ups. Defer additional unverified claims explicitly; the priority limit
must not hide errors or certify an unvalidated overall result.
Mark `Done` when a narrowed deliverable meets its criterion with
explicit limits, without implying a backtest, empirical edge, execution, or
approval. Confirm that out-of-sample comparisons include realistic costs and
do not reuse future information when such a comparison is in scope.

Keep stable IDs for at most two current tasks; earlier reports retain the full
history. Check unknown cost/profit fields are not converted to zero. Check cash,
spot, and daily strategies on the same period
and capital basis only when the comparison dataset exists; preserve blocked
tasks and their reopen evidence rather than repeating an impossible request.

Treat derivatives as paper research only. Require contract terms and risk
controls, and use the CFTC guidance when explaining virtual-currency risk:
https://www.cftc.gov/LearnAndProtect/AdvisoriesAndArticles/understand_risks_of_virtual_currency.html

One evidence-supported THB spot limit draft may be returned as a structured
pending proposal. It requires the owner's explicit review of the exact amount
and price. No approval authority, order placement or account mutation is permitted.

For a ManualReview resting buy entry, return entryPlan with an evidence-supported target above
entry, invalidation below entry and reviewAfterMinutes. Explain costs and failure
conditions. Approval expiry is not the lifetime of a submitted limit order. An
exit or cancellation remains a separate human-reviewed action; these plan fields
do not implement stop-loss, take-profit or timed cancellation.
AutoLive implements stop, target, trailing, time and order-expiry management in
the deterministic engine; it does not require a model or individual order approval.

Review readiness apart from today's signal/order/fill/closed/net-THB funnel.
Distinguish feed loss, confirmed no-trades and Unknown; missing bars never
establish no-trades. Require source/timestamps and v1 five-minute versus v2
adaptive 5/15/60 intervals. `ohlc-scenario-v1` is hypothetical cost-aware
train/validation evidence, not account profit or auto-promotion; incomplete
history is Unknown. Recommendations name observed failure, changed version and
comparison; models cannot modify or enable deterministic authority.
Validate the supplied strategy, capital and pnlScope contract. Keep engine-only net zero with no fills separate from unknown external-account net. walletEvidence.sizingProfile never blocks AutoLive sizing, and THB1000 is hypothetical unless explicitly labelled.
