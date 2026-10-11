# Live report recovery review, 2026-10-11

## Problem and resulting behavior

The owner repeatedly saw missing-chart text without a saved explanation of why the entire meeting was advisory. A report now records a closed set of actual advisory causes: executor readiness, held-asset history, public research availability and reserved funds. Unowned-coin chart gaps remain separate, behind one closed disclosure. Legacy reports explicitly say their cause was not recorded; current status cannot invent a historical cause. Null metadata stays omitted, preserving historical journal hashes and report replay.

Production diagnosis also found roughly **68 MB** of expanded account JSON, largely reconciliation audit entries. Sending an already-persisted report still read and rewrote that account. The immutable-audit guard repeatedly extracted the full new audit array for each old entry. A known exact replay timed out at 10 seconds, even though the journal and report validation succeeded.

Report reads now project only meeting audit details and preserve row, account, snapshot and mandate binding. Exact saved replays return before obtaining the financial lease; conflicts still fail. New reports revalidate under the original lease. A no-op commit retains the database revision. The audit trigger expands each array once and compares every old ordinal; it still rejects edits, removal and reordering. No audit was removed, compacted or archived.

## Independent review and checks

The lead reviewed the actual changed files. A supporting review identified a missing stored-identity check in the first report projection; the lead restored it and added three database regression cases.

- 173 focused domain checks passed, zero failures or skips.
- The full 169-test isolated PostgreSQL integration suite passed. After restoring projection binding, its nine report-persistence checks passed again, including the three new identity cases.
- 20 report UI checks passed; frontend typecheck and production build passed.
- 21 research/provider/reader checks passed after adding bounded WLD coverage.
- Synthetic report UI inspection at 1440, 820 and 390 pixels found no horizontal overflow; missing-chart details stayed closed and the date field retained its touch height.
- Owner and company production images built successfully; `git diff --check` passed.

Windows AppControl prevented one newly built assembly from loading during a local run. The same relevant checks ran successfully in an isolated Linux SDK instead; host security settings were not changed. Test databases, networks and mounts were separate from the real account and auth files.

## Runtime evidence and rollout

The updated web application returned HTTPS 200. The known old report replay returned HTTP 200 in a probe taking about **0.7 seconds** including its activity/journal reads; another direct saved replay took about **0.48 seconds**. Report-persistence checks establish that replay does not rewrite the account or increase its revision. Real order count remained **one** throughout the probes.

The final owner/company update preserved existing named volumes and verified mount locations. One earlier preparing daily meeting was recorded as interrupted during deployment; the following 14:00 review completed and delivered today's eight-role learning evidence. Later updates checked for zero claimed meetings before restarting the company. Startup recovery temporarily marks the snapshot unreconciled and may leave the coordinator in its five-minute retry interval; read-only packet probes later showed reconciled, fresh data. These states must not be described as live execution.

The actual account remained **unarmed**, Stop remained false, and the legacy funding grant remained fixed (`UseFullPortfolio=false`). The software supports an explicit full-portfolio owner grant, but deployment and model output cannot migrate the actual grant. The owner must select that policy and activate from the local owner control flow after reconciliation. No activation, order placement/cancellation, withdrawal, paid provider or secret inspection was performed.

The additive report field can be absent in old saved data. The SQL function can be rolled back to its previous immutable-prefix comparison without changing stored documents or schema. Rollback must retain the new optional field's reader support once a report containing it has been persisted; dropping that property would change the content-addressed journal representation.
