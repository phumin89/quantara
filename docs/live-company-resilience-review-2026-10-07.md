# Live company resilience review — 2026-10-07

## Problem and result

The company could stop producing research after execution was disarmed. One rejected historical market feed also aborted the entire meeting preparation. A source error therefore looked like an empty office, with no explanation or retry time.

The coordinator now separates research from execution authority. Fresh, reconciled account data can support an advisory meeting while trading is paused. Failed evidence is reported explicitly, and recoverable preparation failures retry after five minutes without claiming a meeting slot or calling the AI with unusable evidence.

The first deployed advisory round saved five real role reports but reached the existing token budget before the portfolio conclusion. Its reported input usage increased from 26,788 tokens for CEO to 68,529 for quant research. The default JSON encoder expanded Thai report text into Unicode escape sequences; recent reports grew from roughly 3,200–4,300 characters to 13,800–19,300 when escaped. Prompt serialization now keeps Thai literal with `JavaScriptEncoder.Create(UnicodeRanges.All)`. HTML-sensitive characters, quotes, and controls remain escaped, parsed input is unchanged, and token/model-call budgets remain unchanged. These character measurements are not a claim of equivalent billed-token savings.

## Evidence policy and execution boundary

- Reject contradictory or malformed history rather than guessing which candle is correct. During investigation, the public SOL feed returned two different candles for the same completed day. The original failure did not retain its exception, so this is a reproduced failure mechanism, not a recovered original stack trace.
- Exclude a failed symbol from the supplied candidates and permitted proposals. Valid BTC/ETH evidence can still support research. If the excluded symbol is held, the entire meeting is advisory-only; if it is unheld, independently authorized valid symbols remain eligible for the existing executor checks.
- Public research provider failure or pending/reserved funds makes a continuous review advisory-only. If all native historical sources fail, do not call the model or consume the review slot; report the source issues and retry.
- A round that starts without execution authority cannot gain it during slow source retrieval or model work. An advisory round never submits intents. A later owner activation applies to a later round.
- Refresh the account and quotes after evidence preparation and again before dispatch. Preserve account binding, owner mandate/version, unchanged owned funds, bounded prices, sizing, turnover, and deterministic executor risk validation.
- Do not automatically rearm trading. The patch performs no owner activation, order placement, cancellation, or broker transaction.

## Scheduling, persistence, and recovery

The existing 30-second worker and 15-second heartbeat remain. Ordinary account/journal/source preparation failures use a five-minute backoff. Healthy watching continues checking every 30 seconds; it does not inherit the error backoff. Completed or already attempted hourly slots retain their existing once-per-slot protection.

Unexpected worker or telemetry exceptions no longer terminate the background loop. Actual shutdown cancellation still stops it. Completed reports replay to the owner store idempotently after restart; financial requests are never replayed. An unresolved `STARTED` journal claim remains a reconciliation blocker rather than authorizing a retry of a potentially submitted financial request.

Typed, bounded issue codes and the next retry/check timestamp travel in the activity projection. Saved meeting summaries retain the evidence issues and advisory-only flag. These optional fields are omitted from old summaries when absent/default, preserving legacy journal checksums. Failure during preparation appears in activity without fabricating an AI report.

The existing coordinator authorization can also read back its own bounded display projection for operational diagnosis. This returns no prompts, credentials, raw account audit, or financial controls. Foreign accounts, invalid tokens, and browser-origin calls remain denied by the existing scope and authorization policies; endpoint tests cover these cases and unchanged account/broker state.

## Review and validation

Codex independently inspected the coordinator, authority checks, worker loop, persistence/metadata boundaries, frontend changes, and actual tests. Local workers supplied bounded host and frontend patches. Review fixed a shutdown compile error, kept ordinary noncontinuous sizing behavior, separated healthy polling from error backoff, removed duplicate advisory outcome copy, and strengthened the legacy checksum test with an independently serialized old summary shape.

