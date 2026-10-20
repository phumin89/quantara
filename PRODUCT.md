# Quantara

<!-- impeccable:product-schema 1 -->

## Platform

Web application, responsive on desktop, tablet and phone.

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

| Workspace                    | Behavior                                                  |
| ---------------------------- | --------------------------------------------------------- |
| Home (`/`)                   | Read-only research office showing recorded role states.   |
| Crypto (`/crypto`)           | Shows actual balances, prices and imported trade history. |
| Daily Brief (`/daily-brief`) | Opens with compact trading status and optional team chat. |

- A single-page application with shared left navigation; Home and Crypto are reachable without a full document reload.
- The interface language is English, including accessible control labels.
- Crypto displays real imported account evidence. Paper trading is explicitly simulated; real spot LIMIT transactions require owner-enabled trading authority.
- The backend persists imported records in SQL Server. Closed coins remain visible.
- Missing history never authorizes inferred cost or profit; broker conversions and activity outside the available window are incomplete.
- Build and verify one feature at a time.

Daily Brief research:

- The owner can stop/resume automatic read-only research.
- CEO plans, Analyst uses a whitelisted portfolio snapshot, and Reviewer checks prior output.
- Actual outputs and state transitions are retained in Team's SQL database.
- Authentication/runtime/storage failures remain visible; the app never substitutes fake AI reports.
- Team activation does not authorize trading.

## Brand Commitments

Follow the owner-provided DESIGN.md. Use its documented font fallback until
licensed Saans assets are supplied. Preserve the original design guide.

## Engineering

- Follow the owner's [code-instructions.md](code-instructions.md): Clean Code, SOLID, inward dependencies, guard clauses, explicit errors and meaningful deterministic tests.
- Service/layer boundaries and one type/component per matching file are enforced by architecture checks.
- Controllers use Mediator CQRS; query handlers receive read-only repository interfaces.
- Frontend routes/components/services/API/contracts are separated.
- Editor/build diagnostics and CI enforce strict compiler flags, formatting, lint, architecture and deterministic tests.
