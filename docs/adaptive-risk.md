# Adaptive risk sizing

Adaptive sizing is currently a manual-review feature. The default profile is
`Disabled`; the owner explicitly selects the `Aggressive` Crypto profile and
reviews the server-side policy constraints before proposing an order.

The current engine uses relative policy limits, completed five-minute market
data, ATR(20), a verified top-of-book quote, and a stop anchor. It sizes only
marketable spot LIMIT proposals. Sizing may use nearly all available funds,
but pending orders, manual/live exposure, stale evidence, unknown outcomes, or
uncertain account state block a proposal. Approval is immutable and the
Portfolio service revalidates account, policy, quote, stop and capital before
submission.

Account SQL remains additive and retains the audit trail and broker fills.
Read-only cash-flow history covers the available 90-day window. Equity is net
equity; unrealized PnL is not treated as realized profit. Exact TWR is
`Unknown` across cash-flow intervals when the flow-time valuation is absent.
There is no win-rate or risk ramp yet.

AutoLive has a separate versioned adaptive path, `adaptive-intraday-v1`. It
scans the eligible `bitkub-thb-spot-v1` THB universe, includes future deposits,
and sizes from actual account capital. Its default bounds are 100% exposure,
100% concentration, a 5% stop-risk scenario, 10% current-equity daily loss and
up to 16 positions. These are policy limits and scenarios, not guarantees.
Legacy fixed-symbol JSON and hashes remain compatible and are never silently
widened.

Manual adaptive proposals still require ManualReview mode and a durable
submission claim. AutoLive never calls the manual approval command. The owner
uses **Set up and review**, then one explicit **Enable Auto Trading** action
with an expiry of no more than 30 days. Account locks protect this boundary
across API hosts. Candidate scanning uses background batches of up to 16 while
the execution engine checks live state on its configured 10-second cadence.
Stale evidence and unknown broker outcomes block new entries; unknown outcomes
are reconciled read-only and never retried automatically.

If the account is already live, the owner must disable it before a Portfolio
deployment. After deployment, only the owner reviews the new policy and
re-enables live trading. This document makes no deployment or profit guarantee
claim.
