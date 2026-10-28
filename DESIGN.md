---
version: alpha
name: Quantara Workspace
description: Hallmark Dusk workspace with slate surfaces, periwinkle actions, apricot pending states and a playful pixel research office.
colors:
  primary: '#a0b0f4'
  on-primary: '#0f1223'
  canvas: '#0e1017'
  surface: '#171a23'
  text: '#f4f1ed'
  text-muted: '#e2e5ec'
  text-panel-muted: '#c2c8d3'
  hairline: '#2a2e36'
  success: '#a6d1bc'
  success-soft: '#1d2b27'
  warning: '#f4ba87'
  warning-soft: '#30251f'
  error: '#f2a6a9'
  error-soft: '#332126'
  info: '#a0b0f4'
  info-soft: '#22283c'
  closed: '#f4ba87'
  closed-soft: '#30251f'
  sell: '#f4ba87'
  sell-soft: '#30251f'
rounded:
  container: 16px
  control: 10px
  tag: 8px
---

# Quantara design guide

The owner's 28 October 2026 request selects Hallmark Dusk and supersedes the
mint-and-peach palette. Keep the playful pixel office, calm typography and modern
transparency. Preserve real data, routes, controls and financial authority.

## Dusk workspace

| Foundation    | Direction                                                                                                                          |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Typography    | Native system body with Thai fallback; self-hosted Nunito headings at weight 600. Pixel Agents typography stays inside the office. |
| Canvas        | A still sunset valley photograph with a dark reading scrim; Dusk slate fallback.                                                   |
| Surfaces      | Translucent smoked panels with a lit top edge and 22px backdrop blur. Raised forms and data rows remain more opaque.               |
| Controls      | Rounded 10px controls, 44px targets, clear focus, pressed and pending states. Periwinkle marks the primary action.                 |
| Identity      | Quantara wordmark and the existing pixel research office; no invented figures, mascots, testimonials or performance claims.        |
| Color meaning | Keep the existing success, warning, error, buy, sell and unknown meanings. Decorative accents never indicate trading authority.    |

Shared tokens are `--surface-panel`, `--surface-raised`, `--surface-soft`,
`--surface-header`, `--border-panel`, `--shadow-panel`, `--radius-panel`,
`--radius-control`, `--radius-tag`, `--color-mint`, `--color-peach`,
`--color-lilac`, `--font-body` and `--font-display`. MUI's semantic palette
remains the source for text, actions and financial states. The old mint, peach
and lilac names are compatibility aliases for dark accent surfaces. New design
work uses the semantic surface and Dusk paper/accent tokens. Apply backdrop blur
to the shared header and overlays, avoiding a separate expensive blur for every row.

## Geometry and layout

| Element                                       | Corner radius                |
| --------------------------------------------- | ---------------------------- |
| Tables, panels, dialogs and chat overlay      | 16px                         |
| Buttons, navigation items, filters and inputs | 10px                         |
| Status tags                                   | 8px                          |
| Icon-only controls                            | May retain a circular target |

- Use shared CSS radius tokens in MUI overrides; numeric sx radii multiply the theme radius.
- Center the workspace with content up to 1280px. Team chat floats above it and never changes main content width, position or page scroll. Main tables scroll within their focusable labelled region on smaller screens.
- Use `--page-gutter`: 16px on phones, 24px from 600px and 32px from 1024px. Use `--panel-padding`: 16px on phones and 24px from 600px. Separate page sections with `--section-gap` (24px). Compact rows and embedded transcript content keep their local reading rhythm.
- Main data rows approximately 60px; table headings 40px; cells padded 16px horizontally. Interactive targets stay at least 44px.
- Coin name and ticker share one line. Numeric headings and values align right with tabular numerals. Trade history expands beneath the relevant coin.
- Keep unrelated page sections separated; keep related table data compact.

## Color meaning

- Table headings use raised slate with soft white labels, including expanded history.
- All: neutral. Holding: sage. Closed: apricot, never interpreted as a loss. Filters and row tags share these meanings.
- Buy: periwinkle. Sell: apricot. Labels remain visible so color is never the only cue.
- Use installed MUI outlined icons alongside labels: grid for All, wallet for Holding, check-circle for Closed, inward arrow for Buy and outward arrow for Sell. Icons are decorative and inherit label color; preserve accessible text and selected-state cues.
- Verified positive P/L: green; negative P/L: red; zero: neutral. Unknown values remain unknown and have no invented gain/loss color.
- Incomplete coverage or unknown cost: amber. Connection and load failures: red.
- Tags use dark tinted surfaces and light semantic text. Periwinkle actions use dark ink. Glass has a lit edge; ordinary controls have no decorative drop shadow.

