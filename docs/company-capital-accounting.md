# Company capital accounting

The capital provider funds the company; routine accounting must not require manual
balance edits or treat deposits as trading success. This increment establishes a
deterministic accounting boundary before autonomous planning or policy selection.

## Implemented

- `CapitalFlowEvidence.Unique` validates broker movement identities, amounts,
  timestamps and kinds. Identical imports count once; changed evidence for the same
  identity is rejected. Account-specific identities remain partitioned by the caller.
  Existing risk performance, objective deposits and risk persistence use this path.
- `CompanyCapitalAccounting.Reconcile` consumes aligned broker opening/closing
  valuations, complete account transfer coverage, and classified movement evidence.
  It excludes verified external deposits and withdrawals from company equity change.
- Internal movement requires both account-scoped broker records and a retained
  evidence reference linking withdrawal to deposit. Direction, native asset and
  quantity must reconcile. Matching on similar amounts or dates alone is insufficient.
  A record cannot be classified as both an external contribution and an internal leg.
- Native delivered quantity must equal withdrawal quantity minus its recorded fee;
  unsupported receiving-fee semantics are rejected. Existing cash-flow `ValueThb`
  represents net delivered value, not gross debit. The internal transfer net-value
  difference is separate from native THB transfer fees. Non-THB fee valuation remains
  Unknown without separate verified conversion evidence.
- Identical links and repeated imports are idempotent. Missing legs, unknown THB
  valuation, unclassified movements, incomplete coverage, mismatched cutoffs, uncertain
  orders or non-broker capital produce Unknown totals with explicit reasons.

The report preserves the interval `(opening observation, closing observation]`.
The configured company account set is mandatory and retained in the report. A missing
configured account cannot silently shrink a company total; an extra account cannot
expand scope.
Its net change is closing marked equity minus opening marked equity minus external
deposits plus external withdrawals. Internal transfer fees, conversion differences
and unrealized valuation changes remain in that change. It is **not** realized
trading profit or company income after operating costs; `VerifiedIncome` is always
false. No target, risk limit, balance or execution authority is changed.

## Evidence boundary and remaining work

The existing Bitkub evidence reader imports completed fiat and crypto movements.
Company-wide reconciliation and transfer classification are currently offline
operations, verified with synthetic multi-account records. No Webull connection,
account entitlement, production import adapter, new API, scheduler or database
migration is introduced by these increments.

Production classification must come from verified broker/bank/transfer provenance,
not a model assertion or an owner labeling each monthly deposit. The adapter must
provide complete, aligned account evidence and retain the classification references.
Unverified Webull records must not be invented to make a company total look complete.

An offline application importer now connects the existing capital reader to this
journal. Production observation provenance and classifications remain unimplemented;
the fixture importer cannot admit them. Annual planning and derivatives remain
separate later increments. A missed stretch goal must never increase authority,
raise risk automatically, force trades, borrow or assume future contributions.

## Verification

`CompanyCapitalEvidenceStore` is a bounded local journal for the
`company-capital-fixture-v1` protocol only. It is not registered with the live
application. Every source is bound to configured broker, legal region, account and
`offlineFixture` environment; unsupported source/adapter claims are rejected.

Each receipt retains normalized observations, source reference, adapter version,
recorded time and a SHA-256 content digest. Account/import identity is idempotent
across decimal scale and explicit timestamp-offset representations: amounts use
invariant canonical decimal JSON and instants use UTC. Offset-free timestamps are
rejected. Journal amounts must be JSON numbers whose original value is exactly
representable as `decimal`; excess precision and nonzero underflow are rejected
before content-digest validation. The native JSON parser can otherwise round those
values ([.NET implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Number.Parsing.cs)).
Equivalent scale/exponent forms retain their value and digest, and reads never
rewrite the journal. Known equity must reconcile to the marked asset values; missing prices
cannot support a known equity claim. A restart retry using an old expected revision
returns the existing receipt. A different payload
under the same import identity is rejected. Another import identity cannot rewrite
same-time valuations, broker movements or existing classification facts. New
classification evidence can be appended to previously unclassified facts without
duplicating capital. These hashes detect content changes; they are not broker
signatures or proof that fixture records came from a real account.