- Backend after the encoding follow-up: 963 passed, 0 failed; one existing opt-in PostgreSQL test skipped. The full suite ran in an isolated Linux Docker source snapshot with no production credentials, runtime configuration, or account state mounted.
- Native Windows test discovery was blocked by Application Control and ran no tests. This was not counted as success; no Windows security setting was disabled.
- Frontend after the final activity correction: 90 passed, 0 failed; TypeScript check passed.
- Regression coverage includes source quarantine with execution both paused and armed, held-symbol gaps, all-source recovery without consuming a slot, failed account-read recovery, authority changes during model work, rejection of proposals for excluded symbols, legacy journal loading, finite metadata validation, and survival of a real background worker after a thrown tick.
- The encoding regression checks literal Thai input and prior reports, exact JSON round-trip values, continued HTML/quote/control escaping, and normal meeting completion. Lead review added the round-trip proof; a missing test namespace was corrected before the successful full run.
- Responsive report review at 1440/768/390 px used actual source components with clearly identified synthetic reports. No horizontal document overflow was observed; date/select/disclosure controls were 44 px high. The advisory outcome appeared once, followed by the source issue and conclusion. The phone disclosure was operable.
- Runtime inspection also exposed previous coordinator issues/retry time remaining in telemetry during a later working role. The office hides those previous lines while a real role works, then uses the latest coordinator result afterward. The regression checks both states; it does not invent activity.

## Deployment and runtime verification

Owner, frontend, and final company images built successfully. The existing execution gate was checked as disarmed before deployment. Updated services preserve the existing healthy ledger, owner-session volume, company journal, account configuration, and source/artifact caches. The company deployment was corrected to mount the existing Codex sign-in file before final runtime verification; the sign-in contents were not printed.

The first deployed round stored five reports under the 12:00 slot with `TOKEN_BUDGET`, `AdvisoryOnly=true`, and both the SOL source issue and a meeting-failure issue. These reports remained in the journal and PostgreSQL audit.

After the encoding update, a preparation check reported `ACCOUNT_STATE_INVALID` and the visible five-minute retry time. The worker resumed the 13:00 review without a manual meeting trigger. Portfolio-initial completed at 13:10:26 Bangkok, risk-critic at 13:11:06, and portfolio-final at 13:11:50. The journal and PostgreSQL both contain the completed 13:00 review as `READ_ONLY`, with three reports, `AdvisoryOnly=true`, and only the SOL history issue. Together, the two new rounds saved all eight distinct team roles today. The partial round is retained rather than rewritten as completed.

The final three calls reported 18,066 / 18,907 / 20,449 input tokens, plus 1,074 / 1,644 / 1,879 output tokens, within the unchanged meeting budget. This is runtime evidence for this review, not proof of the cost or completion of every future full daily meeting.

The fresh activity heartbeat then showed `ALREADY_ATTEMPTED`, the SOL source issue, and the next review at 14:00 Bangkok. The account remained `Armed=false`, `Stopped=false`, `Reconciled=true`, with zero executor orders. No activation or broker order was performed during verification. The HTTPS frontend opened normally; its fresh browser session correctly required owner verification. Saved real reports were also rendered through the current report component for visual inspection, without establishing an owner session or submitting controls.

The saved final report was checked again at 1440/768/390 px. All three document widths matched their scroll widths, including the real Thai conclusion. A screenshot of the saved report component is retained locally under `work/resilience-saved-report-panel.jpg`; it is a rendering of saved production reports, not an authenticated browser screenshot or a new meeting trigger.

## Remaining limitations

Trading remains governed by the existing owner activation and reconciliation requirements. A restored research meeting is not evidence of a live trade or profitability. Contradictory SOL history remains excluded until a valid feed is available. A persisted ambiguous meeting claim, corrupt journal, invalid account binding, or unresolved broker outcome still requires evidence-based reconciliation; this patch does not erase those records or silently restore execution authority.
