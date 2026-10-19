---
version: alpha
name: Quantara Workspace
description: Compact data workspace with neutral surfaces, restrained corners, black table headers and semantic status colors.
colors:
  primary: '#141414'
  on-primary: '#ffffff'
  canvas: '#ffffff'
  canvas-soft: '#f3f3f3'
  text-muted: '#707070'
  hairline: '#e0e0e0'
  success: '#166534'
  success-soft: '#dcfce7'
  warning: '#92400e'
  warning-soft: '#fef3c7'
  error: '#991b1b'
  error-soft: '#fee2e2'
  info: '#1e40af'
  info-soft: '#dbeafe'
  closed: '#6b21a8'
  closed-soft: '#f3e8ff'
  sell: '#9a3412'
  sell-soft: '#ffedd5'
rounded:
  container: 8px
  control: 6px
  tag: 4px
---

# Quantara design guide

This guide supersedes the original Mobbin pill geometry and monochrome-only status rules. Retain neutral surfaces, clear type hierarchy and shadow-free composition.

## Geometry and layout

| Element                                       | Corner radius                |
| --------------------------------------------- | ---------------------------- |
| Tables and panels                             | 8px                          |
| Buttons, navigation items, filters and inputs | 6px                          |
| Status tags                                   | 4px                          |
| Icon-only controls                            | May retain a circular target |

- Use exact pixel corner values in MUI overrides; numeric sx radii multiply the theme radius.
- Crypto and Daily Brief workspaces: left aligned, full available width up to 1280px. Main table minimum width 1040px; scroll within its focusable labelled region on smaller screens.
- Main data rows approximately 60px; table headings 40px; cells padded 16px horizontally. Interactive targets stay at least 44px.
- Coin name and ticker share one line. Numeric headings and values align right with tabular numerals. Trade history expands beneath the relevant coin.
- Keep unrelated page sections separated; keep related table data compact.

## Color meaning

- All table headings use #141414 backgrounds and white labels, including expanded history.
- All: neutral gray. Holding: green. Closed: purple, never interpreted as a loss. Filters and row tags share these meanings.
- Buy: blue. Sell: orange. Labels remain visible so color is never the only cue.
- Use installed MUI outlined icons alongside labels: grid for All, wallet for Holding, check-circle for Closed, inward arrow for Buy and outward arrow for Sell. Icons are decorative and inherit label color; preserve accessible text and selected-state cues.
- Verified positive P/L: green; negative P/L: red; zero: neutral. Unknown values remain unknown and have no invented gain/loss color.
- Incomplete coverage or unknown cost: amber. Connection and load failures: red.
- Tags use light tinted backgrounds and dark text. Navigation and ordinary actions remain neutral. No decorative gradients or shadows.

## Typography and interaction

- App copy is English, sentence case.
- Saans is preferred; supplied assets are unavailable, so retain the established system sans fallback.
- Page title 32px, body 14–16px, supporting labels 12px.
- Preserve keyboard focus, readable contrast, filters, loading, error recovery and responsive navigation.

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

| Label         | Value                   |
| ------------- | ----------------------- |
| Total assets  | Held crypto value only. |
| Available THB | Unreserved cash.        |
| Holding       | Number of held coins.   |

- It is independent of table filters, uses three columns on desktop and stacked rows on phones.
- Unknown amounts display a dash and an explanation, never an invented zero.

Portfolio rows and history reflect actual imported Bitkub data. Retain coins after sale. Never fill empty areas with fictitious holdings, profit, charts or summary values. Clearly disclose limited exchange history, excluded broker conversions and unknown cost/P&L. Styling never changes account authority or trading permissions.

## Daily Brief

Daily Brief is an operating view:

### Status

- Open on Trading status; Team chat is a separate tab.
- Keep trading status polling mounted when changing tabs.
- Show the actual operating mode, freshness, verified balances and active order counts before research or retained history.
- Paper balances must be clearly labelled as simulated; unknown profit stays Unknown.
- The selected date applies to reports, while trading status and order controls always describe the current account.

### Settings

- Keep budget and market settings in a separate side panel, with strategy and execution fields under Advanced settings. Preserve every saved rule.
- One live activation review presents the exact account, eligible-market scope or legacy symbols, verified capital, limits, rules hash and owner-selected expiry.
- Adaptive setup does not show fixed-symbol or fixed-order fields as active requirements.
- Expiry shortcuts only fill the date; they never submit.
- Explain disabled activation next to its required date field.
- Settings, research controls and trading authority remain separate.

### Chat and retained details

- Keep active manual proposals visible; completed proposals are retained under Past proposals.
- Positions, orders and daily reports expand on demand.
- Team chat retains the desktop member rail and a mobile disclosure.
- When Team chat opens or the selected date changes, show the newest recorded messages.
- Follow arriving reports while the reader is near the bottom; preserve the reading position when the reader has scrolled up to older messages.
- Keep scrolling inside the conversation, without a page or focus jump.
- Research briefs and learning notes are collapsed initially, with all original text available.
- Show the latest Reviewer summary first and put previous summaries in a disclosure.
- Preserve plain text rendering, focus indicators and 44px interaction targets.
