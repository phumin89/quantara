# Reconciliation and capital provenance review

These changes preserve the existing trading mandate, policy and live activation
requirements. They do not activate trading or resend uncertain orders.

Broker inspections run in bounded concurrent batches of four, prioritizing the
oldest inspection. Each has a ten-second linked timeout. Preparation returns after
that batch so healthy evidence is consumed before the thirty-second freshness
limit; subsequent batches rotate through retained due orders. Prepared evidence
must still match the entire retained intent and cannot be future-dated or stale.

An unavailable or malformed broker observation changes only that order to unknown.
All fills in an observation are validated before applying any accounting change.
Existing uncertainty guards block fresh entries; cancellation and resubmission are
not a fallback. Store/CAS failures still propagate and cannot be mislabeled as a
broker observation failure.

Capital snapshots now carry explicit `Broker`, `Paper` or `Unknown` provenance.
Portfolio's real capital projection accepts only coherent, fresh `Broker` evidence.
Legacy payloads without a source deserialize as `Unknown` and remain unavailable
until refreshed from the broker. A rollback to an older binary must account for
its strict JSON schema before it reads newly persisted provenance fields.

SQL mirror comparison reuses UTF-8 buffers while retaining exact serialization
equality. Equal numerical values with changed decimal scale or timestamp offsets
still violate immutable history; retained identities cannot disappear or duplicate.
The synthetic 10,000-fill comparison reduced measured allocation from 16.50 MB to
1.63 MB. Timing varied from roughly 30 ms to 25 ms in that fixture. This does not
establish lower production CPU, database latency or trading decision latency.

Company status separates historical failed reports from the current evidence
deferral, including after its deadline. A current portfolio/storage failure takes
precedence over retained deferral metadata. Historical evidence remains available.

Independent source review found and rechecked the reconciliation starvation,
status precedence and immutable-history regressions. Deterministic regressions
failed before their fixes and passed after them. Full Portfolio, Team, MarketData,
Portfolio API, Architecture and Analyzer checks passed using fake dependencies;
SQL integration checks remained disabled. Production runtime was not restarted,
deployed or exercised by these checks.
