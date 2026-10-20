---
version: 1
slug: "home"
primary_target: "route:/"
related_targets: ["apps/web/features/home"]
---

# Home research office

## Scope and visitor mode

**Mode: Operate.** The owner checks recorded Team activity, selects a role, and
opens its report in Daily Brief. Home is a read-only research view. Research
activity does not grant trading authority.

This is the built-state contract for `/`, derived from the Home components and
the desktop, tablet and mobile fixture screenshots. The screenshots contain
synthetic evidence.

## Direction and composition

Inherit the neutral white and gray surfaces, system sans fallback, outlined
panels, restrained corners and shadow-free composition in
[DESIGN.md](../../DESIGN.md). The focal element is the pinned Pixel Agents office
inside the shared Quantara navigation shell.

The reading order is title and actions, connection and research operation,
office and Team roster, selected role evidence, then renderer credits. Home is
left aligned within the existing workspace width. At the MUI `lg` breakpoint,
the office and a 280px roster share one outlined panel. Tablet and phone layouts
stack the roster below the office; header actions wrap. The canvas fits the
available width with a 3:2 aspect ratio and a 440px height cap.

## Signature interaction and role states

Eight stable seats form two rows of four: CEO, Analyst, Reviewer and Intraday;
then Swing, News, Positions and Quant. Selecting a character or its roster button
selects the same evidence. The selected roster row has a neutral tonal fill.
Roster buttons expose their pressed state and control the labelled evidence
region, providing keyboard access independently of the canvas.

Status labels carry the meaning alongside semantic color:

| Label | Meaning and treatment |
| --- | --- |
| Working | Green; a fresh running step in the matching current running run. |
| Completed | Green; a recorded completed step. |
| Queued | Blue; recorded queued work. |
| Failed | Red; a recorded failure. |
| Blocked | Amber; configuration or stopped research prevents activity. |
| Stale | Amber; retained work without a fresh successful read. |
| Unknown | Amber; missing verified state or a running step without its matching current running run. |
| Idle | Neutral; no recorded work for the role. Idle is not a completed report. |
| Cancelled | Neutral; a recorded cancelled step. |

Only Working seats animate. Timers and next-role hints do not synthesize activity.
Animation pauses for reduced motion, an offscreen canvas, a hidden document,
explicit Pause animation, or no active role. Resume animation changes the visual
pause state only. Renderer failure leaves the roster and evidence available.

## Evidence and freshness

Selection shows bounded plain text, up to 700 characters, with wrapping and
normalized whitespace. Recorded times use Asia/Bangkok and the visible ICT
suffix. Evidence with a date and run ID links to
`/daily-brief?date=<Bangkok date>&view=team-chat#run-<id>`.

Home reads `GET /api/team` and `GET /api/team/brief` every ten seconds, with an
eight-second timeout and one request pair in flight. Refresh status uses the same
read path. A successful read is fresh for thirty seconds. Loading, connection
failure, retained stale records and unavailable state remain visible. Older or
conflicting revisions cannot overwrite retained work for the same account;
account changes and an empty current run clear prior evidence.

Polling pauses while the document is hidden. In-flight reads abort on hiding or
unmount, and canvas frames, observers and listeners are cleaned up on unmount.

## Sources

- [Home components and adapter](../../apps/web/features/home)
- [Data lifecycle and pinned renderer provenance](../../docs/home-office.md)
- Synthetic screenshot evidence is retained in the task's Library uploads; local QA uses `.impeccable/review/home-{desktop,tablet,mobile}.png`.
