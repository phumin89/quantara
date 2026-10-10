# Daily learning review, 2026-10-10

## Saved evidence

Today's learning completed at **16:03:32 Bangkok**, with one model call. Saved projection `ff8177d6-91a6-457d-978e-ad983d5a48d1`, draft `d26cde8e-ee4f-444c-95e7-8371c81fa8ab`, and attempt marker `9544feae-a325-4915-bef0-c8ae7a74779f` all have cutoff 2026-10-10. The draft records 16,049 input and 1,299 output tokens; the projection does not double-count them. All eight canonical roles are ACCEPTED and account-bound. Independent checks found exactly one eligible saved source report matching each role's artifact ID and contiguous quotation. ACCEPTED establishes citation validity, not improved trading performance.

Examples of recurring missing evidence in those historical reports:

- CEO source `4e36b9ef-3789-4c9c-8e58-5b604168fa24`: “ETH ผ่านทดสอบ แต่มีเพียง 3 รอบซื้อขาย”. Today's practice retains the three-round sample limitation and costs when comparing assets; it does not infer future profit from a pass label.
- Final portfolio source `386ee134-6521-481b-b8e8-5458fdc3b6b2`: “XRP/DOGE/SOL ขาดประวัติราคา; รอบนี้หยุดส่งคำสั่ง”. The saved round is advisory (`READ_ONLY`); this quotation alone does not establish that one unheld coin's missing history blocked every other coin.

The latest saved public shortlist, observed **2026-10-10 00:16:45 Bangkok**, is KUB, USDT, BTC, NEAR, ETH, XRP, DOGE and SOL. Every shortlisted coin has saved source IDs and a credibility finding. The missing-document queue is empty in this snapshot. NEAR and ETH retain explicit partial-extraction gaps. KUB uses saved V5.2 content ID `doc-54eaf1dcb0d514cdb68d496d9115db6437966df11502b68a27acdeb4474a5451`. No unchanged documents were sent through a model again and no speculative source coverage was added. This is the latest saved shortlist, not a claimed current scan.

## Reproducible learning-input correction

`CompanyLearningService.Reports` sorted completed history by date and hour but ignored the review minute. With completed 09:00, 09:15 and 09:45 reports in append order, all eight roles and the shared risk critic could learn from 09:00 instead of the newest completed 09:45. This was an untested practice exposed by the quarter-hour report format added on 2026-10-09; the inspected runtime history still uses hourly identities, so no historical quarter-hour misselection is claimed.

The correction adds a descending `ReviewMinute` tie-breaker. The original account, age, completed-status, exact-citation, canonical-role and once-per-day reuse checks remain intact. Existing cached learning is preserved; today's successful job was not rerun.

The lead inspected the worker's actual patch and rejected an initial fixture that created a STARTED record before subsequent completed records, which would test the journal's interruption guard rather than ordering. The corrected regression records legacy 09:00, 09:15 and 09:45 completions, then a STARTED 10:00 attempt. It checks all eight learned artifact IDs and exact quotations, canonical role order and the shared critic against 09:45 while excluding the unfinished attempt.

Before the production correction, the focused regression executed and failed on the older artifact ID. Afterward, **14 focused checks passed, with zero failures or skips**, including all company-learning checks and the existing next-meeting role-memory/cache-key check. The latter verifies each role receives its own next practice, revised practices miss the old decision cache and another account's learning is rejected. This is a before/after proof of evidence selection, not a claim that agents became smarter or profitable.

## Runtime limit

The default Windows Docker endpoint was temporarily unavailable; the existing engine was subsequently read through its observed `docker_engine_linux` endpoint without changing the user's Docker context or starting/restarting services. Host Application Control initially blocked a test assembly; a later normal test invocation executed successfully. No security policy was changed.

Trusted read-only activity at **16:09:38 Bangkok** reported `LIVE_UNAVAILABLE / JOURNAL_UNAVAILABLE`, before a new meeting. The source journal contains 30 saved attempts, no STARTED record, and its integrity/reconciliation and all bounded saved-report validations passed independently. The latest owner report audit ends at **2026-10-09 23:00**; the company's saved **2026-10-10 00:00 READ_ONLY** report has not appeared there. This identifies a report-publication/runtime failure, not proven journal corruption. The exact transport/storage cause remains unresolved in this bounded learning review.

Today's learning is saved and its next-meeting injection path is tested, but no new production meeting has yet consumed it. The correction is not deployed during this heartbeat: no running financial service is restarted, no journal or account state is reset, and no activation, order submission/cancellation, permission/allowlist/limit change, secret inspection or paid provider is performed. The initial 30 THB purchase is not used as a performance result.
