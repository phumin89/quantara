# Quantara — Live Workspace

Reviewed 2026-10-05. This replaces the former single-column page and Sandbox mode navigation. Preserve the owner's modern monochrome interface, self-hosted IBM Plex Sans Thai and colored Pixel Office.

## Layout and navigation

The persistent app shell uses a 224px left sidebar on desktop, with Office, Account/assets and Trading links. Each is a real Next route (`/`, `/account`, `/trading`), reachable directly and through client navigation. One shared owner/account provider survives route changes. Use one selected black navigation item, consistent stroke SVG icons, and a single connection label in the header. Sidebar status identifies Live/Bitkub without implying execution is enabled.

Content uses the available screen width. Align the page title and content padding. Office leads the homepage; current company and cash summaries sit alongside it on wide screens, below on smaller screens. Account contains verified balances and assets; Trading contains readiness and order outcomes. Do not gather every function into one long document or add empty navigation destinations.

At 700px and below, navigation becomes a compact top row and financial content stacks. Native scrolling belongs to each readable region when needed. Keep a skip link, keyboard focus, one H1 per route, controls of at least 44px and no page overflow at 375px.

## Visual language

White and pale neutral surfaces, black selected navigation and text, subtle gray boundaries. Pixel artwork retains its original colors. Use IBM Plex Sans Thai Regular/Medium/SemiBold, readable 16px body text, compact 20–24px app headings, at least 14px labels, and tabular financial numerals. No display font, negative Thai tracking, decorative metric chart or invented readiness percentage.

Use native forms, clear disabled/loading/error states and visible focus. Hover changes color/background on capable pointers with about 150ms transitions. The chat window keeps its brief arrival; reduced motion removes spatial animation. No repeating decorative animation or game-loop wandering.

## Office and team room

The original Pixel renderer and artwork are bundled with the frontend. The current viewer draws a stationary office and resizes it; it does not connect to the paper bridge, launch agents or suggest completed work. Its caption explicitly says Live team activity is not displayed yet. Connect real Live events before adding active workers or work animations.

The persistent floating team room is separate from the page. It supports close, minimize, Escape and focus return; phones use a full viewport reader. Open with the current verified system conclusion and its reasons, then disclose the source. Until a Live report feed is connected, state that reports are unavailable rather than inventing a team conversation. Future reports should prioritize decisions, reasons and meaningful disagreements, retaining raw evidence behind disclosures.

## Current data and access

Refresh verified observations in the background without clearing a fresh balance while a request is pending. Commit matching status/account data together. Account values expire after 30 seconds; failed/unauthorized observations and owner actions clear them. An expired observation is unavailable, not zero. Server observation time identifies the source, not a claimed execution or market refresh.

Pairing appears in Account or Trading only when owner verification is needed. A remembered browser retains viewing for 30 absolute days across normal starts and route changes. Remembering does not extend financial control authority, approve a mandate or activate execution. The current production application remains wallet viewing with execution disabled; the design must state that plainly.

Review evidence: `work/live-account-connect/spa-workspace-review.md`.