## Typography and interaction

- App copy is English, sentence case.
- Use system sans for body text, including Segoe UI on Windows for Thai. Self-hosted Nunito is reserved for headings. Keep labels and financial data in the body face.
- Use slate and smoked working surfaces. Active navigation uses the periwinkle action pair and an icon; tablet and phone navigation uses the menu below 1024px.
- Header navigation uses CSS breakpoints from the first server-rendered frame;
  loading JavaScript must not briefly show the desktop menu on a phone.
- Body copy uses 1.6 line height. Keep status tags muted, with light semantic text; use one consistent 44px control height.
- Page titles 26–36px depending on the screen, body 14–16px, supporting labels 12px.
- Preserve keyboard focus, readable contrast, filters, loading, error recovery and responsive navigation.
- Provide a keyboard-visible Skip to main content link before navigation. Its target
  receives focus below the sticky header. On phones, use equal 16px page gutters;
  a labelled Team chat control has dedicated header space (Chat on phones),
  leaving all page actions unobstructed. Close sits inside the chat header.
  Closed chat reserves no gutter. Header links and chat controls retain 44px targets.
- Load failures use the shared alert colors, a short heading and a contextual retry action. Retry shows its pending state and prevents duplicate reads; a failed refresh retains and labels the last saved snapshot.

Crypto has a compact Bitkub status beside its title:

| Status    | Color and meaning                                                                     |
| --------- | ------------------------------------------------------------------------------------- |
| Connected | Green; reflects the last successful update, not a continuous exchange health monitor. |
| Syncing   | Amber.                                                                                |
| Error     | Red; show error details and Retry only when an error occurs.                          |
| Checking  | Neutral before wallet data is available.                                              |

Hover, keyboard focus or click reveals wallet, price and history update times plus sync progress.

## Product truth

Portfolio summary is one compact outlined panel above the filters with three labelled values:

| Label                 | Value                                  |
| --------------------- | -------------------------------------- |
| Crypto holdings value | Held crypto value only, excluding THB. |
| Available THB         | Unreserved cash.                       |
| Holding               | Number of held coins.                  |

- It is independent of table filters, uses three columns on desktop and stacked rows on phones.
- Unknown amounts display a dash and an explanation, never an invented zero.
- Available THB is the broker-reported unreserved balance; pending local proposals can reduce engine spendable capital further. Do not force these separate scopes to agree.
- Keep wallet, price and history timestamps visible. Engine last buy and P/L are labelled as confirmed live engine evidence. Wallet identity is unknown when the source omits it.

Portfolio rows and history reflect actual imported Bitkub data. Retain coins after sale. Never fill empty areas with fictitious holdings, profit, charts or summary values. Clearly disclose limited exchange history, excluded broker conversions and unknown cost/P&L. Styling never changes account authority or trading permissions.

## Workspace ownership

Home owns the office and concise operational state. Dashboard owns financial
KPIs and charts. Crypto owns holdings, history, proposals and trading controls.
Daily Brief owns full research reports. Team chat is one persistent transcript:
an accessible modal overlay at every screen size, initially closed. Its stable
header launcher opens that same transcript; closing or resizing preserves the
reading position. Native focus trapping, Escape, backdrop close and focus return
protect reading and keyboard navigation. The panel sits 16px from the viewport
edges below the header, slides in over 320ms and out over 200ms, and retains its
content throughout exit. Reduced motion removes the slide. There is no second chat.

## Crypto trading controls

