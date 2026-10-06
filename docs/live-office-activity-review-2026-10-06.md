# Live Office activity and copy review — 2026-10-06

The owner found the previous Office checklist repetitive and the empty room misleading. This round removes that checklist and connects the existing meeting lifecycle to the Pixel viewer. It does not start advisory work or activate trading in the unverified runtime.

## Findings and fixes

- Removed the three-step Office checklist, duplicated access explanations, static-image caption and unused styles. Disabled chat has one status and a recovery link. Trading explains its first-use proof requirement in one disclosure instead of a generic release checklist.
- The Live host previously constructed `AiCompanyMeetingRunner` without its existing event publisher. A display publisher now projects only actual role/status/time events. It never sends prompts, model results, proposals or credentials to the viewer.
- Authenticated coordinator transport saves a single account-bound activity snapshot; remembered owner viewing reads it. Other accounts, unknown roles, concurrent working roles, stale/future timestamps and old snapshots are rejected. This cache does not touch the financial ledger.
- A 15-second heartbeat renews a 45-second display lease. The worker clears unfinished work in its finalizer, including provider failure paths that do not emit a failure event. Transport failure cannot interrupt risk checks or meeting decisions.
- The parent validates account scope and freshness before sending a fixed-origin message to the frame. The frame accepts only its parent at the owner origin, clears missing/expired activity, respects reduced motion and animates only actual work. Completed/idle roles stay at stable seats; no roaming or fabricated roster.
- Lead review corrected null-meeting and zero-account validation gaps and a sofa-first seat mapping. Four existing chairs and two benches are preferred, with separate remaining seats. Production compilation exposed DOM types in the Node test project; structural message identities fixed that without weakening source/origin checks.

## Verification

- Lead reviewed the actual changed source, authorization groups, account binding, telemetry isolation, expiry and projection behavior independently of worker reports.
- Headless strategy/risk/accounting/Live/Bitkub/owner checks: **579 passed, 0 failed, 0 skipped**. Includes actual publisher → fixed HTTPS client → authenticated endpoint → display cache, with no broker or ledger mutation.
- Frontend: **77 passed**; TypeScript passed. Pixel activity: **10 passed**; both TypeScript project configurations and focused lint passed. Docker production builds for owner, frontend and isolated company passed.
- The markup detector ran once. It flagged an unused legacy tab selector; the selector was removed. No detector loop or new dependency was introduced.
- Deployed owner and frontend to the existing `quantara-owner-readonly` Compose project, preserving its owner-state volume and ledger. An accidentally created duplicate project was removed before the existing app was updated; no second application remains. The isolated company image is built but its service remains off.
- Real browser review at 1440×1000, 900×1000 and 390×844: no horizontal overflow, one H1, primary and minimize controls at least 44px, crisp office stage at desktop/tablet/phone sizes. Native navigation worked; phone chat filled the viewport; Escape restored launcher focus. Final desktop compact chat measured 190px with one minimize control. No checklist remained. Browser QA was anonymous; account/activity validation and connected financial states use deterministic tests, not fabricated production events. Screenshots are ignored local artifacts in `work/live-activity-review/`. Two bounded passes were completed; the temporary tab was closed and viewport reset.
- HTTPS application response is 200; anonymous `/live/activity` response is 401. No pairing code or API key was emitted or changed.

## Remaining activation requirement

Read-only broker checks confirm wallet access, Trade permission and a stable account without open supported orders. No matched trade history was available, and the broker verification profile has no verified execution rules. Real buy/sell accounting proof is still absent.

The existing evidence checker needs private before-buy, after-full-buy and after-full-sell captures from the same account, with actual quantity, fee and wallet agreement. The owner performs the financial actions; the checker captures and verifies results without placing orders. Successful proof is followed by the existing account/limit/reconciliation/activation flow. A passing code test or an activity animation cannot replace this requirement.

The runtime keeps execution and the isolated company disabled until that flow completes. No actual order, pairing reset, credential change or new scheduled paper/AI work was performed in this round.
