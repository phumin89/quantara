# Bitkub settlement round review — 2026-10-06

Status: implementation and actual non-financial preflight completed; real-money execution remains disabled. No real order, cancellation, activation or verified-profile flag was changed.

Findings and fixes:

- Reconciliation compared REST millisecond history timestamps against an epoch in seconds. It could include unrelated pre-epoch history. The filter now uses milliseconds and rejects future history. A database flow includes old foreign history and confirms only the owned current fill is applied.
- Accounting reconstructed quote spending solely from received base quantity times fill price. Ordinary non-divisible buys were rejected. `BrokerFill.QuoteAmount` now preserves independently normalized quote spending for budget buys; cash, reservations, budget and turnover use it. Coin quantity and fee stay separate. Positive quote spending must not be below quantity times price. Sell/non-budget explicit amounts remain unsupported. Revision checks still allow fee changes only; quote/coin/price changes are rejected.
- Pair rules add opt-in `FloorToBaseStep` buy rounding with an exact remainder check. The default remains `Exact`, and invalid enum values fail closed. No real rounding convention was inferred from a public precision field or selected in production. Only independently confirmed per-fill rounding may enable this branch; unsupported sell cash rounding/lineage remains blocked.
- Existing JSON fills without `QuoteAmount` retain exact-price accounting. The computed fallback is excluded from JSON; rounded spending survives a fresh PostgreSQL store. Rollout is additive with no schema/data rewrite. An older executor rejects the new explicitly selected pair-rule field under its strict profile parser. Do not downgrade an account with rounded fills to an older executor.
- Added signed Trade-permission probe through `/api/v3/market/wstoken`, required to have Trade in the official REST permission table. A successful token is validated and discarded, never returned, logged or saved. Only an unambiguous error 52 is a denial; unavailable/malformed responses remain unknown. This grants no order authority.
- Expanded the isolated account checker with `--preflight-env` and bounded private `--capture-evidence-env`. It reads two wallet observations, open orders, complete paginated history and public metadata. Capture includes typed history/order details and the local credential fingerprint, with no key/secret/signature/token. Captures have unique names in ignored runtime storage and never authorize execution.

Lead review corrected worker test fixtures that used a wrong fill price, invalid budget, string-shaped account ID, double fee subtraction and a rounded wallet in all legacy cases. Replaced the incomplete worker diagnostic patch; distinguished unknown permission from denial, added stable wallet/history reads and sanitized stage errors. Inspected the actual final changed files directly.

Validation:

- Headless Domain/Live/Bitkub/owner/shared-AI suite: **518 passed, 0 failed, 0 skipped**.
- Actual PostgreSQL integration: **63 passed, 0 failed, 0 skipped**. New flows preserve gross quote spending through restart, reconcile idempotently and do not resend/rearm; historical timestamp regression is covered.
- Focused final regression checks: **23 passed, 0 failed, 0 skipped** after improving the owned sell/non-budget fixture. Owner/company images and the isolated checker published successfully. Logs: `work/live-account-connect/live-rounded-focused.log`, `live-rounded-build.log` and `live-capture-build.log`.
- No frontend layout changed; desktop/tablet/phone evidence from the preceding owner UI round remains the relevant visual review.
- Actual authenticated preflight at **2026-10-06 17:04 Bangkok**: THB available/reserved/total **0**, no nonzero coins, **Trade probe succeeded**, no open orders/history in BTC/ETH/SOL pairs, two wallet observations identical. The token was discarded. This proves connectivity/permission observation; it does not prove real fill normalization or actual order execution.
- Actual capture mode also completed with two stable wallet observations, typed wallet entries and the bound credential fingerprint; zero history records. The private artifact stayed in ignored `.runtime`, with no credential/token emitted. Updated owner image deployed; `/`, `/account`, `/trading` return 200 using normal TLS. Ledger/owner/web run; company does not. Execution and company remain explicitly disabled.

Remaining: independently verified broker amount/fee/rounding/lineage evidence; positive funding; owner-bound private execution profile; owner provisioning/baseline/activation; separately authorized actual app canary. The [small-capital proof plan](bitkub-small-capital-proof.md) makes the next broker evidence step concrete. Do not describe this checkpoint as working autonomous Live trading.
