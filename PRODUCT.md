# Quantara

<!-- impeccable:product-schema 1 -->

## Platform

web

Responsive on desktop, tablet and phone.

## Users

The owner operates a private Bitkub THB spot research and trading workspace.
They need to understand account evidence, review the team's reasoning and control
trading without navigating technical diagnostics for routine tasks.

## Stack

| Area       | Technology and ownership                                                                                |
| ---------- | ------------------------------------------------------------------------------------------------------- |
| Frontend   | Next.js, TypeScript and Material UI.                                                                    |
| Backend    | C#, ASP.NET Core, Mediator and SQL Server.                                                              |
| MarketData | Owns public prices, symbols and candles.                                                                |
| Portfolio  | Owns account evidence, trading and its database.                                                        |
| Team       | Owns AI task orchestration and a separate SQL Server database; its isolated Node worker uses Codex SDK. |

## Product Purpose

An AI company operated by its owner. Bitkub THB spot trading is an owner-enabled
Portfolio capability: the adaptive engine scans an eligible versioned universe
and sizes from verified account capital. AI research never authorizes or selects
broker orders.

## Current Scope

| Workspace                    | Behavior                                                                 |
| ---------------------------- | ------------------------------------------------------------------------ |
| Home (`/`)                   | Original Pixel Agents office UI showing recorded research role states.   |
| Dashboard (`/dashboard`)     | Financial KPIs, dated engine charts and clearly scoped research studies. |
| Crypto (`/crypto`)           | Balances, imported history, holdings, proposals and trading controls.    |
| Daily Brief (`/daily-brief`) | Full dated research archive and research operating controls.             |

- A single-page application with a slim shared header for Home, Dashboard, Daily Brief and Crypto; phones use its menu. The header account context comes from research scope, not broker identity verification. A small footer carries report timezone and a data-scope link.
- Team chat reserves 380px beside the centered workspace while open on desktop. It opens as a modal right drawer on narrow screens. A labelled control in the shared header opens it; Close sits inside the chat header. Escape collapses it, and navigation preserves conversation and reading position. It is read-only, without a composer. All routes share current Team activity reads; the dated archive stays in Daily Brief.
- Dashboard separates current verified account valuation from Bangkok selected-day engine charts. Live, Paper and hypothetical studies are labelled separately. Missing account equity history, contributions and drawdown stay unavailable; deposits never become trading profit.
- Home keeps concise operational state, the original office and role selectors beside it on desktop. Full role evidence belongs in Daily Brief.
- Daily Brief retains one selected Bangkok day in a bounded keyboard-scrollable reader. A report selector starts with the latest run and preserves the selected report during polling; every older run and date/run deep link stays accessible. Original Markdown headings, lists, tables and source links render safely; unstructured text keeps its wording, and absent summaries remain explicit. There is no generated client-side financial summary.
- Crypto labels distinguish held crypto value from THB and live engine fills from whole-account profit. Visible wallet/price/history timestamps explain asynchronous snapshots. Account-context changes reset the entire Crypto view; the wallet API still does not provide an account identity.
- The interface language is English, including accessible control labels.
- Crypto displays real imported account evidence. Paper trading is explicitly simulated; real spot LIMIT transactions require owner-enabled trading authority.
- The backend persists imported records in SQL Server. Closed coins remain visible.
- Missing history never authorizes inferred cost or profit; broker conversions and activity outside the available window are incomplete.
- Build and verify one feature at a time.

Daily Brief research:

- The owner can stop/resume automatic read-only research.
- Focused task research runs from the selected specialist to Reviewer; the retained legacy/manual flow remains CEO planning, Analyst evidence review, and Reviewer checks of prior output.
- Actual outputs and state transitions are retained in Team's SQL database.
- Authentication/runtime/storage failures remain visible; the app never substitutes fake AI reports.
- Team activation does not authorize trading.

## Brand Commitments

Follow the owner's current [DESIGN.md](DESIGN.md). The 28 October 2026 direction
uses Hallmark Dusk: a sunset-valley photograph, translucent smoked panels,
periwinkle actions and apricot pending states. Self-host Nunito for headings,
retain the native Pixel Agents office and keep financial states and report text
readable.

## Product Principles

- Keep routine tasks easy to find, read and complete across every page.
- Give each fact one clear home; link to details instead of repeating reports.
- Ground each agent's conclusion in evidence, uncertainty and reconsideration conditions.
- Separate verified balances, strategy records and hypothetical results; missing values stay unknown.
- Preserve owner control of real trading and make status, failures and recovery clear.

## Engineering

- Follow the owner's [code-instructions.md](code-instructions.md): Clean Code, SOLID, inward dependencies, guard clauses, explicit errors and meaningful deterministic tests.
- Service/layer boundaries and one type/component per matching file are enforced by architecture checks.
- Controllers use Mediator CQRS; query handlers receive read-only repository interfaces.
- Frontend routes/components/services/API/contracts are separated.
- Editor/build diagnostics and CI enforce strict compiler flags, formatting, lint, architecture and deterministic tests.
