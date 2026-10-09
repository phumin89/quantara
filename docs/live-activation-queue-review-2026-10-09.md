# Activation queue repair — 2026-10-09

The owner ran the funded activation command. It verified 247 native THB markets,
but failed with a generic account/storage/broker error before arming. The prior
sell settlement repair remained valid: the order was filled and there was no
receipt conflict. No order was placed or cancelled during this repair.

## Reproduction

- Authenticated private receipt stream became ready in under one second.
- A read-only account lease request immediately after executor startup failed at
  **30.375 seconds**, with `NpgsqlException` wrapping `TimeoutException`.
- Background reconciliation owns that lease while reading the full catalog twice.
  The database command's default 30-second timeout was shorter than a healthy cycle.
- With the patch, the same read-only lease acquired successfully after
  **31.498 seconds**. The snapshot version had changed while waiting, confirming
  why approval captured before queueing could also become stale.
- In the same lease, read-only full broker evidence verified twice, and detached
  daily-frame recovery followed by actual broker activation evidence returned
  **Allowed=true, no violations**, in **74.645 seconds**. This diagnostic did not
  commit the frame, arm execution or invoke an order endpoint.

## Change and safety review

- Only lease acquisition replaces the driver's 30-second timeout with a linked,
  bounded **60-second** wait. Caller cancellation remains effective; one writer
  still owns the account, lost sessions cannot reconnect into ownership, and
  ordinary query/commit timeouts are unchanged.
- Explicit local owner commands capture the current snapshot after ownership is
  acquired. Their approved mandate and account binding remain pinned, every
  broker/risk check still runs, and Stop cannot be cleared. Browser approval APIs
  retain their strict original snapshot-version checks. No new HTTP/model route
  exposes these owner-only command methods.
- An already settled old-day account goes directly to the owner's daily rollover,
  which performs complete two-read reconciliation itself. It avoids an identical
  preceding scan and preserves high-water equity and existing capital/limits.
- CLI failures identify the phase and a fixed error code, never raw exceptions,
  connection strings, authenticated URLs, response bodies or credentials.
- Windows PowerShell renders native stderr using its message, retaining the real
  exit code. Verified against actual nested native processes with exit codes 0/3;
  no activation script was executed by that check.

## Validation

- Actual source/test/launcher diff independently reviewed; whitespace check passed.
- Headless trading suite: **1,049 passed**, zero failures/skips.
- Disposable PostgreSQL integration suite: **159 passed**, zero failures/skips.
- Relevant regressions: a lease held beyond 30 seconds, queued ownership observing
  owner Stop, cancellation without entering the callback or leaking ownership,
  strict browser approval versus fresh local-owner approval, changed mandate,
  changed binding, Stop preservation, daily frame freshness, preserved high-water
  equity, and diagnostics refusing to echo private exception text.
- Existing software verification in the launchers remains intact. No reduction or
  restructuring of the test suite, no paid provider, no new financial authority.

Owner activation remains off after the earlier failure. Financial activation is
still the owner's existing explicit command, using the repaired runtime.

## Runtime verification

The rebuilt owner image was deployed without starting the company service. Its
running image ID matches the new build; the web application returned HTTP 200.
Read-only persisted-state verification after deployment confirmed execution
disarmed, Stop unchanged, zero reserved cash, no outstanding orders, unchanged
mandate version 5 and unchanged funding limits. Cash remains 4,999.12 THB; the
old daily frame remains intact until the owner's explicit activation command.

Reference: [Npgsql command timeout documentation](https://www.npgsql.org/doc/api/Npgsql.NpgsqlCommand.html#Npgsql_NpgsqlCommand_CommandTimeout)
documents the default 30-second wait; cancellation and ownership are retained
through the application's bounded lease acquisition.
