# Quantara — Live Workspace

Reviewed 2026-10-06. This replaces the former single-column page and Sandbox mode navigation. Preserve the owner's modern monochrome interface, self-hosted IBM Plex Sans Thai and colored Pixel Office.

## Layout and navigation

The persistent app shell uses a 224px left sidebar on desktop, with Office, Account/assets and Trading links. Each is a real Next route (`/`, `/account`, `/trading`), reachable directly and through client navigation. One shared owner/account provider survives route changes. Use one selected black navigation item, consistent stroke SVG icons, and a single connection label in the header. Sidebar status identifies Live/Bitkub without implying execution is enabled.

Content uses the available screen width. Align the page title and content padding. Office opens with one current status and one next action. Pixel and a compact cash rail sit alongside each other on wide screens, below on smaller screens. No generic setup checklist or repeated access lectures in Office. Account contains verified balances and assets; Trading contains the specific first-use requirement, owner controls and order outcomes. Do not gather every function into one long document or add empty navigation destinations.

At 700px and below, navigation becomes a compact top row and financial content stacks. Native scrolling belongs to each readable region when needed. Keep a skip link, keyboard focus, one H1 per route, controls of at least 44px and no page overflow at 375px.

## Visual language

White and pale neutral surfaces, black selected navigation and text, subtle gray boundaries. Pixel artwork retains its original colors. Use IBM Plex Sans Thai Regular/Medium/SemiBold, readable 16px body text, compact 20–24px app headings, at least 14px labels, and tabular financial numerals. No display font, negative Thai tracking, decorative metric chart or invented readiness percentage.

Use native forms, clear disabled/loading/error states and visible focus. Hover changes color/background on capable pointers with about 150ms transitions. The chat window keeps its brief arrival; reduced motion removes spatial animation. No repeating decorative animation or game-loop wandering.

## Office and team room

The original Pixel renderer and artwork are bundled with the frontend. Existing Live meeting events flow through an account-bound display snapshot into the viewer. Render only roles actually reported by the meeting; keep stable seats, preferring existing chairs and benches. Only a reported working role types; other roles remain stationary. Empty, expired or unavailable activity clears characters. Activity expires after 45 seconds without a company heartbeat, independently of financial state. Honor reduced motion and run no idle animation loop. The viewer cannot start work, change authority or submit orders. Desktop stage is 380–528px, narrow tablet 300px and phone 240–280px; preserve crisp integer pixel zoom.

The persistent floating team room is separate from the page. It has one minimize button, Escape and focus return; phones use a full viewport reader. Use a content-sized window for disabled/unavailable access, with one status and recovery route. System status is labelled separately from recorded meeting conclusions. The account-bound report reader puts decisions first and team reasoning behind disclosures. When the feed cannot be read, say reports are unavailable rather than claiming none exist. Never invent a team conversation.

## Current data and access

Refresh verified observations in the background without clearing a fresh balance while a request is pending. Commit matching status/account data together. Account values expire after 30 seconds; failed/unauthorized observations and owner actions clear them. An expired observation is unavailable, not zero. Server observation time identifies the source, not a claimed execution or market refresh.

Pairing appears in Account or Trading only when owner verification is needed. A remembered browser retains viewing for 30 absolute days across normal starts and route changes. Remembering does not extend financial control authority, approve a mandate or activate execution. The current production application remains wallet viewing with execution disabled; the design must state that plainly.

Review evidence: `work/live-account-connect/spa-workspace-review.md`.
