# Meeting history date picker — 2026-10-07

## Change and acceptance

The previous control mixed meeting dates and rounds in one select. Its newest-31-round response also hid older saved dates. The team room now uses a native date input and a compact disclosure listing dates with saved reports. One round uses a plain label; multiple rounds use a selector for the chosen day only. Conclusions and team reasoning keep their existing hierarchy.

The existing owner-read endpoint accepts an optional canonical Bangkok business date and returns a complete saved-date index separately from the newest-round feed. Day requests return that day's saved rounds. Empty dates never display another day's report. Opening the calendar causes no request; choosing a date known to be empty causes no immediate day request. The existing 30-second background refresh can discover newly saved reports without resetting the selected day or reloading the page. In-flight replies from an abandoned selection are ignored.

No dependencies, database migration, broker calls, order authority or activation changes were added. Owner-read authentication and account scope validation remain in place. Optional response fields preserve compatibility during deployment with the previous owner service.

## Independent review and fixes

The lead reviewed the actual patch and corrected an effect reset loop, incorrect empty-day loading, date-input CSS targeting and the legacy test transpiler's Set spread behavior. A controlled state/effect regression test now covers stable selection, no duplicate initial fetch, no known-empty-day fetch, stale replies, polling and disposal. Parser and endpoint tests cover invalid dates, cross-day replies, retained old dates and all rounds on a busy day.

## Verification

- Backend suite: 950 passed, 0 failed, 1 existing opt-in PostgreSQL test skipped.
- Focused owner endpoint/access checks: 50 passed.
- Final frontend suite: 87 passed, 0 failed; TypeScript passed.
- Owner and frontend Docker images built successfully; the frontend was rebuilt after the final UI fixes.
- Bounded browser fixture review at 1440, 768 and 390 CSS pixels: no horizontal overflow; date controls are 44 pixels high; multiple-round selector appears only where needed. Known-date disclosure wraps and scrolls within the team room. Fixtures contain clearly identified synthetic reports, not production meetings.
- Production frontend updated independently with `up -d --no-deps owner-web`. Owner, company and ledger containers were not restarted. The HTTPS app route opens successfully.

## Deployment boundary

The updated frontend is running. The full-history owner endpoint is built and tested but has not replaced the running owner service. Until that update, the compatible frontend derives available dates from the previous newest-31-round response; older saved dates beyond that response become available after the owner update.

Restarting the owner invokes `LiveReconciliationWorker.StartAsync` and suspends execution for recovery before accepting traffic. Applying the owner image therefore pauses Auto Trade and requires the owner's normal activation flow afterward. This UI-only rollout did not activate execution, submit/cancel orders or inspect broker credentials.
