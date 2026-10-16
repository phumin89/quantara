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

AutoLive remains a separate owner-mandated capability and is unchanged. An
all-coin-universe automatic strategy remains separate from this profile and
must not be inferred from it.

Adaptive proposals require ManualReview mode at evaluation and the durable
submission claim. AutoLive activation is refused while adaptive manual orders
are outstanding; account locks also protect this boundary across API hosts.

If the account is already live, the owner must disable it before a Portfolio
deployment. After deployment, only the owner reviews the new policy and
re-enables live trading. This document makes no deployment or profit guarantee
claim.
