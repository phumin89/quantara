# Quantara

<!-- impeccable:product-schema 1 -->

## Platform

Web application, responsive on desktop, tablet and phone.

## Stack

Next.js, TypeScript and Material UI. Read-only backend: C#, ASP.NET Core,
Mediator and SQL Server. Two services: MarketData for public prices and symbols,
Portfolio for account reads and its database. Codex integration is deferred.

## Product Purpose

An AI company operated by its owner. The owner identified automatic Bitkub
reading and trading as a future feature. No trading capability or authority
is implemented by the current frontend shell.

## Current Scope

A single-page application with left navigation. Home remains empty at `/`.
Crypto at `/crypto` shows actual balances, prices and imported trade history.
Both are reachable through
the shared left navigation without a full document reload.
The interface language is English, including accessible control labels.
No AI, simulated portfolio data or external transactions. The backend reads
Bitkub and persists imported records in SQL Server. Closed coins remain
visible. Missing history never authorizes inferred cost or profit; broker
conversions and activity outside the available window are incomplete.
Build and verify one feature at a time.

## Brand Commitments

Follow the owner-provided DESIGN.md. Use its documented font fallback until
licensed Saans assets are supplied. Preserve the original design guide.

## Engineering

Follow the owner's code-instructions.md: Clean Code, SOLID, inward dependencies,
guard clauses, explicit errors and meaningful deterministic tests.
Service/layer boundaries and one type/component per matching file are enforced
by architecture checks. Controllers use Mediator CQRS; query handlers receive
read-only repository interfaces. Frontend routes/components/hooks/API/contracts
are separated. Editor/build diagnostics and CI enforce strict compiler flags,
formatting, lint, architecture and deterministic tests.
