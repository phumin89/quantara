# Auto trade path review — 2026-10-06

The owner asked to continue until real Auto trading works. This round targets execution and recovery only. No UI changes, simulated trading, real placements, cancellations or owner activation were performed.

## Verified defects and fixes

- `BitkubActivationSource` treated local risk reserves as exact broker reserves. Local budget-buy reservations intentionally retain fee headroom, so an owned open/partial buy could reconcile successfully but prevent owner reactivation. Preflight now requires exact broker wallet/open-buy reservation agreement within local per-order and aggregate bounds. Coin totals, sell quantities, account binding and prices remain exact; duplicate, foreign, terminal-state or non-limit open orders are rejected. Acknowledged owned orders must also have matching open evidence.
- Owner API preflight/arm trusted the saved profile's Trade permission. The shared source now probes current permission every time, so a revoked grant is not reused. The profile still binds the account; it does not grant current authority.
- Graceful meeting cancellation left its persisted claim STARTED, blocking every later day. Cancellation now records INTERRUPTED and rethrows. Restart can publish that report but cannot replay the financial request or repeat the day's meeting. Unresolved submitted orders remain owned by the executor and retain their reservations. Abrupt process death or an unsuccessful journal write still leaves STARTED blocked; this change does not silently clear unknown financial outcomes.
- Historical canary captures expired after 24 hours, while the verifier also required the initial capture to contain no trades. Completing the round trip later could therefore strand legitimate first-use evidence. Captures now stay usable strictly within the broker's 90-day REST archive boundary; relative metadata freshness, ordering and wallet/fee equations remain required. The existing command still corroborates all trades, current key/permission, empty current inventory, cash and market rules before writing any profile. Old balances never become current capital or activation authority.

## Independent verification

- Lead inspected the actual code and synthetic fixtures independently of supporting agent reports. No new dependency, permission, risk bypass, verified fee mode or auto-arming behavior was introduced.
- Final headless trading/risk/Live/Bitkub/owner checks: **646 passed, 0 failed, 0 skipped**. Includes current-permission revocation, open/partial budget buys with fee headroom, exact broker sums and per-order limits, foreign/duplicate/state mismatches, exact sell/coin checks, cancellation before submission and accepted submission without reply. Interrupted reports replay without resending; the next day requires fresh, armed, reconciled account evidence. Historical proof accepts a 30-day baseline and rejects future captures and the 90-day archive boundary.
- PostgreSQL integration checks: **68 passed, 0 failed, 0 skipped**, using an isolated temporary database/container/network. Covers existing provisioning, owner activation, risk submission, settlement, recovery and persistence. No real account connection or broker credential was mounted into the tests; the test database and network were removed.
- Production owner and isolated company images built successfully. Updated the existing owner service while preserving its state and ledger. The company service remains off and execution configuration remains disabled. The company image is prepared, not running.
- A fresh read-only broker capture confirms the existing small buy remains held with no open supported orders. The complete after-buy capture pointer is preserved. Financial balances and identifiers remain in ignored local captures and are not included in this report.
- A separate bounded read-only probe authenticated the documented private WebSocket endpoint and subscribed successfully to both order_update and match_update. It then disconnected. No order/match payload was captured and no trading command was sent. This verifies transport access, not accounting or Auto trading.

## Remaining real-account work

The after-sell observation is still absent, and the fully credited buy does not distinguish REST buy amount/cash-fee conventions. Do not select a convention from wallet equality alone, exhaust fee credits by repeated trades, fabricate a profile or label successful code tests as a completed real trade.

The owner completes the agreed small round trip on Bitkub before the existing THB-only first activation can proceed. Paid-fee evidence or authoritative fee confirmation is still needed for the current REST normalization path. The official private stream provides explicit received amounts and wallet fees, but merely authenticating it does not integrate a durable, corroborated fill source; that alternative remains a separate implementation and verification step.

After a genuine verified profile exists, the prepared owner command provisions positive limits from actual THB, reconciles the ledger and explicitly arms before starting the isolated CEO. Acceptance still requires an app-originated real order, matching broker settlement and restart recovery without duplicate submission.

Source contracts reviewed: [REST V3](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md), [private order and match streams](https://api.bitkub.com/docs/websocket/private/order_update), and [private WebSocket specification](https://github.com/bitkub/bitkub-official-api-docs/blob/master/websocket-private.md).
