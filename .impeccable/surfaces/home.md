---
version: 1
slug: "home"
primary_target: "route:/"
related_targets: ["apps/web/features/home"]
---

# Home research office

## Scope and visitor mode

**Mode: Operate.** The owner inspects recorded Team work, selects a role and opens
its evidence in Daily Brief. Research remains read-only.

This contract records the current source implementation. Build and browser
verification for this revision are pending; earlier fixture screenshots show the
superseded custom office.

## Direction and composition

The Quantara shell retains [DESIGN.md](../../DESIGN.md). A full-width iframe of
the original pinned Pixel Agents App is Home's primary view, preserving its
default layout, native office UI, assets and pixel font inside the neutral shell.
The reading order is Home and Refresh status, current connection and research
state, office, role controls, evidence and credits.

Home uses the available width up to 1440px. The iframe height is 480px on phones,
600px from `sm` and 680px from `lg`. Role controls sit below the office in two
columns on phones and four from `sm`.

## Selection and recorded state

CEO, Analyst, Reviewer, Intraday, Swing, News, Positions and Quant retain stable
role IDs. Native character/card selection and parent role buttons select the
same evidence; parent selection focuses the native camera. Role buttons expose
their pressed state and control the labelled evidence region. Status text remains
visible alongside semantic color.

Working and Completed use green; Queued uses blue; Failed uses red; Blocked,
Stale and Unknown use amber; Idle and Cancelled remain neutral. Active work
requires a fresh running step in the matching current running run. No timer or
next-role hint creates activity. Native motion pauses for reduced motion, hidden
or offscreen content, explicit pause, or no active role. Office failure leaves
the role controls and evidence usable.

Evidence is plain text capped at 700 characters with normalized whitespace,
recorded Bangkok time and an available date/run link to Daily Brief. Keep
loading, missing evidence, stale state and critical errors explicit.

## Shared workspace behavior

Home consumes the root Team activity provider's current-day read pair every ten
seconds, with one pair in flight and an eight-second timeout. Daily Brief and the
persistent Team chat widget use the same current state. Historical report dates
are read separately as needed. Retain stale records after failure; clear prior
evidence when account scope changes or the current run is empty.

Team chat is a modal right panel on desktop and full screen below `md`. It stays
mounted across routes and preserves conversation position. Dashboard is a
separate summary route; it does not displace the office on Home.

## Sources

- [Home components and adapter](../../apps/web/features/home)
- [Shared activity provider](../../apps/web/features/team/components/team-activity-provider)
- [Integration, provenance and verification scope](../../docs/home-office.md)
