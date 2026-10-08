# Live UI loading stability review — 2026-10-07

## Findings and changes

The app already uses Next client navigation, a persistent root account provider and real Office, Account and Trading routes. The reported movement was caused by changing content geometry rather than an explicit document-refresh loop.

- Office conditionally inserted its multi-line activity header after receiving a heartbeat. In an isolated source-component preview at 1440px, the Pixel frame moved from 225px to 320.39px vertically. The header now remains mounted: a concise status occupies a reserved two-line slot, and work details belong to an always-present closed native disclosure. Missing observations remain unknown; they do not fabricate activity.
- The connection indicator appeared and disappeared with each pending account poll. Its space now remains present while inactive and is hidden visually and from assistive technology.
- A failed meeting-history poll removed the selected day's report and disclosures. Saved same-date reports now remain mounted during pending reads and poll failures, with a reserved update notice. Changing dates still hides another date's conclusions. The date index, cancellation, authorization clearing and existing polling cadence remain unchanged.
- Account and report initial reads now use accessible static skeletons without invented balances. Initial report failures have a visible recovery message; a failed request cannot leave an unexplained skeleton indefinitely.

The local worker bridge failed twice because its required executable was unavailable. The bounded report-reader patch used the permitted direct fallback. The Codex lead inspected the actual resulting diff, requested a correction for visible initial error handling and real effect coverage, and reviewed the final changes independently.

## Verification

- All 97 frontend tests passed, including real controlled state/effect tests for failed-poll recovery, cancelled old-date responses and authorization clearing. Static tests confirm that a pending or failed new date cannot display the old date's conclusion.
- TypeScript checking and the production web image build passed. The UI detector reported no findings; diff whitespace checking passed.
- In the labelled synthetic source-component preview, the Pixel frame remained at 328.59px for missing, completed and working activity. The selected report conclusion remained at approximately 588.08px for ready, pending and failed reads. These are bounded geometry measurements, not a claim of a site-wide zero CLS score.
- Responsive source-component checks at 1440px, 768px and 390px found no horizontal overflow. The reader retained its independent scrolling styles. The real HTTPS production page was also checked at its available 1265px viewport; the new stable header, indicator slot and bundled Pixel artwork loaded. The viewport override applied to the temporary preview but not that production tab, so the production screenshots are not evidence of three distinct viewport sizes.
- The owner-web service was rebuilt and replaced without restarting the owner, company or ledger services. Existing account data, viewing sessions and authorization configuration were preserved. The verification browser had no owner session; it was not paired for this UI review.

Temporary preview screenshots and logs live under ignored `work/stable-ui-review` and `work/stable-ui-*.log`. The screenshots use explicitly labelled synthetic account and meeting data. They do not prove financial execution.

## Remaining Live work

This change does not activate trading or change risk checks, account reconciliation, limits, execution symbols, broker credentials, AI prompts or stored reports. Financial execution remains separately gated. Missing SOL history and unverified trading proposals shown in a saved meeting are research/execution issues, not solved by skeletons or client navigation.

## 2026-10-08: remove duplicate date tabs

Removed the owner's unwanted "วันที่มีรายงาน" disclosure and date-button row, including unused CSS. The native labelled calendar, selected date, range, disabled state, focus ring and per-day round selector remain. The existing date index and history-fetch behavior are unchanged. The local coding bridge was unavailable; its direct fallback was independently reviewed by the lead.

The 14 focused report/history tests, TypeScript check and production frontend image build passed. The deployed owner-web was inspected at 1440, 768 and 390 CSS pixels: zero date-button rows, 44-pixel calendar controls and no horizontal overflow. Evidence is saved in ignored `work/date-picker-1440.png`, `work/date-picker-768.png`, `work/date-picker-390.png` and `work/date-picker-proof.png`. Only the frontend container was replaced. Financial services, permissions, owner limits and stored reports were not changed; this UI cleanup does not resolve the existing SOL history gap or demonstrate a trade.
