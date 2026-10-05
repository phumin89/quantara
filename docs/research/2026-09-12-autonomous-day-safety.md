# Autonomous company: restart safety

Implemented a bounded prerequisite for unattended paper operation, not a live-trading launch.

## Changed

- Default app/Docker configuration disables the legacy deterministic trading fallback.
  Hosted AI remains disabled unless explicitly configured. No key, paid request, or existing
  portfolio/CFO account was changed during this work.
- The AI cycle coordinator durably claims a Bangkok business date BEFORE research,
  model calls, or paper-order effects. Completed/failed attempts cannot repeat that date,
  including across API restarts and concurrent instances sharing the journal.
- The journal is beside the configured decision ledger: `<DecisionStatePath>.meetings.json`,
  with a permanent `.lock` marker, checksum, exclusive write lease and atomic replacement.
  Missing previously initialized or corrupt state fails closed, never silently resets.
- Unhandled exceptions/cancellation leave STARTED. Later dates cannot proceed while an
  unresolved attempt exists. Reconcile artifacts and paper account manually before recovery;
  there is deliberately no automatic replay/reset endpoint.
- Today's saved attempt restores into existing company status fields. STARTED restores a
  safety halt. A provider/data failure consumes that day's attempt instead of paid retries.
- Research ends on the previous UTC date to avoid requesting today's unfinished daily bar.
  This does not replace exchange-calendar, quote-freshness or source-quality checks.

## Evidence and limitations

Integration tests recreate the API against the same journal with controlled bars and a test
meeting provider. They prove non-repetition and account preservation for the exercised
no-order scenario, not investment performance. Journal tests cover concurrent claims,
corruption, missing state, interruptions, clock rollback and owner-only completion.

This is at-most-once ATTEMPT protection, not exactly-once order recovery. A failure during
execution can leave partial paper fills and requires reconciliation. Keep journal, decision
ledger and portfolio together in persistent storage and backups. This does not protect
against deletion of the entire state directory, hostile edits or storage loss.

Still required: connect the separate CFO-funded portfolio, automated timestamped news and
fundamental collection, aggregate AI spending limits, durable per-role scheduling,
market-calendar/freshness validation and end-to-end forward-paper acceptance. Agenda text
is not proof of independently scheduled agent work. Market-context shadow reports remain
opt-in and cannot execute orders. No bank connection or live broker path is introduced.