The classifier needs reciprocal managed-account identities and the same retained
transfer reference on exactly two compatible broker observations. It never matches
on similar dates or amounts. Missing, mismatched or ambiguous references remain
unclassified and keep performance Unknown. Webull labels in fixtures do not imply an
implemented Webull reader, verified THB valuation or production entitlement.

Reports bind `EvidenceOrigin=offlineFixture`, the exact journal revision and the
digests used for that result. Marked equity change is still not verified income.
Reads do not create journal files. A malformed journal is not reset. Appends use an
exclusive writer file, expected-revision check and flushed same-directory temporary
payload before replacement; orphan temporary files are not treated as committed
evidence. The journal is bounded to 32 MiB and 10,000 receipts. Process restart and
pre-publication leftovers are tested; power-loss/filesystem durability is not claimed.

`dotnet run --project tests/Portfolio.Checks -- --company-import-only` exercises
temporary journals, deduplication/conflicts, later classification, source/scope,
mutual transfer references, restart, orphan payloads, cancellation, competing writers,
revision guards and changed content. It uses no live broker, SQL or credentials.

## Read-only application importer

`CompanyCapitalImporter` reuses `RiskCapitalReader`, the read-only risk store and
account context. Before source I/O it checks the exact broker, legal region, account,
environment, adapter and configured company scope. Only the Bitkub TH
`offlineFixture` adapter is admitted. The real Bitkub reader declares `production`;
changing the requested source label cannot admit it. Source/account identity is
checked again after asynchronous collection. This is local adapter metadata, not
cryptographic broker attestation or verified production entitlement.

The opening valuation must already exist; the importer never fabricates a baseline
or edits balances. The closing observation must be newer and cannot borrow a quote
from after its account cutoff. Both account and market reads use the existing
read-only ports. Broker movement records are retained with their original unknown
classification: a detected deposit alone does not prove an external contribution.
The only write is to the explicitly configured offline journal.

`CompanyCapitalReadinessGate` rechecks the complete configured account set, fixture
provenance/digests, report revision, closing freshness, nonnegative known valuations
and contribution/equity arithmetic. Missing coverage, prices, classification or
transfer costs keeps `CanSimulate=false`. The result revision follows the actual
report snapshot, including an append between import and report reads.
`CanUseForLiveDecisions` is always false. No API, DI registration, scheduler,
production journal, trading state or company loop is changed.

`dotnet run --project tests/Portfolio.Checks -- --company-importer-only` verifies the
actual application bridge with fake readers and temporary journals, including
restart/retry, unclassified monthly funding, missing opening/asset prices,
source/region/environment/account/adapter rejection before I/O, source changes
during collection, quote cutoff, stale/scope/arithmetically invalid consumer reports
and concurrent report revision. The standard Portfolio checks also run this suite.

## Minimum path to an end-to-end company simulation

1. Compose an offline runner with explicit fixture accounts, synchronized account
   and market cutoffs, retained opening valuations and verified fixture movement
   classifications. Append classification evidence through the existing journal;
   incomplete multi-account evidence must stop the cycle.
2. Feed a ready report and versioned company objective, budget and risk boundaries
   into a bounded management/research/allocation/review cycle. Reuse the existing
   Paper fill and historical replay calculations for simulated execution, with a
   separate fixture store and no real broker, model worker or live scheduler.
3. Retain each cycle's evidence revisions, decisions, simulated fills, fees and
   outcome review. Test restart, duplicate cycles, monthly funding, internal
   transfers, missing evidence and losses through the whole cycle. Reconcile the
   resulting marked capital separately from realized trading P/L and operating
   costs; unknown company income must stay Unknown.

These steps establish an offline simulation, not a production company. Production
still needs admitted broker/account/region/API capabilities, complete aligned
observation and transfer provenance, and a separately reviewed integration with the
existing service boundaries. No simulated result can authorize live orders or
expand the owner's capital/access limits.

`dotnet run --project tests/Portfolio.Checks -- --company-capital-only` runs offline
monthly contribution, duplicate/conflict, multi-account transfer, missing-leg,
unknown-cost, coverage, cutoff, uncertainty and paper-isolation fixtures, followed by
the existing objective, broker-evidence and risk-pipeline fixtures. It does not access a real broker
or SQL instance. SQL conflict/idempotency remains covered by the existing transaction
and account-scoped primary key; live SQL checks are opt-in.
