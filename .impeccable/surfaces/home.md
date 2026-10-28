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

This contract records the current source implementation. Use the latest local
workspace screenshots and verification report for its acceptance state; earlier
Library screenshots show the superseded custom office.

## Direction and composition

The Quantara shell retains [DESIGN.md](../../DESIGN.md). A full-width iframe of
the original pinned Pixel Agents App is Home's primary view, preserving its
default layout, native office UI, assets and pixel font inside the owner's
playful glass shell. The reading order is Home with the research archive link and
Refresh status, current connection and research state, office with role controls
and credits.

Home uses the available width up to 1440px. The iframe height is 480px on phones,
600px from `sm` and 680px from `lg`. Role controls occupy a 280px vertical box
to the right from `lg`; smaller layouts stack the two-column role box below.

## Selection and recorded state

CEO, Analyst, Reviewer, Intraday, Swing, News, Positions and Quant retain stable
role IDs. Native character/card selection and parent role buttons select the
same role; parent selection focuses the native camera. Role buttons expose
their pressed state. Status text remains
visible alongside semantic color.

Working and Completed use green; Queued uses blue; Failed uses red; Blocked,
Stale and Unknown use amber; Idle and Cancelled remain neutral. Active work
requires a fresh running step in the matching current running run. No timer or
next-role hint creates activity, and idle native characters may continue their
office wandering without claiming that a role is working. Native motion pauses
for reduced motion, hidden or offscreen content, or explicit pause. Office
failure leaves the role controls and archive link usable. Full
recorded role evidence lives in Daily Brief; no duplicated evidence passage
sits below the Home office.

## Shared workspace behavior

Home consumes the root Team activity provider's current-day read pair every ten
seconds, with one pair in flight and an eight-second timeout. Daily Brief and the
persistent Team chat widget use the same current state. Historical report dates
are read separately as needed. Retain stale records after failure; clear prior
evidence when account scope changes or the current run is empty.

Team chat sits below the 64px header. From `lg` (1024px), it starts expanded
as a non-modal panel and reserves 380px beside the centered workspace.
The transcript stays mounted across routes. Smaller screens start collapsed
and open a right modal drawer with backdrop and focus trap, bounded by the
visual viewport and safe areas. A labelled header control opens the chat;
Close sits inside its header. Both retain 44px targets. Closed chat leaves no
blank rail. Conversation position is preserved without adding browser history
entries. Follow [DESIGN.md](../../DESIGN.md) for the shared Dusk surfaces and
typography. Dashboard is a separate financial KPI/chart route; it does not
displace the office on Home.

## Sources

- [Home components and adapter](../../apps/web/features/home)
- [Shared activity provider](../../apps/web/features/team/components/team-activity-provider)
- [Integration, provenance and verification scope](../../docs/home-office.md)
