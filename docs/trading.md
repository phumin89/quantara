# Deterministic spot trading

Quantara's Portfolio service owns the deterministic trading worker, its
account-partitioned SQL state and broker credentials. MarketData supplies public
market evidence over HTTP. Team and Codex produce research; they cannot enable
AutoLive, select its orders or submit broker transactions.

## Unconfirmed order status

Crypto shows active orders before ledger details. **Review orders** opens this
section; it does not enable trading, send an order or repeat a cancellation.
Amounts and remaining amounts use THB for buys and the base coin for sells.

| Evidence                                        | Display and meaning                                                                                          |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Submission or cancellation outcome is uncertain | AutoLive displays **Enabled · Blocked** while its mandate remains enabled.                                   |
| A recorded inspection attempt                   | Its own timestamp and latest safe failure message appear beside the order. The engine heartbeat is separate. |
| No recorded inspection timestamp                | **No check time recorded**; this does not prove that the order was never checked or sent.                    |
| Original strategy decision                      | Retained under **Order details**, separate from inspection failures and broker identity.                     |

The optional `LastInspectionError` field is omitted from retained JSON when null.
Records with supported states remain readable. Verified broker results clear an earlier inspection
error; failed checks retain the unknown outcome and never authorize a resend.

### Stored state validation

The SQL state boundary accepts only the exact intent states `pending`, `submitting`,
`submitted`, `partial`, `unknown`, `filled`, `cancelled`, `rejected` and `expired`.
Cancellation is either absent or exactly `claimed`, `unknown`, `acknowledged` or
`confirmed`. Both Paper and Live books use this validation, including the runtime
read in the manual-sale journal transaction. Supported values and retained
uncertainty are preserved; case variants are never normalized into broker outcomes.

An unsupported stored value makes trading state unavailable. Engine decisions and
control requests, including Disable/Emergency stop, then fail before recording a
change. This does not revoke a saved mandate or cancel existing broker orders.
Retain the exact data for investigation; never guess a supported value to clear the
failure. No malformed retained account record has been established by this review.

## Modes and owner activation

| Mode             | Behavior and authority                                                                                 |
| ---------------- | ------------------------------------------------------------------------------------------------------ |
| **Paper**        | Default for new trading state, with simulated funds, fees and limit fills.                             |
| **Stopped**      | Disables trading.                                                                                      |
| **ManualReview** | Preserves the existing individual proposal approval workflow.                                          |
| **AutoLive**     | Uses the deterministic strategy under a single owner mandate; it has no per-trade Approve/Review step. |

Implementation and verification leave live activation to the owner and send no real
broker orders.

Manual proposal amount, limit, target and invalidation inputs must be exactly
representable as decimals. Team validates its four fields; Portfolio independently
validates new pending-order amounts and limits before reading the broker or saving
a proposal. Values requiring rounding are rejected with a precision error. Equivalent
leading or trailing zeros remain accepted, and separate market step/scale limits
still apply. This validation never approves or submits an order.

Read-only order reconciliation also binds the broker's reported original amount
and order rate to the saved proposal or intent. This check follows a linked
order's `first` reference before accepting its terms; the AutoLive adapter checks
its separate follow-up response independently. A mismatch, including extra
precision that would round to the saved value, cannot clear an unresolved order.
The first response must identify the requested child before its `first` reference
can be followed; an unrelated response cannot supply trusted order lineage.
Numeric/string forms, scientific notation and equivalent zero padding are
supported. Actual fill prices may improve on the limit, and a known broker ID may
retain an empty legacy client reference. This does not identify the cause of an
existing account's balance-review or enablement failure.

AutoLive fill transaction IDs accept nonblank strings and the existing JSON-number
form, retaining their exact text and 150-character limit. Objects, arrays, Booleans,
null and whitespace-only IDs cannot become verified fills or notification keys.
This validation never places or resends an order.

