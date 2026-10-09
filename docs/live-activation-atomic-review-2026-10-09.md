# Atomic owner activation review — 2026-10-09

The previous review missed a production interleaving. Its read-only probe held
one lease through reconciliation and assessment, but the actual owner command
released that lease between reconciliation and Arm. The probe therefore did
not faithfully exercise the failing transition.

## Observed failure

The real persisted audit records a successful owner daily baseline at
09:43:02 UTC, background `BROKER_EVIDENCE_UNAVAILABLE` at 09:43:31, then
`OwnerArmBlocked` with `STALE_SNAPSHOT,RECONCILIATION_REQUIRED` at 09:43:42.
The background failure invalidated the snapshot after the owner had verified
it. The later successful background cycle did not automatically restore the
owner's execution authority.

## Repair and independent source review

- The explicit local owner command now holds one account lease across its
  complete two-read reconciliation, any required baseline recovery, broker
  preflight and Arm. It commits once after the final gate decision. Existing
  recovery and accounting routines are reused; no freshness timestamp is
  fabricated and no stale snapshot is accepted.
- The request pins the account, broker binding and approved mandate version.
  Stop is checked before broker reads and remains protected by the final
  deterministic gate. Browser approvals retain their existing strict snapshot
  semantics. No new HTTP/model capability or financial limit is introduced.
- Background reconciliation retains its 25-second bounds per broker read.
  Its outer budget is 120 seconds: up to 60 seconds acquiring the lease, two
  bounded reads and database work. Previously its 60-second total budget could
  be consumed by waiting for a healthy owner operation, causing an artificial
  evidence failure immediately after activation. Snapshot/preflight freshness
  limits remain 30 seconds; actual evidence failures still disarm.
- Empty native stderr records no longer print the PowerShell exception type.
  Actual stderr messages and native nonzero exits remain intact.

Codex reviewed the actual production and test changes after the supporting
worker edits. The first worker tests did not create a genuinely stale snapshot
and queued the competing writer before the source read; those checks were
corrected before acceptance. The queue test has bounded waits and observes
committed Arm before the intentional subsequent failure disarms the account.

## Verification

- Headless trading checks: 1,049 passed, zero failures/skips.
- Isolated PostgreSQL integration checks: 162 passed, zero failures/skips.
- Regression checks reproduce both reported reasons before owner recovery,
  verify refreshed evidence and unchanged funds/mandate, prove the competing
  writer cannot intervene before Arm, and retain fail-closed broker failures,
  owner Stop, changed mandate/binding and strict browser approvals.
- `tools/test-activation-console.ps1` replays the actual renderer under Windows
  PowerShell 5 with stdout, multiline/empty stderr and exit codes 0 and 3.
  It never invokes Docker or activation. Both exits/messages passed.
- A real read-only diagnostic invoked the production
  `LiveOwnerActivation.ActivateFullSpotAsync` flow against the existing account
  and full broker catalog. Its store wrapper holds the real account lease but
  substitutes detached transactions whose commits cannot write the ledger.
  The candidate passed with no violations in 61.741 seconds, with one detached
  commit. Durable execution stayed off; funds, orders, fills, mandate and Stop
  remained unchanged. No order was placed or cancelled.

The owner and company images were rebuilt. Only the disarmed owner host was
recreated; company execution was not started by this repair. Actual financial
activation remains the owner's explicit existing launcher action.

After deployment, the running image matched the new build and the application
returned HTTP 200. The same complete production owner flow was exercised again
with detached commits immediately after restart, while background reconciliation
was active. It passed with no violations in 76.574 seconds, again with one
detached commit and durable execution off. The queued background cycle then
completed successfully: saved reconciliation evidence advanced from 09:57:22 to
09:58:36 UTC without a new blocked event. Read-only verification confirmed cash
4,999.12 THB, no reserved cash, no outstanding orders, mandate version 5, unchanged
funding limits, Stop false and the owner's existing daily frame unchanged.
