# Crypto accounting and delivery

## Accounting scope

The Crypto screen separates wallet valuation from **confirmed engine fills**.
Imported exchange history stays available, but a recent-history window does not
prove a complete account ledger. Deposits and withdrawals are not trading profit.

| Measure          | Evidence and calculation                                                                                                                                            |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Latest buy       | Gross-weighted price of the latest confirmed engine buy order; excludes fees.                                                                                       |
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
- Missing quotes do not erase verified realized P/L. Missing basis is never zero.
- A newer capital observation may refresh the read-only wallet view only for the
  exact account, inside the saved freshness limit, without blockers or uncertain
  orders. Reserved THB is not added to available THB.
- The page polls read-only snapshots every 15 seconds, or every two seconds during
  an explicit sync. Requests do not overlap and stop when the page unmounts.

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

## Verification

- Deterministic Portfolio checks cover fee credits, partial fills, closing/reopening,
  broker precision, duplicates, invalid evidence and account snapshot isolation.
- Delivery checks cover concurrent jobs, timeouts, failed aggregate commits,
  receipt persistence failures and restart recovery without network sends.
- Opt-in receipt SQL checks use an isolated `QuantaraFeatureChecks*` database.
- Crypto fixture checks mock every API call and cover values, nulls, negative P/L,
  old responses, polling lifecycle and desktop/tablet/phone layout.
