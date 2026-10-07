# Live office roster review — 2026-10-07

The owner saw one character after a public market scan. The latest fresh
activity snapshot contained only `market-research`; the viewer treated that
snapshot as the staff roster and removed the other seven configured roles.
The day's journal contains five initial reports and three portfolio review
reports, covering all eight roles. Presence and current work were conflated.

## Accepted change

- A valid, fresh, account-bound activity snapshot displays all eight configured
  roles at stable seats. Unreported roles are inactive. Only a reported
  `working` role receives typing animation; model calls remain sequential.
- Missing, malformed, foreign-origin or expired activity clears the office.
  Empty heartbeats now receive the same 45-second expiry timer as role events.
- The compact label distinguishes the configured team from a currently working
  role. Idle READ_ONLY status says the system is watching with orders paused.
- Prefer separate sofas before adjacent cells on a vertical sofa. The first
  screenshot exposed overlapping sprites; the corrected layout shows eight
  distinct characters while retaining chair and bench priority.

## Independent review and verification

Codex inspected the actual patch after the bounded local worker round. Review
removed fabricated fallback timestamps, caught the empty-heartbeat expiry
regression, corrected sofa placement, and simplified summary separators for
activity records without coordinator status.

- Frontend: 91 tests passed; TypeScript passed.
- Pixel activity: 12 deterministic tests passed, including sparse failed scout
  events, empty heartbeats, stable seats, work transitions and strict origin /
  freshness validation. Pixel TypeScript and focused ESLint passed.
- Impeccable detector: no findings in the changed viewer files.
- Production frontend image built successfully.
- Bounded visual checks at 1440×1000, 834×1112 and 390×844: eight characters,
  no sprite overlap and no horizontal page overflow. A working-role fixture
  shows one active role. A one-shot empty heartbeat clears characters after
  expiry. Existing reduced-motion handling remains unchanged.

Visual checks used the production compiled Pixel bundle in an isolated local
preview with explicit synthetic-data labeling. Only the copied preview bundle's
trusted parent origin changed to the preview port; production origin validation
remains HTTPS localhost. These images prove display behavior, not broker activity
or simultaneous model work. The fixture's static server-rendered header is not
a production polling or authentication test.

Evidence is in ignored `work/office-roster-*.log`, the detector JSON and desktop,
tablet, phone, working-role and expiry screenshots.

## Deployment and remaining limitations

Recreated only `owner-web`. Owner execution host, company research host and
ledger start times are unchanged; the ledger remains healthy. HTTPS app and
Pixel entry point return 200 with the new bundle `live-Nkaa4G1j.js`.

No broker credential, pairing state, mandate, activation, order or ledger entry
changed. Trading remains paused/read-only; configured character presence does
not prove trading readiness. The public scanner still reports a feed error and
SOL history remains unavailable. Those backend research issues are separate
from this accepted display fix.
