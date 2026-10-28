# Crypto accounting and delivery

## Accounting scope

The Crypto screen separates wallet valuation from **confirmed engine fills**.
Imported exchange history stays available, but a recent-history window does not
prove a complete account ledger. Deposits and withdrawals are not trading profit.

| Measure          | Evidence and calculation                                                                                                                                            |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Latest buy       | Total gross THB divided by filled quantity for the latest confirmed engine buy order; excludes fees.                                                                |
| Average cost     | Remaining engine holding cost divided by broker-credited units; includes paid entry fees. Shown only when fill replay, retained holding and wallet units reconcile. |
| Realized P/L     | Confirmed engine sell proceeds minus paid exit fees and released weighted entry cost.                                                                               |
| Unrealized P/L   | Reconciled holding value minus remaining entry cost. Future exit fees are excluded.                                                                                 |
| Net P/L          | Realized plus unrealized P/L in the same engine scope; unavailable when either is unknown. Fees are already included and are not deducted twice.                    |
| Fees paid        | Actual net fees on distinct confirmed engine fills. Fee credits reduce paid fees.                                                                                   |
| Account-wide P/L | Remains unknown until historical costs, transfers and broker conversions are reconciled.                                                                            |

- Paper trades and imported copies of engine fills are not included again.
- Partial sales release proportional cost; a completed sale clears the position's
  cost before a later purchase starts a new basis. Confirmed realized P/L remains.
- Replay uses the execution ledger's precision and duplicate-identity checks.
  Conflicting or unsupported evidence leaves the affected symbol unknown.
- Latest buy uses the same execution-identity checks: repeated fills count once,
  and conflicting identities leave that symbol's last buy unavailable.
- Missing quotes do not erase verified realized P/L. Missing basis is never zero.
- A newer capital observation may refresh the read-only wallet view only for the
  exact account, inside the saved freshness limit, without blockers or uncertain
  orders. Reserved THB is not added to available THB.
- The page polls read-only snapshots every 15 seconds, or every two seconds during
  an explicit sync. Requests do not overlap and stop when the page unmounts.

## Coin history recovery

- Wallet rows require distinct ASCII coin symbols, ignoring letter case. Invalid
  or ambiguous rows make the response unavailable; they are not silently combined.
  Valid symbol spelling, balances and unknown values remain intact.
- Each history response must match the requested ASCII coin symbol, ignoring
  letter case. Invalid or missing timestamp offsets make that response unavailable;
  valid timestamp strings and retained transaction IDs remain unchanged.
- A history read times out after eight seconds and offers **Retry**. Retry rereads
  saved history. A late response cannot replace a newer request or repopulate
  history after invalidation or leaving the page.
- Successful history remains cached until the imported-history observation changes.
  Empty history, zero-quantity legacy rows and optional coverage remain supported.

## Trading amount display

