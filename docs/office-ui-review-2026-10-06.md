# Office design and implementation review — 2026-10-06

Historical presentation round (`45d8a19`). The subsequent [activity and copy review](live-office-activity-review-2026-10-06.md) supersedes its static-viewer description and checklist.

The owner requested agents to review and redesign an Office that appeared empty and inactive. This round changes presentation and explanation of verified state; it does not activate trading or add simulated activity.

## Independent review

Assessment A: `/root/office_design_review`, Codex fallback after the local vision bridge failed because its configured executable was missing and LM Studio was unavailable. Assessment B: `/root/office_mechanical_review`, isolated from A. The lead owned scope, implementation decisions, actual diff acceptance and verification. `/root/office_layout_patch` owned only `workspace.css`.

| Finding | Change |
|---|---|
| The blocking reason and next action were buried in a floating reader. | Office opens with current system state and one recovery/readiness link. |
| Office occupied a very wide dark stage while important information was a small rail. | Balanced Pixel/account grid, responsive stage heights and readable cash/help sections. |
| Empty chat repeated disabled state in a tall window. | Content-sized unavailable/disabled chat with one status and recovery action. Existing report windows remain full readers. |
| A stationary empty room looked like a broken activity monitor. | Caption/help identify the viewer as a static image; reports and orders provide evidence of actual work. |

The existing left navigation, colored Pixel assets, self-hosted IBM Plex Sans Thai, single minimize button and Escape/focus return remain. Native disclosures and existing account-bound reports remain. No dependency, broker request, authorization, mandate or execution change was added.

## Verification

- Lead inspected the actual TSX/CSS diff and the coordinator gate: a meeting requires a valid armed packet; a UI status does not prove a meeting is running.
- Frontend tests: **75 passed**, including five new tests for read-only funds versus permission, missing versus zero balances, reconciliation/permission versus activity, unavailable reports and single-control compact chat.
- TypeScript check passed. Final Docker production build passed and generated `/`, `/account` and `/trading`. Only `owner-web` was recreated; owner process and remembered viewing state were retained.
- Assessment B ran the markup detector exactly once on `LiveAccountPanel.tsx`, `LiveOffice.tsx` and `CompanyChat.tsx`: exit 0, JSON `[]`, zero rule/file findings. No overlay was injected because browser evaluation is read-only.
- B observed the old production build independently before deployment. The lead reviewed the deployed build in two bounded passes: 1440×1000 desktop, 900×1000 tablet and 390×844 phone; final pass also checked the owner's 3434×1439 wide-screen size.
- No horizontal overflow; one H1; primary controls and minimize target at least 44px. Desktop compact chat measured about 355px tall; phone reader filled the viewport. Escape returned focus to the launcher. Tablet stage reduced from 378px to 300px after the first pass.
- Browser QA used an anonymous test session; connected cash, zero, stopped and armed states were verified with deterministic render fixtures. The owner's paired browser was not reset. Screenshots are local ignored artifacts under `work/office-ui-review-2026-10-06/`. Temporary tabs were closed and viewport override reset. No temporary server was started.

## Remaining Live work

Pixel does not consume a Live activity stream; it draws no characters. Actual meeting/activity events must be connected before rendering active workers or animations. Readiness, permission and recorded outcomes must remain distinct from in-progress work.

Trading remains disabled in this runtime. Broker buy/sell accounting proof, verified profile, bound reconciled account and owner activation are still required. This design round does not claim completed Live trading or that any real order was placed. Unreadable reports are described as unavailable, never as proof that no work occurred.
