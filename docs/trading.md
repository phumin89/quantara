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

The adaptive path scans the versioned `bitkub-thb-spot-v1` THB universe and
sizes from verified account capital, including future deposits. It does not
require symbols, allocated THB or a fixed per-order amount. Its default bounds
are Aggressive, 100% exposure, 100% concentration, a 5% stop-risk scenario,
10% current-equity daily loss and up to 16 positions. These are policy limits,
not guarantees.

To activate after the deployment and verification have been reviewed:

1. For legacy rules, open **Daily Brief → Trading status → Trading settings**.
   For adaptive trading, press **Set up and review**; this prepares the
   versioned policy and opens review without activating it. Review its market
   scope, relative limits and inclusion of future deposits. Legacy settings retain
   the strategy version, allowed THB spot symbols, capital allocation, per-order
   size, simultaneous positions, daily loss cap, spread/slippage, data age,
   take profit, stop loss, trailing stop, time exit, cooldown, order lifetime,
   fee model and entry volume ratio. Strategy and execution values are in
   **Advanced settings**. Save changes first; saving disarms live trading.
2. Open **Review live mandate**. Verify the exact account, strategy/rules hash,
   universe or symbols and every capital/risk cap. Choose an explicit future expiry within
   the next 30 days, or choose a 1-, 7- or 30-day shortcut. These shortcuts
   only fill the expiry; they never activate trading. Fresh markets, verified available balances and server
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
Legacy uses `ema20-50-breakout-v1`; adaptive uses `adaptive-intraday-v1`.
Research alerts are not trading instructions. Rule settings are constraints,
not a proven return or guaranteed exit price.

The legacy strategy enters only when the latest completed five-minute candle
closes above the previous 20 candles' highs, seeded EMA20 is above EMA50, and
volume is at least the configured multiple of the previous 20-candle median
(the median must be positive). It needs at least 51 contiguous completed bars
and uses the latest contiguous suffix of up to 120; older gaps do not invalidate
a sufficient recent window. Owned positions are checked between candle closes for stop
loss, take profit, trailing stop and time exit. The defaults are 1%, 2%, 1% and
120 minutes respectively; they trigger LIMIT intents rather than guaranteed
fills. Daily loss includes today's verified realized net plus current unrealized
result and blocks new entries, while valid mandated exits remain eligible.

In the legacy path, the allocation cap covers open position cost and reserved entry cash. Per-order
caps also constrain planned sell notional, so a larger exit can require more
than one candle. Splits preserve a market-minimum remainder where possible;
venue minimums and precision can still leave dust. Paper consumes only observed
top-of-book liquidity once per fresh snapshot, estimates fees and expires
unfilled limits. It does not reproduce exchange queue priority.

Adaptive mode adds versioned trend pullback and range rebound entries, including
resting limits below the current ask. It ranks candidates by planned net reward
after fees, spread and slippage divided by structural stop distance; this is a
deterministic priority score, not a return forecast. Owned exits take priority.
Structural stops and ATR-derived targets/trailing distances are frozen with the
entry. Adaptive exits use the full owned quantity, exchange precision and minimums,
without the legacy entry notional cap. A frozen buy side or an entry spread limit
does not prevent a permitted sell. Unfilled entries expire or are cancelled on
verified invalidation; cancellation uncertainty prevents replacement.

The engine consumes priority market events. Background
research rotates detailed candle/depth batches of sixteen across the eligible
universe and isolates missing market evidence. Fresh owned-position quotes do not
wait for candle history or the background account scan. Broker status checks retain
their separate thirty-second minimum interval. Coverage and timestamps expose
actual progress; the whole universe is not refreshed simultaneously.

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
Manual and autonomous submission claims share the account lock. AutoLive refuses
activation with uncertain manual submissions or outstanding adaptive proposals;
manual approval requires AutoLive to be disabled first. Notification state writes
share the engine gate, while Telegram network calls release it.

Stale or inconsistent market data, unknown execution state, insufficient
available funds and risk/identity failures stop new entries and expose a fault.
Disable and Emergency stop do not prove outstanding orders were cancelled or
positions closed. Inspect actual broker evidence and reconcile before resuming;
never treat an assumed stop-loss fill as an executed exit.

Daily Brief displays mode, current position cost exposure, open orders, cash,
equity and last heartbeat. Realized gross/net P&L and fees come from actual fills
and known cost basis; incomplete values remain **Unknown**. The concise Thai
summary is retained at **23:59 Asia/Bangkok**, with report revisions for late
evidence. Select the Bangkok date in Daily Brief to read it. In adaptive mode,
Telegram receives concise Thai confirmed-fill notes: coin, quantity, and actual
cash paid/received including fees. Submission alone never announces a purchase;
duplicate broker fills never create another note. Daily summaries stay in the app.
Legacy report delivery is retained for compatibility.
Critical Telegram alerts cover emergency stops, risk breaches, uncertain
submissions, reconciliation mismatch, stale feeds and prolonged outages.

The daily net objective is **THB 500**, measured from verified strategy sell fills,
never a guaranteed profit or a reason to bypass entry checks. The optional
opportunity review uses `observed-book-v1`: at most eight concurrent hypothetical
samples, a seven-day model window, a reference budget of ten exchange minimum
notionals, subsequent observed quotes, visible liquidity and fees. Candle touches
alone never fill a sample; missing exits remain Unknown. These samples and their
mean net returns are not actual fills or missed account profits. Team receives
only bounded, account-isolated performance evidence for its daily lessons; it
cannot change a live policy or promote a strategy itself.

