# Deterministic spot trading

Quantara's Portfolio service owns the deterministic trading worker, its
account-partitioned SQL state and broker credentials. MarketData supplies public
market evidence over HTTP. Team and Codex produce research; they cannot enable
AutoLive, select its orders or submit broker transactions.

## Modes and owner activation

New trading state defaults to **Paper** with simulated funds, fees and limit
fills. **Stopped** disables trading. **ManualReview** preserves the existing
individual proposal approval workflow. **AutoLive** uses the deterministic
strategy under a single owner mandate; it has no per-trade Approve/Review step.
Implementation and verification leave live activation to the owner and send no
real broker orders.

To activate after the deployment and verification have been reviewed:

1. Open **Daily Brief → Trading → Configure rules and limits**. Review the
   strategy version, allowed THB spot symbols, capital allocation, per-order
   size, simultaneous positions, daily loss cap, spread/slippage, data age,
   take profit, stop loss, trailing stop, time exit, cooldown, order lifetime,
   fee model and entry volume ratio. Save changes first.
2. Open **Review live mandate**. Verify the exact account, strategy/rules hash,
   symbols and every capital/risk cap. Choose an explicit future expiry within
   the next 30 days. Fresh markets, verified available balances and server
   control capability are required; a rejected activation stays visible.
3. The owner alone presses **Enable Auto Trading** once. The persisted mandate
   binds this exact configuration and expiry. The deterministic Portfolio
   worker may then enter, track, cancel and exit within that mandate without
   per-order confirmation. Closing the browser does not stop the worker.

Changing material rules, account or risk caps, mandate expiry, **Disable** and
**Emergency stop** disarm live authority. Re-enabling requires another explicit
owner action on the current configuration. Expiry never renews authority.
AI output, schedules, restarts and Telegram mock buttons cannot enable it.

## Risk, recovery and accounting

Trading is THB spot LIMIT only, with no leverage, transfers or withdrawals.
The initial version is `ema20-50-breakout-v1`; research alerts are not trading
instructions. Rule settings are constraints, not a proven return or guaranteed
exit price.

The initial strategy enters only when the latest completed five-minute candle
closes above the previous 20 candles' highs, seeded EMA20 is above EMA50, and
volume is at least the configured multiple of the previous 20-candle median
(the median must be positive). It needs at least 51 contiguous completed bars
and uses the latest contiguous suffix of up to 120; older gaps do not invalidate
a sufficient recent window. Owned positions are checked between candle closes for stop
loss, take profit, trailing stop and time exit. The defaults are 1%, 2%, 1% and
120 minutes respectively; they trigger LIMIT intents rather than guaranteed
fills. Daily loss includes today's verified realized net plus current unrealized
result and blocks new entries, while valid mandated exits remain eligible.

The allocation cap covers open position cost and reserved entry cash. Per-order
caps also constrain planned sell notional, so a larger exit can require more
than one candle. Splits preserve a market-minimum remainder where possible;
venue minimums and precision can still leave dust. Paper consumes only observed
top-of-book liquidity once per fresh snapshot, estimates fees and expires
unfilled limits. It does not reproduce exchange queue priority.

Only new fills belonging to this strategy enter its live book. Existing manual
holdings and orders retain their original history and are not silently assigned
an autonomous cost basis or exit authority. Daily reports cover this strategy's
book; whole-account P&L remains Unknown when legacy cost evidence is incomplete.

SQL storage adds account-keyed rule versions, mandates, decisions, order intents,
fills, positions, risk counters, alerts and report delivery alongside the
existing Portfolio ledger. Team and MarketData retain their own databases.
Execution claims persist before broker requests. Duplicate events, browser
retries and restarts never authorize another submission of an uncertain intent.
Unknown broker outcomes remain blocked until exact read-only reconciliation.

Stale or inconsistent market data, unknown execution state, insufficient
available funds and risk/identity failures stop new entries and expose a fault.
Disable and Emergency stop do not prove outstanding orders were cancelled or
positions closed. Inspect actual broker evidence and reconcile before resuming;
never treat an assumed stop-loss fill as an executed exit.

Daily Brief displays mode, current position cost exposure, open orders, cash,
equity and last heartbeat. Realized gross/net P&L and fees come from actual fills
and known cost basis; incomplete values remain **Unknown**. The concise Thai
summary runs at **23:59 Asia/Bangkok**, with retained delivery state and report
revisions for late evidence. Select the Bangkok date in Daily Brief to read it.
Critical Telegram alerts cover emergency stops, risk breaches, uncertain
submissions, reconciliation mismatch, stale feeds and prolonged outages.

## Private server configuration

The canonical [Docker preparation](docker-runtime.md) generates a 32-byte
random owner-control token in `.runtime/docker/runtime.env`, restricts that file
to its owner and preserves existing SQL credentials and the token on repeated
runs. Portfolio receives it as `Trading__OwnerToken`; only the Next.js server
receives `QUANTARA_TRADING_CONTROL_TOKEN`. The guarded server proxy adds the
owner header. Team, model workers and browser code never receive it.

Do not edit the repository `.env` to add the control token, print private
environment files or expose the application beyond its loopback deployment.
Missing server configuration fails closed; configuration alone grants no
mandate. Preparation and normal startup preserve existing containers; apply
updates only through reviewed maintenance with current canonical-data backups.

A powered-off or sleeping PC, lost network, stopped Docker or stopped/unhealthy
service cannot trade. Compose restart policies and health checks do not provide
uninterrupted availability or guarantee execution during an outage.

## Verification

Run deterministic Portfolio/MarketData checks and
`node tools/verify-trading-ui.cjs`. Add `--browser` with the frontend running for
isolated editable-rule, mandate, error-recovery and responsive fixtures. All
fixture API calls are intercepted before navigation. `tools/verify-docker.ps1`
checks token generation/preservation, owner-only permissions, service isolation
and retained runtime guards without starting Docker or reading account secrets.
Real SQL or account integration remains explicitly opt-in. Never use the live
Enable button or a real broker POST to verify an implementation.
