# Hangfire and adaptive exit — implementation review

This review records the Hangfire/exit implementation, verification coverage and open release gates. Deployment evidence and current runtime state belong in private operational records. Maintenance requires AutoLive disabled.

## Results and verification

### Delivery and queue recovery

- Account/destination/alert-bound SQL dispatch leases; existing outbox claims remain canonical.
- Telegram 429 has bounded explicit retries; permanent rejection and ambiguous sends are retained separately.
- Successful receipts retain message ID/time. Lost final persistence remains a failed job with an unreplayed sending claim.
- Expired enqueue leases are recoverable; stale lease cannot acknowledge a newer dispatch; attempt cap is eight.
- Pending scanning rotates so blocked old messages cannot hide new ones. Queue errors back off to 60 seconds.

### Report work and security

- Minute-level preparation in a separate maintenance queue; fill delivery in its own single-worker queue.
- Streaming prices, order submission, reconciliation and human callbacks remain outside Hangfire.
- Read-only local Dashboard through server proxy and private control token.
- GET pages plus an exact `/stats` POST accepting only bounded metric names; all job commands remain denied.
- Origin/path/redirect/body budgets tested; existing antiforgery handling remains enabled.
- No control token in browser payloads/job arguments.

### Exit behavior and compatibility

- Frozen versioned entry plans, first-fill anchor, monotonic stop, cost-aware trailing, bounded reviews, confirmed regime changes and hard deadline.
- Valid quote can protect without candle history. Missing evidence cannot reset a deadline.
- Sell intents retain the plan revision.
- Frozen v1/v2 hash serialization; null/empty additive fields omitted. No automatic policy upgrade.
- Existing positions keep existing exit behavior. Configure cannot orphan an adaptive owned position.

### Prospective study and release gate

- New v2 entries can retain a read-only v3 seed. Separate paper/live, rules-hash and account partitions.
- Missing evidence/active entry/invalid seeds are explicit.
- Hypothetical net and actual baseline net stay separate; completed hypothetical outcomes are immutable.
- v3 live activation remains unavailable.
- Historical paired replay, portfolio capital replay and holdout acceptance are not complete; prospective observation cannot be manufactured during implementation.
- `readyForReview` is only a sample-count milestone.

## Validation

Checks completed with fake brokers/Telegram:

- Full solution build (zero warnings/errors).
- Portfolio/Team/MarketData deterministic checks.
- API security and architecture checks.
- npm check/build and format verification.
- Dashboard proxy checks.
- Desktop/tablet/phone browser fixtures.

Verification details:

- Browser fixtures intercept every API request, including activation. Screenshots include the new per-position exit plan.
- Format verification reports Roslyn workspace-loading notices, exits successfully and reports no formatting changes.

Opt-in integration:

- Used an isolated `QuantaraFeatureChecksHangfire...` database: concurrent lease claims, stale acknowledgement, destination/account isolation, 200-character keys, lease recovery and attempt cap.
- A real Hangfire SQL server resolved the actual notification job through DI, wrote a fake Telegram receipt and safely replayed the same job without a second send.
- Canonical application databases and real Telegram were not used by that test.

Review fixes beyond the first draft:

- Preserve the last review reason and require two distinct regime reviews.
- Retain the first-fill deadline across partial fills/restart and preserve stop/plan metadata during accounting.
- Prevent research errors from changing execution authority and keep pinned study history separate from execution books.
- Preserve idempotent Telegram edits and surface unpersisted receipts as failed jobs.
- Reject empty-body proxy edge cases and avoid head-of-line starvation.

## Rollout and recovery

### Maintenance requirements and regression coverage

- Verify AutoLive is disabled before maintenance. Development, testing, deployment and restart never activate a mandate or submit real broker orders.
- Preserve the exact v2 rules hash, revoked mandate, immutable retained fills and owned position quantity/cost/first-entry time. Back up all three canonical databases and private configuration; verify checksums and isolated restore/physical checks before replacement. Keep AutoLive disabled afterwards.
- Retain mixed SQL/BIN2 diagnostic-join regression and narrow form-route security tests for the Dashboard's read-only statistics POST. After updating, verify services, statistics responses, queue heartbeats, report jobs and scheduler logs; retain actual observations privately.
- Verify pending counts, delayed messages and unacknowledged sending claims remain visible. Legacy sending/unknown claims require review and must not be replayed. Live trade receipts require confirmed fills under a valid owner-enabled mandate; maintenance never creates an order to test delivery.

### Deployment procedure

1. Deploy H independently: owner disables AutoLive.
2. Verify exact canonical project/account/hash/mandate and existing owned/open/unknown state.
3. Take and verify backups of all three canonical databases plus private runtime configuration. Verify restore in an isolated database before replacement.
4. Use prepared images from a verified private deployment manifest. Replace Portfolio and Web only.
5. Preserve the exact existing v2 rules/hash and revoked mandate; do not enable live trading afterwards.

### Compatible recovery and open gates

- Once non-null receipt/study fields are written, the old strict JSON binary is not a safe rollback.
- To resume the legacy scheduler, stop new producer/consumers first, then use this compatible Portfolio binary with `Trading__UseHangfire=false`.
- Preserve outbox, sending/unknown claims, receipts, study data and Hangfire history. Never reset/retry all jobs.
- H3 remains open until 72 hours of healthy queue observation.
- E3 remains open until the frozen comparison/holdout criteria pass. Failed/insufficient evidence does not promote v3.

Protocol: [frozen parameters and assumptions](adaptive-exit-protocol.md). Full acceptance and rollback plan: [reviewed plan](background-jobs-and-adaptive-exit-plan.md).

The statistics POST is a read operation in [Hangfire's JsonStats dispatcher](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Dashboard/JsonStats.cs); its [official client](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Dashboard/Content/js/hangfire.js) polls it via POST. Supporting that exact operation does not allow enqueue/retry/delete commands.
