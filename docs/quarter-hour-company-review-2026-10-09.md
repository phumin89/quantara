# Quarter-hour company review, 2026-10-09

The owner selected a 15-minute portfolio review cadence. This changes when the company can reconsider its plan; the independent price-plan monitor still checks every 10 seconds. A review does not force an order or repeat model work when the evidence is unchanged.

## Changes and preservation

- Live company requests 15-minute Bangkok slots. Each saved review is identified by business date, hour and minute (00, 15, 30 or 45). Daily research remains separate. Regular reviews retain the existing three-role flow; changed research adds the existing specialists.
- Missing `ReviewMinute` means zero and zero is omitted from serialized records. Existing JSON journal checksums, report identities and historical text remain readable without rewriting them. Duplicate slots, interrupted meetings, account mismatches and clock rollback remain rejected.
- The executor advertises its minimum supported interval. An older executor omits that field, so a new company safely retains hourly identities until the owner reloads the executor. A capability change during a meeting invalidates that round.
- The report picker shows HH:mm and accepts one daily meeting plus up to 96 review slots. Legacy reports remain `:00`; the date picker and saved conclusions remain intact.
- Saved-report publication shares the account lease with complete broker scans. The former 10-second transport deadline failed before publication completed, keeping the company in `LIVE_UNAVAILABLE / MEETING_FAILED`. Reports now allow 75 seconds; financial requests and activity still have independent 10-second deadlines. Startup publishes at most one saved report per tick before continuing the current meeting, newest first, so 22 saved reports cannot monopolize startup or delay showing today's conclusion. Failed report replay respects its five-minute backoff. No financial request is replayed.

## Validation and runtime handoff

The actual source and worker diffs were reviewed by the lead. The UI second opinion found no verified defect. Local reviewer bridging was unavailable because its configured executable was missing; the advisory review used a direct read-only fallback.

Final checks passed: **1,064 headless**, **162 disposable PostgreSQL** and **17 focused report UI** checks, all without failures or skips. TypeScript type checking passed. Coverage includes quarter-slot identity, restart, legacy checksums, duplicate/conflicting reports, bounded historical publication, worker backoff, caller cancellation and the separate financial timeout. PostgreSQL verification includes two distinct quarters in the same hour across a fresh store restart and conflicting replay.

After prioritizing the newest saved report, all **91 coordinator checks** passed; the regression proves today's newly completed report is published on the next tick ahead of the remaining backlog. The actual one-line scheduling diff was reviewed and the company image rebuilt.

Bounded desktop (1440×900), tablet (768×1024) and phone (390×844) checks showed readable report controls and legacy HH:mm labels. The phone DOM had no horizontal overflow. Runtime readiness intermittently reported a stale account snapshot during broker scans; that is separate from report rendering and is not a profitability or execution proof.

All three runtime images were built. Company and frontend were updated without restarting the active owner executor. A read-only ledger check after deployment showed Armed=true, Stop=false, Reconciled=true, cash 4,999.12 THB, reserved cash 0, mandate version 5 and one existing order. Authorized capital and per-order ceiling remained 5,000.0872 THB. No order was placed or cancelled by this implementation work.

The running owner binary still supports hourly report identities. After the current meeting finishes, the owner can run `Enable Auto Trading.cmd` to load the built executor through the existing reconciliation and activation path; only then can the production company use quarter-hour identities. Ordinary app startup does not change saved financial authority. At 17:51 Bangkok the 2026-10-09 daily meeting was STARTED: CEO and chief-of-staff were completed, and market-research was working. This proves the company advanced beyond historical replay; it does not prove a completed quarter-hour round, a new trade or profitable performance.

The actual daily round subsequently ended with `TOKEN_BUDGET`: six model calls reported 246,291 input tokens and 10,695 output tokens (256,986 total); five role artifacts were saved. No final portfolio decision or new order was recorded. The bounded-publication image was deployed only after the meeting ended. The token-budget failure remains a separate blocker for completing the full daily meeting; its ceiling was not raised, and the failed meeting was not rerun. Missing history was also recorded for XRP-THB, DOGE-THB and SOL-THB. Passing synthetic checks does not remove these real runtime limitations.

## Rollback

Keep the current journal reader when reverting scheduling or UI changes. Once nonzero review minutes are written, the old reader cannot verify their checksum or distinguish same-hour slots. Preserve the journal and audit data; do not delete reports or reset financial state to obtain a rollback. The default hourly fallback covers an older executor only before quarter-hour reports are accepted.

## Follow-up questions

The app currently registers persistent entry/quantity/target/stop/deadline plans and submits an entry when its monitored condition is met. Parking entry and target limit orders immediately is a separate behavior change, requiring reservation, partial-fill, expiry and cancellation handling; a sell limit is not a stop-loss order. No resting-order behavior was added here.

Meeting roles already have distinct runtime instructions and saved role learning memory. Per-role `AGENTS.md` and `SKILL.md` could make those policies easier to maintain, but must actually be loaded, versioned and validated by the meeting runtime. Merely adding files does not change agent behavior, and role files cannot expand RiskEngine or owner authority. No role-file loader was added here.
