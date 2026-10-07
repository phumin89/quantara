# Missing team discussion — 2026-10-07

## Verified runtime findings

All four Live services were running. Read-only checks of the company's journal and the owner's durable audit found a daily attempt on 2026-10-07 with a review snapshot at 10:13:33 Bangkok time. It finished as `RESEARCH_UNAVAILABLE`, with no saved AI team reports. The owner stored that failure at 10:13:37; the report did not disappear between the journal and database.

Re-fetching the same public Bitkub daily-history range (2025-09-02 through 2026-10-06) returned 400 chronological daily rows for BTC and ETH, but 401 rows for SOL. SOL had two rows with timestamp 1778284800 (2026-05-09 UTC), with different closes. The provider rejects non-increasing dates, and the coordinator catches market-evidence failures before calling the meeting runner. This reproduces a credible cause of the saved failure; the original exception is not retained, so its exact text cannot be confirmed. No conflicting row was discarded or guessed to be correct.

The owner audit also recorded `BROKER_EVIDENCE_UNAVAILABLE` at 10:21:04 Bangkok time. That transition disarmed execution. Subsequent reconciliation succeeded and the account snapshot was fresh, but `Gate.Armed` remained false. `ValidPacket` currently blocks the meeting/research pipeline while execution is disarmed. The internal cause of the failed broker read is not available from the persisted reason code.

## UI correction

The chat's default day was latched to its first fetched date, so an open room could keep showing yesterday despite new saved dates arriving. The default now follows the latest feed. Explicit historical-date choices remain stable, and known-empty-day request skipping and stale-response cancellation remain unchanged.

The lead inspected the actual worker patch and expanded the controlled polling regression to cover a newly saved day. All 87 frontend tests and TypeScript validation passed. A bounded fixture review at 1440, 768 and 390 CSS pixels found no horizontal overflow. No layout, broker permissions, execution state or financial validation changed. The frontend image was built and updated independently; owner, company and ledger were not restarted.

## Remaining blockers

- The conflicting SOL history still prevents the current all-symbol evidence pass. A safe data-source remedy must preserve completed native bars and reject conflicting financial evidence.
- Auto Trade is disarmed after the broker-read failure; it was not reactivated by this investigation or frontend update.
- Today's stored record is a failed preparation attempt, not a completed meeting. The absence of team discussion is real and must not be presented as an idle animation problem.

Public API reference: [Bitkub REST documentation](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md). Diagnostic public responses and test/build logs remain under ignored `work/`; credentials and raw account records were not added to Git.