## Compatibility and rollback

Legacy rules retain their exact hash and omit inactive additive JSON properties.
Deploying this build never converts an old mandate to all-market authority. New
state still starts in Paper. Existing owned fills, account keys and SQL records
are retained; only explicit owner setup saves the new policy and revokes old
authority. Verified cash flows affect funding once, not trading profit.
An unexplained strategy-cash shortfall blocks submission until funding evidence
is reconciled; a higher account balance alone never invents a cash flow or profit.

Before owner setup, the legacy payload remains readable by the previous build.
After `adaptive-intraday-v1` is saved, rollback requires a build that understands
that policy and its additive records. Do not replace it with a legacy-only binary,
rewrite history or revive a revoked/expired mandate. Disable live trading before
maintenance, retain canonical backups, and leave reactivation to the owner.

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

## Real-time market decisions

MarketData reuses its public Bitkub WebSockets and SQL-owned completed five-minute
candles. Portfolio subscribes to a private authenticated SSE stream of typed
`QuoteChanged`, `BookChanged`, `CandleClosed`, `FeedUnavailable` and heartbeat
events. No broker keys enter MarketData. Public order books retain actual bid/ask
quantities and the older side's timestamp; incomplete, crossed or disconnected
books never become executable prices. Candle history is not synthesized.

Holdings and active orders in both retained books are required watches, including
buy-frozen markets that still permit selling. Up to 16 remaining candidates rotate
every 30 seconds, within a total limit of 64 watched symbols. Required watches
refresh every two seconds and retain their socket sessions across rotation.
This is bounded real-time coverage, not simultaneous coverage of every listing.
The separate fair universe scan still checks other eligible markets.

Each SSE subscriber has a bounded 256-key coalescing buffer. Portfolio has a
128-event priority queue, at most 64 dirty symbols and three reserved timer lanes.
Owned exits run before reconciliation, completed-candle entries and research.
Due reconciliation gets a turn after at most 16 consecutive owned events so
continuous quote traffic cannot starve fill accounting. Received owned quotes
retain their decision context within the bounded queue; preflight uses the latest
quote. A delayed fallback snapshot cannot overwrite a stream update received
after that snapshot request began.
Overflow retains owned symbols as dirty and immediately schedules a fresh quote
read. Candidate updates refresh cached prices without causing intrabar buys.
Per-symbol/kind sequences reject duplicate or older frames; reconnect rebuilds
the watch from SQL, and durable rule/symbol/candle intent keys remain authoritative.

Fresh owned bid/ask events evaluate stop loss, take profit, trailing and time exits
without a ten-second poll or a new candle. `adaptive-intraday-v1` entries still
require completed-candle evidence and existing risk sizing. Slow universe/capital
scans and broker status reads run outside the serial account decision gate;
broker submission itself remains serialized and can wait for the exchange.
Final market freshness, mandate, account, balance and risk guards, durable claims,
partial-fill accounting and unknown-outcome no-retry rules remain in force.

The private stream heartbeats every 15 seconds, times out after 45 seconds without
a valid frame and reconnects with one-to-30-second backoff. Handshake is bounded
at 20 seconds. Disconnect invalidates cached prices. Independent timers schedule
maintenance/expiry every five seconds, research every minute, read-only broker
inspection at least 30 seconds apart, and a 30-second owned-quote fallback check.
Reports and confirmed-fill delivery run independently every 30 seconds. Thai
confirmed-fill notes and critical alerts retain their existing delivery claims;
the 23:59 Bangkok daily report stays in the app for adaptive mode.

Daily Brief shows private-stream connectivity and watched count. Collapsed
diagnostics expose watched/dirty symbols, coalesced/dropped triggers, rolling
received-to-local-decision p50/p95 (512 samples), latest decision-to-submit age,
and exit/reconciliation timestamps. Stream connectivity alone does not prove
every public market is healthy or an order filled. Latency excludes exchange/API
round trips and is not a fill guarantee; fake-load measurements are reported
separately from live execution.

Docker preparation also generates and preserves an independent random
`QUANTARA_MARKET_EVENTS_TOKEN` in private `runtime.env`. Only MarketData and
Portfolio receive `MarketEvents__Token`. Native development must privately supply
the same setting to those two processes; missing configuration fails closed.
No schema, persisted rule hash or owner mandate is changed by this transport.
Roll back only to an adaptive-policy-compatible build, keep all canonical data
and private tokens, and disable live trading before replacing running containers.

## Verification

Run deterministic Portfolio/MarketData checks and
`node tools/verify-trading-ui.cjs`. Add `--browser` with the frontend running for
isolated editable-rule, mandate, error-recovery and responsive fixtures. All
fixture API calls are intercepted before navigation. `tools/verify-docker.ps1`
checks token generation/preservation, owner-only permissions, service isolation
and retained runtime guards without starting Docker or reading account secrets.
Real SQL or account integration remains explicitly opt-in. Never use the live
Enable button or a real broker POST to verify an implementation.

Fixture measurement on 2026-10-17 (Windows, fake storage/quotes/broker): 100 fresh
owned events while research was stalled produced local-cycle p50 **0.017 ms** and
p95 **0.054 ms**, below the two-second target. The running Paper worker recovered
its required watch and filled its first streamed exit in **25.0 ms**. The suite
also covers stop/target/trailing/time exits, queue overflow, delayed snapshot
races, reconciliation fairness, SSE timeout/reconnect, partial fills, restart and
unknown submission no-retry. These measurements exclude real SQL, exchange/API
and network latency; they do not establish production execution or fill latency.
