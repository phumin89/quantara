# Live owner onboarding review — 2026-10-06

Status: implementation checkpoint; real-money execution remains disabled. No real order, cancellation or activation was performed.

Owner direction: permit 100% of verified Spot capital and delegate asset/size decisions to CEO. The owner setup derives positive money ceilings from the displayed actual THB balance; maximum position exposure/drawdown are 1. Daily turnover is one capital round, slippage retains the system ceiling, fee reserve comes from the verified broker profile. Saving limits and initializing balances do not arm. Funds, account binding, mandate, fees, symbols or Stop changing during confirmation invalidate consent. Fresh snapshot polling alone does not.

Lead review corrections:

- Replaced the worker's manual multi-field form with a full-capital consent and separate activation step. Fixed object-shaped account IDs, Windows import casing, stale consent, control-expiry handling and Stop-clear consent. Mutations have fresh CSRF, synchronous duplicate guards, no automatic retries and a post-action state read.
- Added opt-in Bangkok-day rollover. Two fresh exact money observations, settled orders, unchanged fills and a recent verified prior-day anchor are required for continuous automatic rollover. Stable observations may straddle midnight. Interrupted days require disarmed owner action. High-water equity/drawdown, Stop, fills and history are preserved. An expired or changed mandate cannot regain authority. Reserve/prepare/confirm reject an old or missing daily baseline.
- Advice tolerates reconciliation version/quote refresh only when owned cash/quantities/reserves, turnover and mandate limits are unchanged and price drift stays bounded. A meeting crossing the Bangkok day cannot dispatch. Broker steps/minimums round quantities and quote spending down. Shared AI schema permits weight up to 1, while input-specific validation retains smaller Sandbox limits.
- Added an isolated Live host entry point and 30-second worker using the existing eight-role Codex meeting. It registers no paper scheduler, broker or ledger capability. Before 09:00, blocked/disarmed/unreconciled accounts or unavailable native evidence do not spend model quota. The persistent daily claim prevents duplicate financial submission after restart.
- Completed summaries persist first locally, then idempotently into the private PostgreSQL account audit. Replay only saves reports, never resubmits an order. Scoped token authorization binds the account and cannot use owner controls. Saved reports never mutate money/gates. The chat validates the entire response, shows final conclusion first, lets the owner select dates, and keeps earlier role reasoning optional. Malformed/unavailable data is distinct from an authentic empty history.

Validation:

- Headless Domain/Live/Bitkub/owner/shared-AI checks: **495 passed, 0 failed, 0 skipped**.
- Actual PostgreSQL integration: **61 passed, 0 failed, 0 skipped**, including report restart/replay and daily rollover persistence.
- Focused frontend checks: **27 passed**; TypeScript passed; production frontend/owner/company images built.
- Bounded desktop/tablet/phone visual review used explicitly synthetic account/report data and existing CSS/components. No horizontal overflow at 1440, 820 or 390 pixels. Fixed large full-width action buttons and excess paragraph spacing; confirmed desktop and phone after the batch. This does not prove activated production UI states.
- Isolated container TLS probe received 401 from the actual executor without authority. A different trust certificate was rejected. Only a public localhost certificate was mounted; no TLS private key, broker credentials, ledger password or owner session was exposed to the company process.
- Deployed `/`, `/account`, `/trading` return 200 with normal TLS validation; anonymous `/live/setup` and `/live/meetings` return 401. Persisted owner viewing state was retained, without obtaining the user's cookie or pairing code.
- Production private ledger is healthy with the existing append-only schema and **zero accounts**. Private configuration templates were prepared idempotently. Broker verification flags, execution and AI remain false. No synthetic account was installed in production.
- Fresh signed Bitkub wallet read this turn succeeded: **THB available/reserved/total 0, no nonzero coin balances**. This proves read access only.

Runtime repair: Docker Desktop failed on stale AF_UNIX endpoints in `Local/Docker/run` and `Local/docker-secrets-engine`. Preserved the two verified temporary directories under sibling backup names and recreated runtime directories; stopped only verified Docker processes and terminated only `docker-desktop`. The engine recovered. No factory reset, VM/volume deletion, global WSL shutdown or Windows security changes. Native .NET tests were blocked by Application Control; SDK containers executed the tests with isolated test databases/networks instead. Logs are in ignored `work/live-account-connect/`.

Remaining before real trading:

1. Actual THB funding and confirmation that the owner's key has Trade permission. Do not send credentials in chat; Deposit/Withdraw are unnecessary.
2. Verified broker/account profile. Published [REST history fields](https://api.bitkub.com/docs/endpoint/api/v3/market/my-order-history?method=GET) and [private order updates](https://api.bitkub.com/docs/websocket/private/order_update) describe amounts/fees but do not provide sufficient proof here for quantity-step units, base rounding, REST fee-credit conversion and complete lineage. Exact reconciliation still rejects unsupported rounding; do not silently guess or install true verification flags.
3. Enable the independently verified executor/profile and isolated company configurations, then use the owner controls to provision, reconcile and explicitly arm. Preparation/startup does not activate.
4. Separately authorized minimum-sized buy/sell, partial-fill/cancellation and recovery proof. Synthetic tests and healthy deployment do not establish Bitkub execution compatibility or profitable operation.