Native broker numbers must be exactly representable as decimals before use in
wallet balances/imports, manual-sale proofs, risk evidence, market limits and
order/fill inspection. The shared reader checks the original coefficient and
exponent, rejecting [implicit rounding](https://learn.microsoft.com/en-us/dotnet/api/system.decimal.parse),
nonzero values rounding to zero and NUL text.
Exact signed values and zero remain supported where the caller already permits
them. Each path keeps its prior numeric grammar; AutoLive inspection still accepts
decimal-point forms only. Original order amount/rate matching stays positive-only.
This check validates incoming evidence and never rewrites retained records.

When no broker ID was retained, recovery checks matching client references in
open orders and complete history pages, then verifies each candidate's canonical
original order. Repeated execution rows and linked children may identify one
original; independent originals never resolve by choosing the first match.
Every response must match the requested ID and any nonempty client reference
before its `first` link is trusted. Conflicting links or unverified candidates
retain uncertainty. Known broker IDs use the direct read path.

Discovery is limited to three history pages and eight distinct order-info reads,
with responses cached per inspection. Continuation after the third page, missing
pagination, broken cursors or exhausted candidate/detail budgets leave the result
Unknown even when an initial match exists. Including server-time reads, one
inspection issues at most 24 GET requests through the adapter and no submission.
Busy account histories may exceed this bounded search; no undocumented time filter
or ordering shortcut is used to claim complete evidence.

The three Portfolio Bitkub HTTP clients cap each buffered response at 32 MiB,
including wallet/history reads, order inspection, server time and submission or
cancellation acknowledgements. This uses the native HTTP buffer limit before
JSON parsing; decoded strings and JSON objects consume additional memory. The
existing timeouts and disabled redirects remain in place. An oversized response
after a durable submission/cancellation claim leaves the outcome uncertain and
never authorizes a retry. The ceiling is configured behavior, not a measured
maximum for the owner's history. Future streaming reads must enforce their own
body limit because this buffer setting does not cover `ResponseHeadersRead`.

Unexpected API failures keep HTTP 503 and use fixed guidance rather than guessing
that SQL failed. GET/HEAD failures ask the user to refresh data; other actions say
the change may have completed and require checking status before another action.
The web proxy and services preserve that uncertainty for lost responses, unreadable
JSON and invalid write-response contracts. Explicit server rejection messages and
caller cancellation remain intact. These messages do not authorize a retry or
change the existing exact-proof recovery for saved accounting confirmations.

The adaptive path scans the versioned `bitkub-thb-spot-v1` THB universe and sizes from
verified account capital, including future deposits. It does not require symbols,
allocated THB or a fixed per-order amount.

| Adaptive default          | Value      |
| ------------------------- | ---------- |
| Profile                   | Aggressive |
| Exposure                  | 100%       |
| Concentration             | 100%       |
| Stop-risk scenario        | 5%         |
| Current-equity daily loss | 10%        |
| Positions                 | Up to 16   |

These are policy limits, not guarantees.

Adaptive **Trading settings** opens with a read-only **System-managed trading plan**
and **Saved risk limits**. The summary explains eligible markets, sizing from verified
capital, strategy-managed stops, targets and trailing, and the saved holding-time cap.
Numeric inputs are available only in the explicit **Advanced overrides** disclosure;
ordinary setup does not require editing them. Saving material overrides disarms live
trading and requires a new owner review. Existing v2 holding-time caps remain saved
constraints; experimental v3 follows its separate saved exit policy and live gate.

Preparation uses the owner-authenticated `POST /api/trading/prepare` with only
`{accountKey,rulesHash}`. The server verifies the exact account and current rules hash
inside the serialized control gate. Initial setup from legacy rules creates v2 using
system-owned defaults. An explicit v1 upgrade changes only the version, preserving
saved technical parameters, risk caps, exclusions and compatibility fields. Existing
v2/v3 rules, hashes and authority remain exact. Preparation never activates trading;
actual rule changes retain the outstanding-order, owned-position and exposure guards
and revoke the old live mandate.

Research and sizing remain separate from activation:

- Team research receives the saved adaptive policy, fresh verified capital,
  symbol/timestamp rejection details and an explicit engine-ledger P&L scope.
- The ManualReview sizing profile is separate; disabling it does not disable adaptive
  sizing. THB 1,000 in a hypothetical study is not a live order cap.
- The engine sizes against cash, exposure, concentration, stop/ATR risk and depth.
  Research names missing signal inputs instead of treating known capital as unknown.
- Native completed 5/15/60-minute candle evidence is available for adaptive research
  and the active v2 policy selects a usable interval.
- The experimental v3 live gate remains closed pending its 14-day/30-pair observation
  and historical paired and portfolio holdout evidence. Stopped remains stopped.
- Updated context applies to new reports; retained historical messages are not rewritten.
- New research jobs are pinned to the verified account before collecting portfolio
  evidence, so collection failures stay visible to that account. An account change
  during collection fails the job before any model call; legacy unbound records are
  not reassigned.

To activate after the deployment and verification have been reviewed:

1. Open the appropriate setup path. Prepare adaptive setup or an explicit v1 upgrade,
   or save any chosen overrides first; material rule changes disarm live trading.
2. Open **Review live mandate**. Verify the exact account, strategy/rules hash,
   universe or symbols and every capital/risk cap.
3. Choose an explicit future expiry within the next 30 days, or choose a 1-, 7- or
   30-day shortcut. These shortcuts only fill the expiry; they never activate trading.
   Fresh markets, verified available balances and server control capability are
   required; a rejected activation stays visible.
4. The owner alone presses **Enable Auto Trading** once. The persisted mandate
   binds this exact configuration and expiry. The deterministic Portfolio
   worker may then enter, track, cancel and exit within that mandate without
   per-order confirmation. Closing the browser does not stop the worker.

| Setup path       | Open and review                                                                                                                                                                                                      |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Legacy rules     | **Crypto → Trading → Trading settings**                                                                                                                                                                              |
| Adaptive trading | **Set up and review autonomous trading** prepares the initial policy and opens review without activating it. Saved v1 uses **Prepare v2 upgrade and review**; existing v2/v3 opens its exact saved rules for review. |

Legacy settings retain these values; strategy and execution values are in
**Advanced settings**:

- Strategy version, allowed THB spot symbols, capital allocation and per-order size.
- Simultaneous positions, daily loss cap, spread/slippage and data age.
- Take profit, stop loss, trailing stop, time exit, cooldown and order lifetime.
- Fee model and entry volume ratio.

- Changing material rules, account or risk caps, mandate expiry, **Disable** and
  **Emergency stop** disarm live authority.
- Re-enabling requires another explicit owner action on the current configuration.
  Expiry never renews authority.
- AI output, schedules, restarts and Telegram mock buttons cannot enable it.

## Risk, recovery and accounting

Trading is THB spot LIMIT only, with no leverage, transfers or withdrawals.
Legacy uses `ema20-50-breakout-v1`; adaptive policies retain their saved version
and rules hash under the [setup and activation rules](#modes-and-owner-activation).
Research alerts are not trading instructions. Rule settings are constraints,
not a proven return or guaranteed exit price.

### Legacy strategy and Paper fills

The legacy strategy enters only when all three conditions hold:

1. The latest completed five-minute candle closes above the previous 20 candles' highs.
2. Seeded EMA20 is above EMA50.
3. Volume is at least the configured multiple of the previous 20-candle median
   (the median must be positive).

It needs at least 51 contiguous completed bars and uses the latest contiguous suffix
of up to 120; older gaps do not invalidate a sufficient recent window.

Owned positions are checked between candle closes:

| Exit condition | Default     |
| -------------- | ----------- |
| Stop loss      | 1%          |
| Take profit    | 2%          |
| Trailing stop  | 1%          |
| Time exit      | 120 minutes |

These conditions trigger LIMIT intents rather than guaranteed fills. Daily loss
includes today's verified realized net plus current unrealized result and blocks new
entries, while valid mandated exits remain eligible.

- In the legacy path, the allocation cap covers open position cost and reserved entry
  cash. Per-order caps also constrain planned sell notional, so a larger exit can
  require more than one candle.
- Splits preserve a market-minimum remainder where possible; venue minimums and
  precision can still leave dust.
- Paper consumes only observed top-of-book liquidity once per fresh snapshot,
  estimates fees and expires unfilled limits. It does not reproduce exchange queue priority.

### Adaptive entries, exits and evidence

| Area                  | Adaptive behavior                                                                                     |
| --------------------- | ----------------------------------------------------------------------------------------------------- |
| Entry patterns        | Versioned trend pullback and range rebound entries, including resting limits below the current ask.   |
| Ranking               | Planned net reward after fees, spread and slippage divided by structural stop distance.               |
| Frozen entry evidence | Structural stops and ATR-derived targets/trailing distances are frozen with the entry.                |
| Exit sizing           | Uses the full owned quantity, exchange precision and minimums, without the legacy entry notional cap. |

- The ranking is a deterministic priority score, not a return forecast. Owned exits
  take priority.
- A frozen buy side or an entry spread limit does not prevent a permitted sell.
- Unfilled entries expire or are cancelled on verified invalidation; cancellation
  uncertainty prevents replacement.

Market-event and scan behavior:

- The engine consumes priority market events. Background research rotates detailed
  candle/depth batches of sixteen across the eligible universe and isolates missing
  market evidence.
- Fresh owned-position quotes do not wait for candle history or the background account scan.
- Broker status checks retain their separate thirty-second minimum interval.
- Coverage and timestamps expose actual progress; the whole universe is not refreshed
  simultaneously.

Execution-history constraints:

- Use the latest contiguous completed five-minute window, with at least 51 verified
  bars and at most 120. An older gap outside that window does not discard a sufficient
  recent suffix.
- An unusable retained snapshot falls back to fresh public history; unresolved gaps
  never produce synthetic candles.
- Adaptive 5/15/60-minute responses enter the cache only after the completed window
  is usable. A late or repaired candle can be fetched again within the same slot.
- Historical responses must align every candle to the requested native timeframe.
  Invalid provider timestamps are unavailable evidence, not valid history.
- Per-market history failures reach the scan status, and later quote/maintenance
  events preserve newer decision reasons instead of restoring old placeholders.

### Account ownership and recovery

Only new fills belonging to this strategy enter its live book. Existing manual
holdings and orders retain their original history and are not silently assigned
an autonomous cost basis or exit authority. Daily reports cover this strategy's
book; whole-account P&L remains Unknown when legacy cost evidence is incomplete.

SQL storage adds account-keyed rule versions, mandates, decisions, order intents,
fills, positions, risk counters, alerts and report delivery alongside the existing
Portfolio ledger. Team and MarketData retain their own databases.

- Execution claims persist before broker requests. Duplicate events, browser retries
  and restarts never authorize another submission of an uncertain intent.
- If final checks fail after a successful claim but before `Send`, that same call
  records an unchanged claim as **cancelled / not submitted**. Changed revisions,
  failed claim acknowledgements and recovered claims retain uncertainty. This does
  not resolve any historical unknown order or authorize a retry.
- Unknown broker outcomes remain blocked until exact read-only reconciliation.
- Cumulative fill reconciliation retains an installed exit plan's effective stop.
  Receiving the same buy fill again cannot replace it with the older entry stop,
  even when a later market read fails. Fill/cash accounting remains idempotent.
- Verified fills must respect the submitted Limit price: buys at or below it,
  sells at or above it. Both ManualReview and AutoLive reject inconsistent rates.
- Manual and autonomous submission claims share the account lock.
- AutoLive refuses activation with uncertain manual submissions or outstanding
  adaptive proposals; manual approval requires AutoLive to be disabled first.
- Notification state writes share the engine gate, while Telegram network calls release it.
- Immutable Telegram acknowledgement receipts are isolated by account, destination,
  alert and exact claim time. Restart recovery repairs recorded outcomes without
  repeating sends; legacy claims without acknowledgement remain unknown. See
  [accounting and delivery](crypto-accounting.md).
- Receipt recovery verifies the stored account, destination and alert with exact
  ordinal identity. SQL text aliases are not acknowledgements; conflicting native
  key matches retain the original row and fail visibly. Claim timestamps keep
  instant equality across offsets and return their stored precision/offset. This
  changes no receipt schema and does not make legacy case-insensitive keys able to
  store two case variants. Unsupported collisions remain uncertain without resending.

Faults and recovery:

- Stale or inconsistent market data, unknown execution state, insufficient available
  funds and risk/identity failures stop new entries and expose a fault.
- Disable and Emergency stop do not prove outstanding orders were cancelled or positions closed.
- Inspect actual broker evidence and reconcile before resuming; never treat an assumed
  stop-loss fill as an executed exit.

### Reporting, notifications and objectives

| Surface                         | Evidence                                                                                                           |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| Crypto trading status           | Mode, current position cost exposure, open orders, cash, equity and last heartbeat.                                |
| Realized gross/net P&L and fees | Actual fills and known cost basis; incomplete values remain **Unknown**.                                           |
| Daily summary                   | Concise Thai summary retained at **23:59 Asia/Bangkok**, with report revisions for late evidence.                  |
| Adaptive Telegram fill notes    | Concise Thai notes with coin, quantity, and actual cash paid/received including fees.                              |
| Critical Telegram alerts        | Emergency stops, risk breaches, uncertain submissions, reconciliation mismatch, stale feeds and prolonged outages. |

- Submission alone never announces a purchase; duplicate broker fills never create
  another note.
- Daily summaries stay in **Crypto → Trading → Daily reports**; select the Bangkok date there.
  Legacy report delivery is retained for compatibility.

| Measure                          | Meaning                                                                                                                                                                     |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Configured business objective    | Account-scoped total wallet equity, including confirmed deposits; the configured amount and Bangkok closing date come from account evidence. Reserved cash is counted once. |
| Account measures                 | Portfolio value, remaining target, confirmed deposits and full-account trading P&L are separate; incomplete valuation or flow coverage remains Unknown.                     |
| Legacy daily-net objective field | Measures verified strategy sell fills against its configured target for historical comparison only.                                                                         |

Neither objective bypasses entry checks.

The optional opportunity review uses `observed-book-v1`:

- At most eight concurrent hypothetical samples, a seven-day model window and a
  reference budget of ten exchange minimum notionals.
- Uses subsequent observed quotes, visible liquidity and fees. Candle touches alone
  never fill a sample; missing exits remain Unknown.
- These samples and their mean net returns are not actual fills or missed account profits.
- Team receives only bounded, account-isolated performance evidence for its daily
  lessons; it cannot change a live policy or promote a strategy itself.

## Compatibility and rollback

- Legacy rules retain their exact hash and omit inactive additive JSON properties.
  Deploying this build never converts an old mandate to all-market authority.
- New state still starts in Paper. Existing owned fills, account keys and SQL records
  are retained; only explicit owner setup saves the new policy and revokes old authority.
- Verified cash flows affect funding once, not trading profit.
- An unexplained strategy-cash shortfall blocks submission until funding evidence is
  reconciled; a higher account balance alone never invents a cash flow or profit.

| Saved state                           | Compatibility                                                                    |
| ------------------------------------- | -------------------------------------------------------------------------------- |
| Before owner setup                    | The legacy payload remains readable by the previous build.                       |
| After `adaptive-intraday-v1` is saved | Rollback requires a build that understands that policy and its additive records. |

- Do not replace the saved adaptive policy with a legacy-only binary, rewrite history
  or revive a revoked/expired mandate.
- Disable live trading before maintenance, retain canonical backups, and leave
  reactivation to the owner.

## Private server configuration

The canonical [Docker preparation](docker-runtime.md) generates a 32-byte random
owner-control token in `.runtime/docker/runtime.env`, restricts that file to its
owner and preserves existing SQL credentials and the token on repeated runs.

| Recipient                            | Binding and behavior                                                              |
| ------------------------------------ | --------------------------------------------------------------------------------- |
| Portfolio                            | `Trading__OwnerToken`                                                             |
| Next.js server only                  | `QUANTARA_TRADING_CONTROL_TOKEN`; the guarded server proxy adds the owner header. |
| Team, model workers and browser code | Never receive the token.                                                          |

- Do not edit the repository `.env` to add the control token, print private environment
  files or expose the application beyond its loopback deployment.
- Missing server configuration fails closed; configuration alone grants no mandate.
- Preparation and normal startup preserve existing containers; apply updates only
  through reviewed maintenance with current canonical-data backups.

Web JSON controls and dashboard statistics forms accept at most 8,192 bytes. The
request deadline covers reading that body as well as the upstream request:

| Web request                  | Deadline                                                       |
| ---------------------------- | -------------------------------------------------------------- |
| Guarded Portfolio requests   | 30 seconds by default                                          |
| Risk refresh                 | 90 seconds                                                     |
| Job dashboard and statistics | 15 seconds; dashboard responses are limited to 2,000,000 bytes |

Cancellation or timeout while reading an incoming body returns 408 without
forwarding it. Failure after forwarding can still have an unknown outcome; use
the existing confirmation/order recovery flow rather than automatically retrying.

A powered-off or sleeping PC, lost network, stopped Docker or stopped/unhealthy
service cannot trade. Compose restart policies and health checks do not provide
uninterrupted availability or guarantee execution during an outage.

## Real-time market decisions

MarketData reuses its public Bitkub WebSockets and SQL-owned completed five-minute
candles. Portfolio subscribes to a private authenticated SSE stream of typed
`QuoteChanged`, `BookChanged`, `CandleClosed`, `FeedUnavailable` and heartbeat events.

- No broker keys enter MarketData.
- Public order books retain actual bid/ask quantities and the older side's timestamp.
  Incomplete, crossed or disconnected books never become executable prices.
- Candle history is not synthesized.
- Portfolio's HTTP reads bound the universe to 1,048,576 raw bytes and market
  evidence to 4,000,000 raw bytes plus its existing 1,000,000-character limit.
  The configured request timeout covers headers and body. Caller cancellation
  also stops SSE frames that were already buffered.

### Watch coverage and account evidence

| Watch group         | Coverage                                                                                                                                    |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Required watches    | Holdings and active orders in both retained books, including buy-frozen markets that still permit selling.                                  |
| Exit research       | Up to sixteen distinct active or triggered study symbols after required holdings and orders.                                                |
| Adaptive candidates | Up to eight additional positive-scored candidates within the saved signal-age limit.                                                        |
| Discovery           | Keeps eight liquid markets and rotates up to eight others every five minutes, giving the one-minute candle reader time to collect evidence. |
| Total               | Capped at 64 watches, with owned symbols taking priority.                                                                                   |

- Stale and future observations are excluded from the displayed adaptive shortlist.
- Candidate slots exclude symbols already pinned by holdings, orders or exit research
  before applying the eight-symbol quota. The ranked shortlist still stops at sixteen.
- Zero-volume markets do not consume discovery slots.
- Required watches refresh every two seconds and retain their socket sessions across rotation.
- This is bounded real-time coverage, not simultaneous coverage of every listing.
  The separate fair universe scan still checks other eligible markets.

Account and position evidence:

- Account capital refreshes independently of native-history scans with one request
  in flight. Its original observation time and policy freshness limit remain visible;
  failed or pending refreshes never make old capital fresh or stall the event path.
- Team receives read-only position-linked exit levels, time exits and active-order
  expiries from the selected engine book, separately from imported wallet holdings.
  These levels are engine conditions, not exchange-native stop orders or guaranteed executions.

Research context:

- Completed team assessments can advance the next research round to the earliest valid
  requested review, subject to the existing fifteen-minute cooldown, budget, Stop and
  no-overlap rules. Research timing grants no trading authority.
- Opportunity summaries expose expired and unknown-net closed samples; waiting
  includes open, and the reported mean uses only known-net closed samples.
- Prior reports retain the task history; new chat messages carry at most two current tasks.

### Queueing and decision order

| Queue limit                             | Bound      |
| --------------------------------------- | ---------- |
| Each SSE subscriber's coalescing buffer | 256 keys   |
| Portfolio priority queue                | 128 events |
| Dirty symbols                           | At most 64 |
| Reserved timer lanes                    | Three      |

- Owned exits run before reconciliation, completed-candle entries and research.
- Due reconciliation gets a turn after at most 16 consecutive owned events so
  continuous quote traffic cannot starve fill accounting.
- Received owned quotes retain their decision context within the bounded queue;
  preflight uses the latest quote.
- A delayed fallback snapshot cannot overwrite a stream update received after that
  snapshot request began.
- Overflow retains owned symbols as dirty and immediately schedules a fresh quote read.
- Candidate updates refresh cached prices without causing intrabar buys.
- Per-symbol/kind sequences reject duplicate or older frames; reconnect rebuilds the
  watch from SQL, and durable rule/symbol/candle intent keys remain authoritative.

Decision guards:

- Fresh owned bid/ask events evaluate stop loss, take profit, trailing and time exits
  without a ten-second poll or a new candle.
- `adaptive-intraday-v1` entries still require completed-candle evidence and existing risk sizing.
- Slow universe/capital scans and broker status reads run outside the serial account
  decision gate; broker submission itself remains serialized and can wait for the exchange.
- Final market freshness, mandate, account, balance and risk guards, durable claims,
  partial-fill accounting and unknown-outcome no-retry rules remain in force.

### Timers, diagnostics and configuration

| Stream or job                       | Timing                                       |
| ----------------------------------- | -------------------------------------------- |
| Private-stream heartbeat            | Every 15 seconds                             |
| Private-stream timeout              | After 45 seconds without a valid frame       |
| Reconnect backoff                   | One-to-30 seconds                            |
| Handshake                           | Bounded at 20 seconds                        |
| Maintenance/expiry                  | Independent timer every five seconds         |
| Research                            | Independent timer every minute               |
| Read-only broker inspection         | Independent timer, at least 30 seconds apart |
| Owned-quote fallback check          | Independent 30-second timer                  |
| Reports and confirmed-fill delivery | Run independently every 30 seconds           |

- Disconnect invalidates cached prices.
- Thai confirmed-fill notes and critical alerts retain their existing delivery claims.
- The 23:59 Bangkok daily report stays in the app for adaptive mode.

**Crypto → Trading → System details** shows private-stream connectivity and watched count. Collapsed diagnostics expose:

- Watched/dirty symbols and coalesced/dropped triggers.
- Rolling received-to-local-decision p50/p95 (512 samples).
- Latest decision-to-submit latency and exit/reconciliation timestamps.

Stream connectivity alone does not prove every public market is healthy or an order
filled. Latency excludes exchange/API round trips and is not a fill guarantee;
fake-load measurements are reported separately from live execution.

- Docker preparation also generates and preserves an independent random
  `QUANTARA_MARKET_EVENTS_TOKEN` in private `runtime.env`. Only MarketData and
  Portfolio receive `MarketEvents__Token`.
- Native development must privately supply the same setting to those two processes;
  missing configuration fails closed.
- No schema, persisted rule hash or owner mandate is changed by this transport.
- Roll back only to an adaptive-policy-compatible build, keep all canonical data and
  private tokens, and disable live trading before replacing running containers.

## Verification

| Check                                     | Scope                                                                                                                                                                           |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Deterministic Portfolio/MarketData checks | Run alongside the trading UI verifier.                                                                                                                                          |
| `node tools/verify-trading-ui.cjs`        | Add `--browser` with the frontend running for isolated editable-rule, mandate, error-recovery and responsive fixtures. All fixture API calls are intercepted before navigation. |
| `tools/verify-docker.ps1`                 | Token generation/preservation, owner-only permissions, service isolation and retained runtime guards without starting Docker or reading account secrets.                        |

- Real SQL or account integration remains explicitly opt-in.
- Never use the live Enable button or a real broker POST to verify an implementation.

Fixture measurements on 2026-10-17 used Windows and fake storage/quotes/broker:

| Fixture                                           | Result                                                                          |
| ------------------------------------------------- | ------------------------------------------------------------------------------- |
| 100 fresh owned events while research was stalled | Local-cycle p50 **0.017 ms** and p95 **0.054 ms**, below the two-second target. |
| Running Paper worker                              | Recovered its required watch and filled its first streamed exit in **25.0 ms**. |

The suite also covers stop/target/trailing/time exits, queue overflow, delayed snapshot
races, reconciliation fairness, SSE timeout/reconnect, partial fills, restart and
unknown submission no-retry. These measurements exclude real SQL, exchange/API and
network latency; they do not establish production execution or fill latency.

## Native timeframes and daily evidence (v2)

`adaptive-intraday-v2` selects the shortest usable native interval in order 5, 15,
then 60 minutes:

- A usable window has at least 51 consecutive completed bars ending at the most recent
  native close.
- Existing spread, visible liquidity, after-cost opportunity ranking and risk checks
  still apply.
- Sparse/missing bars are never synthesized. A provider-reported zero-volume bar is
  retained as evidence of zero reported volume; an absent bar has an Unknown cause.
- Transport failures are labelled source-unavailable.
- Each attempt retains source, interval, counts, reason and check time in the account's
  scan state.
- Native bars use a bounded cache; the existing SQL five-minute series remains unchanged.

Timeframe identity and upgrades:

- Signals, risk stops and durable deduplication keys retain the selected timeframe.
- Quote-driven exits and reconciliation continue independently of candle research.
- v1 behavior and hashes remain unchanged. Upgrade stages v2 in settings; saving
  disarms live authority. Only the owner can review and enable the new mandate.

### Daily historical worker

| Study property | Bound or behavior                                                                                                      |
| -------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Candidates     | At most four observed candidates under v2 rules.                                                                       |
| Cadence        | Once per Bangkok day/rules hash/replay protocol, with hourly availability checks.                                      |
| History        | Seven days of native bars through MarketData, in chunks of at most 999 intervals.                                      |
| Persistence    | Account-owned trading state; no database schema is replaced.                                                           |
| Results        | Source-window warnings and frozen rules hash, visible in Daily Brief and supplied to the AI team's read-only snapshot. |

The independent worker never acquires broker credentials or calls the exchange.

### Historical scenario assumptions

| Scenario assumption | Value or treatment                                                                                                |
| ------------------- | ----------------------------------------------------------------------------------------------------------------- |
| Split               | Bars split chronologically 70/30 into training/validation; positions reset at the boundary.                       |
| Evaluation          | Breakout/pullback/range separately on 5/15/60 minutes. Rows are independent scenarios, not an additive portfolio. |
| Reference budget    | THB 1,000.                                                                                                        |
| Costs               | Both-side fees, saved maximum spread and exit slippage; historical quote/queue evidence is unavailable.           |
| Fill model          | Only a later bar that strictly trades through a limit can model a fill; a touch does not.                         |
| Drawdown            | Close-marked equity after estimated exit fees.                                                                    |
| Minimum evidence    | Fewer than 30 evaluated bars or 20 closed trades is insufficient.                                                 |
| Cash return         | Zero in this scenario.                                                                                            |

- Gaps, partial-window expiries, ambiguous same-bar exits and unfinished positions yield
  Unknown net/drawdown.
- These assumptions cannot verify actual fills, prove profitability or promote rules
  automatically.

| Replay protocol    | Precision behavior                                                                                                                                             |
| ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ohlc-scenario-v1` | Retained legacy results remain readable.                                                                                                                       |
| `ohlc-scenario-v2` | Uses current exchange tick and base/quote scales as historical assumptions; retained results remain readable.                                                  |
| `ohlc-scenario-v3` | Keeps v2 assumptions and rounds sell limits up to the next tick, preserving the minimum price allowed by slippage. Buy limits and quantities still round down. |

The replay protocol identifies a hypothetical calculation, independent of the strategy
version or mandate. A changed protocol refreshes the daily study once; it does not
change trading authority. The engine uses the same directional limit rounding: a
sell quote at one tick remains positive, and rounding cannot lower the permitted sell
price. A tighter sell limit may remain unfilled.

### Daily funnel

The compact daily funnel separates scan readiness, pre-cap signals, confirmed broker
order IDs, filled order IDs, completed sell orders and realized net THB:

- Counts today's Bangkok records in the selected paper/live book.
- Submitted orders are the cohort created today with a confirmed broker ID; unknown
  submissions are excluded.
- Legacy signal history is labelled retained because the raw signal was not stored
  before execution guards.
- Unknown cost/profit remains Unknown.
- Telegram continues to notify confirmed fills only.
- The legacy daily-net field does not replace the configured wallet-equity objective.
