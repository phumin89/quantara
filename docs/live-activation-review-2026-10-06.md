# Live activation round: 2026-10-06

Status: implementation and synthetic activation checks passed; actual Live trading remains INCOMPLETE. The owner requested activation and selected full available Spot capital. Activation authorization is no longer an outstanding clarification. Current signed broker observations show THB 0, Trade permission passed, stable empty wallet, no supported open orders or history. No order, cancellation or activation occurred.

## Implementation and independent review

- Added strict before/buy/sell capture verification and a private profile-writing command. It rejects changed keys, stale/future evidence, incomplete or open orders, changed market rules, conflicting retained history, unsupported lineage, ambiguous credited fees, and mismatched wallet settlement. Only observed BTC conventions are selected. Exact quantity evidence keeps Exact rather than claiming rounding was demonstrated. Original byte sequences are hashed with length prefixes; the hash binds local evidence, not a broker signature.
- The writer rechecks the current credential, Trade permission, two matching wallets, all three supported open-order lists, current BTC history/order details/market rules and inactive configuration. A private file lock serializes cooperating verifier processes; an existing nonempty verified ruleset cannot be replaced by this tool. Actual zero/reused captures were rejected and the private profile hash remained unchanged. Captures and private configuration stay ignored by Git.
- Added an explicit local owner activation command using existing provisioning, baseline, reconciliation and owner controls. Fresh positive THB determines initial capital. Existing mandates, losses, activation history and Stop survive. Snapshot-only stale approval can retry once with the same mandate; a mandate change or Stop is never accepted by that retry. No API/model capability was added and the command itself places/cancels nothing.
- Corrected startup recovery: an absent account waits; a subsequently provisioned account retains its new owner activation. An account already present at executor startup is still durably disarmed before traffic. Both cases are covered against actual PostgreSQL with a manually triggered timer.
- The operator script validates configuration before changing the working app, checks CEO prerequisites and binding, stops prior CEO activity, then activates. Pre-activation startup failures restore the old executor configuration. After an activation attempt, failures keep CEO disabled and report possibly committed authority instead of claiming a database rollback. No money or Stop is reset.

Review corrections: a local worker's timestamp mutation did not change its fixture; fixed it before accepting tests. Corrected worker assertions against the real domain types. Hardened null/duplicate fill checks and required positive filled evidence. Rechecked credentials and all supported open-order lists at profile preparation. No dependency or new UI was added.

## Validation

- 536 headless domain/Live/Bitkub/owner/company checks passed, no skips.
- 68 integration checks against isolated actual PostgreSQL passed, no skips. Includes full-Spot activation, missing funds/permission, preserved Stop, and both startup recovery cases. Synthetic HTTP and keys only; test containers never received production credentials or AI auth.
- Broker reader publish and executor/company production image builds passed. The actual reader returned THB 0 and successful Trade probe. The actual unverified profile was rejected by `--verify-live-configuration` without mutation or activation.
- Updated read-only executor deployment retained remembered viewing. Normal TLS routes `/`, `/account`, `/trading` returned 200; anonymous private setup returned 401. Execution and company remain disabled. No frontend changes; no new responsive review was necessary.

## Remaining actual proof

Funding and a completed broker buy/sell evidence set are required before generating the private profile. No account record has been provisioned from the zero wallet. The initial verifier supports cash-only BTC and direct terminal order lineage, at most eight fills per order. Unsupported partial lineage, ambiguous fee credits or rounded sell cash remain blocked rather than inferred. Captures must be recent and have no intervening deposits/other activity.

The published THB trading fee is 0.25%; the profile uses a conservative 0.35% cash ceiling including a one-cent allowance at the 10 THB minimum. That ceiling is not a claimed actual fee or proof of rounding. Wallet equality must still pass exactly. Primary references: [Bitkub fees](https://www.bitkub.com/en/fee/cryptocurrency), [REST V3](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md).

After evidence passes, the owner can intentionally run the activation script or use existing owner controls. A successful real app order, exact wallet reconciliation, durable persistence and restart recovery remain the release acceptance proof. Synthetic success and a green readiness screen are not a completed real trade. See [the operator steps](bitkub-small-capital-proof.md).
