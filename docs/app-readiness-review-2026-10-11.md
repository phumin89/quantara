# Application readiness review — 2026-10-11

## Conclusion

The company is producing reports, but the real account is **disarmed**. A saved BUY recommendation is not a registered plan or a broker order. This review does not certify unattended real-money operation or profitability.

Read-only audit evidence shows an owner activation on **2026-10-10 16:40 Bangkok**, followed by recovery suspensions and occasional unavailable broker evidence. Startup deliberately disarms the executor. Successful later reconciliation refreshes the account, but does not restore authority. Therefore, the remaining production prerequisite is owner activation through the control flow; missing charts for unowned USDT/XRP/DOGE do not themselves pause all other eligible coins.

## Confirmed defects and fixes

| Finding | Evidence | Result |
| --- | --- | --- |
| Owner rejection details never reached the UI | `requestJson` discarded non-success bodies. A component test returned an actual-format 409 and reproduced the generic message instead of `UNRESOLVED_ORDER`. | Owner mutations explicitly opt into reading 409 JSON. Only bounded, recognized codes become Thai explanations. Unknown text remains sanitized. The test verifies one POST, renewed status and cleared confirmation, without a retry. |
| Disabled activation had no adjacent explanation | Setup checks control, baseline, reconciliation, expiry and confirmation, but the disabled button did not identify the missing condition. | One prioritized explanation is attached with `aria-describedby`. Broker trading permissions remain distinct from browser control permission. Existing guards and financial authority are unchanged. |
| Owner status could claim execution readiness during a receipt-stream outage | `/internal/live/research`, activation and execution already depend on the receipt observer; `/live/status` did not. | Status now returns `RECEIPT_STREAM_UNAVAILABLE`. An endpoint regression verifies BLOCKED while preserving the actual gate and creating no order. The underlying stream already resets readiness in its `finally`; no redundant stream refactor was retained. |
| A brief executor restart left the company waiting five minutes | After the owner rollout, the account was reconciled and HTTPS recovered while company activity still reported `LIVE_READ_UNAVAILABLE` with a five-minute deadline. | Only pre-meeting account-read failures retry after 30 seconds. Tests suppress reads and model work during backoff, recover from fresh data, preserve disarmed authority and create neither a plan nor an order. Claimed meetings and financial submissions are not replayed. |

The lead independently inspected the changed code. An optional local reviewer could not run because its configured Codex executable path was stale. The UI worker used its permitted direct fallback; the lead rejected incomplete helper-only proof and added the component request-flow regression that exposed the discarded response body.

## Validation

- 51 frontend checks passed, including report/date reads, owner controls, exact rejection reporting and no mutation retry. Typecheck passed.
- 169 focused domain/endpoint/coordinator checks passed. Following the recovery retry change, the complete 98-check coordinator class passed again.
- 51 PostgreSQL integration checks passed using an isolated test network/database and fake broker, with no account credentials mounted. They cover explicit activation, Stop, restart persistence, exactly-once entry, acquired-quantity exits at target/stop/expiry, and rejected/uncertain outcomes.
- Owner, web and company production images built. The Impeccable detector returned no findings for the changed UI targets.
- One component inspection round at 1440, 820 and 390 pixels found no horizontal overflow, one blocker message and the matching accessible description. Its values were explicitly labelled as examples. The available browser session was unpaired, so this is not a real authenticated activation demonstration.

## Rollout and remaining limitation

Owner and web were updated after the 23:00 meeting completed, preserving runtime mounts and existing volumes. The company follow-up was updated between reviews, without restarting the owner again. Running image identities were checked against the builds and HTTPS `/trading` returned 200.

Before and after the owner rollout, the mandate and owned-money projections had identical database hashes. Stop remained false, the real order count remained **one**, and unresolved orders remained **zero**. Reconciliation recovered. The legacy `UseFullPortfolio=false` grant was not migrated by deployment; the owner can explicitly select the supported 100% policy in Trading.

An executor restart or unavailable financial evidence still suspends real trading and requires owner reactivation. This remains an operational limitation for unattended use; the 30-day browser-control option remembers access, not financial activation. A future restart-resumption policy must be an explicit owner grant with verified recovery and preserved Stop, account binding and risk validation. This review did not introduce or activate such a policy.

No real order was placed or cancelled, no limits were changed, and the initial 30-THB test purchase was not used as performance evidence. Private probes, screenshots and diagnostic fixtures remain under ignored `work/` paths.

Final runtime verification showed the **23:15 READ_ONLY** meeting completed and persisted, with all five meeting stages reporting completion and the next review scheduled for 23:30. A temporary `ACCOUNT_STATE_INVALID` state during recovery was also inspected: an old reconciled snapshot/its quotes were rejected by the unchanged 30-second guard, while separate public quotes were fresh. The company subsequently completed the review without an authority change. This demonstrates report/research recovery, not real-money execution; periodic stale windows remain visible rather than being called current data.
