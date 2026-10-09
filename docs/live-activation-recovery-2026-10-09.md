# Live activation recovery — 2026-10-09

Owner activation reported `UNRESOLVED_ORDER`. Read-only authenticated order info,
history, open orders and the persisted private order receipt confirmed the earlier
BTC test sell was filled, with no matching open order. No new order or cancellation
was sent during diagnosis.

## Findings and fixes

1. Quantity `0.00001040 BTC` × fill price `2,800,287.93 THB/BTC` gives
   `29.122994472 THB`; the authenticated receipt credited exactly `29.12 THB`.
   Reported fee and credit were both `0.08 THB`, so cash fee was zero.
   Exact unrounded multiplication rejected legitimate cash settlement.
2. Preserve original fill quantity, price, transaction identity and fee evidence.
   Carry authenticated cumulative gross proceeds on the broker order; persist its
   cash adjustment on the owned order. Applying only the change in that adjustment
   handles partial fills and restarts without assigning invented cash to individual
   fills. Receipts require exact identity, units, fee/credit totals and timestamps;
   repeated or foreign fills are rejected. Net THB cash must have cent precision,
   with discrepancy strictly below one cent per corroborated fill. This bound is
   not a wallet tolerance: both full observations and the ledger must still equal
   the actual wallet exactly.
3. Two scans of 247 verified markets took about 33 seconds. Stamping each scan at
   its first market request made the first observation stale at reconciliation.
   Stamp `ObservedAt` at the actual final wallet request, retain
   `CollectionStartedAt`, keep individual quote timestamps and the 25-second scan
   limit. The existing 30-second wallet/quote freshness checks remain enforced.
4. The background worker's outer 25-second timeout could not accommodate two
   independently bounded reads. Its cycle now allows 60 seconds, without parallel
   cycles, placement, cancellation, automatic arming or relaxed broker read limits.
5. Explicit owner activation recovers confirmed results before rejecting a stale
   unresolved order. After verified settlement, the same explicit command may
   establish today's daily risk frame following downtime. It preserves high-water
   equity, capital, binding and owner Stop; ordinary startup never rolls this frame
   or increases authority. Funding changes remain confined to the existing
   explicit owner activation path. Command timeout allows the necessary bounded
   recovery, mandate refresh and new-day reads.
6. The .NET runtime image lacked `libgssapi_krb5.so.2`. Install
   `libgssapi-krb5-2` rather than changing PostgreSQL authentication settings.
   Npgsql documents the missing library as a GSSAPI fallback warning; it did not
   cause the receipt accounting failure.

## Verification

- Independently reviewed the actual production and test diff; `git diff --check` passed.
- Headless strategy/risk/accounting suite: **1,047 passed**, zero failures/skips.
- Isolated PostgreSQL integration suite: **152 passed**, zero failures/skips.
- Regression coverage: credited/uncredited sell cash, upward/downward cent
  settlement, cumulative partial fills with different prices, sub-cent matches,
  reload/idempotence, exact wallet rollback, wrong identity/precision and duplicate
  fills, unchanged owner Stop, missing receipts, preserved high-water equity and
  no placement retry during explicit recovery.
- A two-read worker regression takes 26 seconds and completes; the previous
  one-read timeout would cancel that reconciliation.
- Read-only current-account replay with the patched source recovered the filled
  sell and exact wallet, with adjustment `-0.002994472 THB`, no outstanding order,
  and execution still disarmed. It correctly reported the old daily frame as
  `DAILY_BASELINE_REQUIRED` rather than resetting it in the diagnostic process.
- Built owner/company runtime images and deployed only the owner repair after
  confirming execution disarmed and the company stopped. The running background
  reconciler recovered the actual earlier sell: zero outstanding orders, one
  filled order, exact wallet `4,999.12 THB`, execution still disarmed, Stop unchanged,
  and the old daily frame retained for the owner's explicit activation command.
  Read-only comparison against the last acknowledged-order history confirmed
  unchanged mandate, account binding, owner Stop and daily performance baseline.
  Running owner image matches the rebuilt image; app entry returns HTTP 200.
- Native runtime dependency resolved in the built image. Private probe logs stay
  in ignored `work/`; no account, client, transaction or credential identifiers
  are included in this report.

The initial test purchase/sell is connectivity evidence, not a performance result.
These fixes do not establish profitability or authorize an automatic real-money
activation by the coding agent. The owner retains the existing activation command.

## Primary references

- [Bitkub private WebSocket fields](https://github.com/bitkub/bitkub-official-api-docs/blob/master/websocket-private.md):
  received amount is after fees; net fee is wallet-paid fee; order amounts are cumulative.
- [Bitkub native market metadata](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md#get-apiv3marketsymbols):
  THB quote asset scale is two. No rounding direction is asserted by this patch.
- [Npgsql GSSAPI documentation](https://www.npgsql.org/doc/security): missing
  Kerberos library can fall back to normal authentication.