Trading decimal strings are formatted directly with native
[`Intl.NumberFormat`](https://tc39.es/ecma402/#sec-tointlmathematicalvalue),
keeping precision until the existing display rounding of at most two decimal
places. Ledger costs are added with aligned integer scales before that rounding.
Unknown values stay Unknown. Fields already represented as JavaScript numbers
cannot regain precision lost upstream; this change does not reinterpret them.

## Trading status responses

Paper and live intent statuses must use the exact supported wire values in the
[overview contract](../apps/web/features/trading/contracts/TradingOverview.ts).
The valid `unknown` state stays visible as unconfirmed. Unsupported states,
including case variants, make the response unavailable; **Refresh status** rereads
it. Response validation does not normalize or repair retained records and does not
confirm a broker outcome.
If a control update returns an unreadable status, the UI says the update may have
completed and asks for a refresh before trying again. It does not repeat the command.

## Saved balance confirmations

Manual-sale attribution identifies an execution by account, symbol and broker
transaction ID. Each symbol still needs its own verified history, order evidence
and owner confirmation. An ID shared by two symbols cannot merge their allocations;
conflicting evidence or excessive allocation within the same symbol remains invalid.
Imported records, retained raw IDs and proof identities are preserved.

After recording a shared transaction ID for different symbols, retain a
symbol-aware validator during rollback. Older validators group only by the raw ID
and reject that journal and its confirmation recovery, even though the stored
schema is unchanged.

Balance review retains an owner-confirmed proof in the current browser tab before
sending it. Reloading only reads that proof; it never repeats the confirmation.

After an exact sale confirmation succeeds and fresh status verifies the same
account, the drawer prepares the next unambiguous holding or cash preview.
Each preview still needs its own explicit acknowledgement and confirmation.
Uncertain outcomes retain the original proof without advancing or retrying it.
Successful responses clear only their exact saved proof; a late response from an
older review cannot erase a newer confirmation's recovery copy.

A retained order with an unresolved cancellation still blocks balance changes,
even if its recorded order status is terminal. Read-only broker inspection can
resolve that cancellation; only a validated terminal observation confirms it.
An unavailable observation preserves uncertainty and never resends a cancellation
or an order. Terminal status keeps its existing meaning for fills and reservations.

| Saved state                                          | Recovery                                                                                                                                                                                           |
| ---------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Readable proof, response unknown                     | Show the exact previous review. A retry needs a new acknowledgement and uses the same proof.                                                                                                       |
| Saved proof after rules, mode or order state changes | Allow an explicit retry only for the same account. The server returns an already committed result; an uncommitted review must still satisfy its original binding, expiry and current-state checks. |
| Unreadable proof                                     | Keep current account status visible and block another confirmation. The owner can explicitly discard the local copy, then refresh status.                                                          |
| Proof exceeds storage limits                         | Reject it before sending. Completed order history is excluded from the local review binding; retained server history is unchanged.                                                                 |

Discarding a local copy never undoes or confirms a server change. Historical cost
and profit remain unknown until supported by evidence.

Recovery does not pause, resume or enable trading. Refreshing status never sends
a confirmation. An expired proof can recover an already committed result, but
cannot authorize a new accounting change. A rejected retry keeps the saved proof
visible until the owner explicitly discards it.

Cash-review evidence compares exact decimal values independently of trailing zeros
and normalizes flow timestamps to the same UTC instant. Record ordering is stable;
unknown values remain distinct from zero. Changes to balances, flow identities,
amounts, kinds or instants still require a new review. Existing uncommitted proofs
also accept their original evidence hash when the fresh evidence matches it. Saved
results keep the reviewed hash, and committed retries recover that result without
re-reading the broker or crediting cash again. Trading-state and rules hashes are
unchanged.

## Telegram acknowledgements

| Stage                                     | Durable behavior                                                                                                                |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Before send                               | Claim the exact account's alert before any Telegram request.                                                                    |
| Positive acknowledgement                  | Store the message ID and send time in `TradingNotificationReceipts`, bound to account, destination, alert and exact claim time. |
| Aggregate update interrupted              | Recover the matching receipt into the original alert without sending another message.                                           |
| Old sending claim without acknowledgement | Retain an unknown outcome; never automatically resend it or report success.                                                     |
| Definite rejection                        | Keep existing rejection and bounded 429 retry rules. A timeout is not a definite rejection.                                     |

Receipt writes are immutable and safely repeatable; external sends are not retried
after an uncertain outcome. Recovery is bounded and preserves existing alerts,
fills, rules hashes and trading authority. If acknowledgement storage also fails,
the outcome remains unknown rather than claiming exactly-once delivery.

Daily reports use one independent 15-second cancellation budget for outcome
reads, gate waits and up to three revision-based saves after a send or edit.
Caller cancellation cannot prevent this bounded completion attempt. If the budget
expires, the last durable claim remains available for existing stale-claim
recovery; an uncertain send or unchanged edit is never automatically repeated.

### Dispatch identity compatibility

New dispatch tables use BIN2 keys. Initialization also prepares a transactional
repair for legacy `TradingJobDispatch` collations so case-distinct canonical alert
IDs have independent rows. The owner approved this one-table schema exception;
it does not change alert receipts, account data or trading authority.

| Stage                  | Preservation boundary                                                                                                                                                        |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Initialize             | A database-scoped application lock serializes schema work; the legacy table is locked during repair.                                                                         |
| Repair                 | Retain row values, jobs, leases, due times, attempts and states; restore the supported primary key and defaults.                                                             |
| Unsupported definition | Roll back the entire change. Custom target statistics, annotated constraints, non-ASCII account bindings and unsupported key layouts need an explicit preservation decision. |
| Repeated initialize    | An already BIN2 table requires no key rebuild.                                                                                                                               |
| Application rollback   | Keep the BIN2 schema; older native comparisons remain case-sensitive.                                                                                                        |
| Schema downgrade       | CI_AS cannot retain case-distinct key pairs. Never merge or delete retained records to force a downgrade.                                                                    |

The repair supports a single-partition row-store key on account then alert, with
its name, clustered/nonclustered form, filegroup, compression and retained index
options. Other dependent objects can block the change; failure rolls back rather
than removing them. SQL Server may regenerate automatic column statistics when
collation changes. See Microsoft's [column collation guidance](https://learn.microsoft.com/en-us/sql/relational-databases/collations/set-or-change-the-column-collation)
and [ALTER TABLE documentation](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-table-transact-sql).

`--dispatch-migration-sql-only` is opt-in through
`QUANTARA_DISPATCH_MIGRATION_SQL_CONNECTION`. It reuses the exact server,
date-named Crypto test database, write consent and fixture marker checks, and
requires the dispatch table to be absent before seeding retained fixture rows.
`QUANTARA_DISPATCH_MIGRATION_START_SCHEMA` selects `existing-destination` by
default or `missing-destination` for older tables. Each mode needs a separately
provisioned fresh marked fixture database. The check retains its fixture tables
and rows. Source/build checks do not establish that the SQL migration has
executed successfully.

## Verification

- Deterministic Portfolio checks cover fee credits, partial fills, closing/reopening,
  broker precision, duplicates, invalid evidence and account snapshot isolation.
- Delivery checks cover concurrent jobs, timeouts, failed aggregate commits,
  receipt persistence failures, report completion budgets and restart recovery
  without network sends.
- Opt-in receipt SQL checks use an isolated `QuantaraFeatureChecks*` database.
- Crypto fixture checks mock every API call and cover values, nulls, negative P/L,
  old responses, polling lifecycle and desktop/tablet/phone layout.