- Keep current operating mode, freshness, active proposals, Disable and Emergency stop visible near the page top.
- Paper balances must be clearly labelled as simulated; unknown profit stays Unknown.
- Paper cash, cost and equity come from the Paper ledger, including immediately after a mode change. Retained account-capital observations must not appear as simulated balances.
- Keep detailed engine books, diagnostics and retained reports in disclosures. Uncertain orders open their book automatically.
- Remove the standalone Manual Risk sizing interface while preserving server risk policies and guards.
- Keep accounting recovery inside a contextual **Balance review** drawer, not a standalone Crypto card. A known discrepancy changes the primary action to **Review balances** before live activation. Show one actionable warning; retain the last rejected command under its details.
- Guide balance review through sold holdings and then available cash. Opening the drawer and each confirmed sale prepare the next read-only preview when one retained lot and one matching sale are unambiguous, or a cash preview when no lots remain. Advance only after the exact confirmation succeeds and fresh status verifies the same account. Ambiguous selections remain manual; failed or uncertain confirmations never trigger automatic retries or advance. Show progress during status/history/preview reads. Keep unresolved quantities and unknown cost/profit visible; show the account once in a fresh preview, retaining current-account context during uncertain recovery. Each accounting step retains its exact evidence-bound owner confirmation; cash confirmation closes the drawer and refreshes status. It never opens or approves a live mandate.
- If balance review needs a stopped mode, show **Pause trading to review** in the drawer instead of a disabled form. The explicit action checks the displayed account and rules, disables trading, reloads status, then prepares the read-only preview. Opening the drawer never changes mode. Unresolved orders show their own blocker; uncertain confirmations remain available for inspection.
- When emergency stop is active, replace its button with **Clear emergency stop**. This explicit owner action returns to stopped setup with the mandate revoked; it never enables trading. Include the stop explanation in the balance warning when both apply, rather than stacking duplicate alerts.
- Show trading status once beside its heading, with freshness on the same header. Use labelled Settings and Review & enable actions; keep Emergency stop visible and separate from setup. Failed commands retain their own explanation and immediately reread status; a successful read shows the verified mode rather than labelling every rejection a status outage. Never retry the command automatically.
- Use Orders & positions, System details and Daily reports for collapsed details. Balance warnings keep affected symbols and unverified profit visible beside their recovery action.
- Unconfirmed submissions or cancellations show **Review orders** as the primary action. It opens and focuses the existing order section without changing trading authority. Keep active orders above accounting details, with side, symbol, exact amount and units, limit, creation time and the recorded broker check visible. Request and broker IDs and the original strategy reason open under **Order details**. Missing check timestamps mean **No check time recorded**. Live orders needing review remain visible in Paper mode, labelled as live.
- Keep an order visible while its cancellation remains unconfirmed, even if its recorded order status is terminal.
- Portfolio targets and deadlines use the account objective in the current response. Missing objectives show **Target unavailable**; do not supply a default amount or date. Label the exclusive deadline with **Before** and its Bangkok time.
- An enabled mandate with an unresolved submission or cancellation displays **Enabled · Blocked**. A healthy heartbeat does not establish that a broker order was confirmed.

### Settings

- Keep trading settings in a separate side panel. Adaptive settings initially show a read-only **System-managed trading plan** and **Saved risk limits**.
- Put adaptive numeric editors only inside the explicit **Advanced overrides** disclosure. Keep legacy strategy and execution fields under **Advanced settings** and preserve every saved rule.
- If status refresh fails while Settings is open, keep the error and **Refresh status** above the scrollable settings body. Preserve unsaved edits while the account and saved rules are unchanged; reset them when either changes. Show pending feedback, disable duplicate refreshes and retain keyboard focus after recovery. Show the same error on the main panel only after Settings closes.
- Native form validation opens a collapsed disclosure containing an invalid field before moving focus to that field. Preserve required fields and numeric limits.
- One live activation review presents the exact account, eligible-market scope or legacy symbols, verified capital, limits, rules hash and owner-selected expiry.
- Adaptive setup does not show fixed-symbol or fixed-order fields as active requirements.
- Expiry shortcuts only fill the date; they never submit.
- Explain disabled activation next to its required date field.
- If refreshed balances block an open activation review, show the reason in that review and disable its confirmation immediately.
- Settings, research controls and trading authority remain separate.

## Daily Brief archive

- Keep active manual proposals visible; completed proposals are retained under Past proposals.
- Positions, orders and daily reports expand on demand.
- One selected day uses a bounded, keyboard-focusable report reader. Its Daily reports heading labels the selector. Start with the latest report, honour date/run deep links, and keep every retained report selectable newest first. Date and report selections update the URL; reloading and location changes restore them. Polling preserves the chosen report and reading position; selecting another starts at its top. Wheel and keyboard navigation must remain able to leave the reader.
- Keep current research state, next review and Pause/Resume in one compact panel; put cadence, last completion, role and budget details inside its Research details disclosure. Pending controls show Updating and remain disabled through the confirming status read. Phone labels may shorten to Pause/Resume while their accessible names retain research.
- The Daily Brief reader uses 12px panel padding and 16px section gaps below 600px. Wider screens use 16px panel padding and 12px gaps so the report begins near the first screen. Keep 44px control targets and the full report typography.
- Render original Markdown with real headings, lists, GFM tables and HTTP(S) source links. Report body uses 16px text, 1.65 line height and at most 75ch. Tables and code scroll horizontally without an inner vertical height limit.
- Disable raw HTML, images and unsafe URL schemes. Task checkboxes stay disabled. Archive and sidebar footnote identities are separate. Missing summaries are explicit; never invent a conclusion from incomplete prose.
- Keep account isolation, historical date guards, loading, retry and source timestamps.
- Research briefs and learning notes are collapsed initially, with all original text available.
- Archive reports start with their recorded outputs. Run status, research brief and assessment details follow the outputs; recorded errors remain prominent above them. Role headings identify the outputs without repeating the selected report's role and time.
- Show each retained report once in the conversation. Keep Reviewer output and learning notes within its recorded task; do not repeat the full output in another summary card.
- Preserve plain text rendering, focus indicators and 44px interaction targets.

## Home research office

Home retains the neutral Quantara shell and labelled semantic states. Its primary
view is the full-width original Pixel Agents App in an iframe, preserving the
upstream office layout, assets and pixel font. Keep the native office UI intact
inside the workspace. Role controls sit in a vertical box beside the office on
desktop and stack below it on smaller screens. Full evidence stays in Daily Brief.

Eight role identities connect native character/card selection with the
keyboard-accessible role controls. Explicit selection focuses the native camera,
including selecting the same role again. Status refreshes preserve manual pan; the
research archive link opens the canonical reports. Show stale, missing and error
states explicitly. Upstream idle wandering continues without marking a role as
working. Motion pauses for reduced motion, hidden or offscreen content, or
explicit pause.

Team chat sits below the 64px header and starts closed at every width.
Opening uses a modal drawer, backdrop and focus trap, with a 16px outer inset.
It overlays the workspace without reserving space. The drawer uses Dusk surfaces, a dark Q tile, a compact
heading and a freshness row with Refresh. A labelled Team chat control occupies
dedicated header space (Chat on phones); Close sits inside the chat header.
Both retain 44px targets. Escape collapses and returns focus to the header
control. Expansion moves focus to the conversation heading. Repeated dismissal
during the exit animation keeps the drawer closed.
Preserve the mounted transcript and reading position across navigation and
viewport changes. Show new-message access without pulling a reader away. Keep
freshness, errors and Retry visible. The sidebar shows today's existing reports;
Daily Brief owns the full dated archive. Do not add a floating bubble, composer
or invented unread counter.
Home's composition is recorded in the
[Home surface brief](.impeccable/surfaces/home.md).

## Dashboard financials

Use four current/scoped KPIs and accessible SVG charts with numerical tables.
Arrange the KPIs in one column on phones, two from 600px and four from 1280px.
Keep actual charts primary; hypothetical strategy research and performance scope
open on demand. Chart source notes remain available under Source & scope; empty
charts use compact text instead of a large blank plotting area.

Accepted performance dates stay in the URL. Reload, Back, Forward and workspace
location changes restore the selected day while retaining other query parameters
and fragments. Invalid dates show the current Bangkok day. Advancing the current
day at midnight replaces the URL entry; an older selected day stays selected.

Keep chart text at least 12px from 320px upward. On narrow charts, show the first,
middle and last date labels with readable gaps; retain all seven observations in
the plot and numerical table. Adjust SVG type and axis space together so labels
fit. Chart colors use the shared semantic palette. A labelled chart region leads
by keyboard to its numerical table; table text is 14px, amounts align right and
headings use the shared dark surface.

Account equity and recorded return require fresh, unblocked capital evidence;
return must close at the same valuation timestamp. Account drawdown and complete
cash-flow-adjusted equity history remain unavailable until the API supplies them.
When a KPI is unavailable, its caption states the supplied blocker or freshness
limit. Return captions use the supplied interval explanation when capital is valid;
they do not assume every missing value is caused by cash-flow coverage. Refresh
reloads retained evidence and does not promise a new broker valuation.

Keep current valuation separate from the selected Bangkok performance day.
Seven-day live engine equity represents allocated strategy capital; confirmed
live net exits exclude deposits, withdrawals, manual and Paper results. Missing
dates remain gaps. Strategy studies are independent hypothetical scenarios,
never actual account profit. Preserve source/as-of labels and Crypto drilldowns.
Today's live ledger remains visible in Stopped and Manual Review modes while its
heartbeat is fresh. Paper mode uses retained live reports for these charts;
simulated results never replace actual outcomes.

The shared date-only picker uses MUI X Community with Dayjs, Bangkok timezone,
`DD/MM/YYYY` display and visible validation. Changing a report date does not
change account authority or current wallet evidence.
Calendar days, month/year controls and the calendar opener have 44px targets.
Align weekday headings with those targets and allow enough height for a six-week month.
