# Forward Paper Company Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Complete the approved isolated forward paper company workflow without live execution or automatic paid-AI activation.

**Architecture:** Keep CFO funding as the source of approved contributions. A versioned event journal owns investment cash, positions, pending intents and execution receipts. Reuse the existing meeting runner, market evidence provider and RiskEngine; external inference is replaced only in labeled test fixtures.

**Tech Stack:** .NET 10, xUnit, JSON atomic file persistence, Next.js/React, existing providers.

**Spec:** `docs/superpowers/specs/2026-09-12-forward-paper-company-design.md` (user approved).

## Global Constraints

- No live broker, bank connection, leverage, paid-model activation or local-AI runtime is included.
- Keep the current THB 70,000 monthly salary, THB 14,000 housing and saved CFO mandate unchanged.
- Only approved investment releases fund trading.
- HOLD and no-trade days are valid outcomes, not failures.
- Positive returns are not an acceptance criterion for software correctness.
- Do not relax existing risk limits to improve a test result.

## Execution notes

### Acceptance record — 2026-09-14

- [x] Task 1: event journal, deterministic risk, valuation and accounting tests implemented and reviewed. The successful two-unit USD 100 fixture uses THB 40,000 (cash 32,990; equity 39,990; closing cash 40,680), preserving the existing 20% order ceiling. THB 12,000 remains an oversize-order rejection case, not a reason to relax risk.
- [x] Task 2: concrete `JsonForwardPaperStore` lease/checksum/atomic append ownership, source reconciliation and v1-preservation tests.
- [x] Task 3: controlled-clock eight-role BUY/HOLD/SELL rehearsal, restart, invalid-output and interrupted-meeting tests; legacy AI coordinator disabled while forward funding owns execution.
- [x] Task 4 core proof: 695 .NET tests, 10 web tests, typecheck, production build, API inspection and desktop Office inspection. Restart preserved the initialized execution journal. See research note for commands and limitations.
- [ ] Hosted inference plus authenticated quote-provider end-to-end smoke test: intentionally not activated; needs an explicit AI spending ceiling and suitable data credentials.
- [ ] Mobile viewport and Docker container boot verification: not completed. Docker configuration validation alone is not a deployment test.

The task-level checkboxes below retain the original planned steps, not an assertion that every proposed manual experiment ran. The acceptance record above and research note describe actual evidence. Scripted gateway results prove orchestration, not investment performance. No live-money readiness claim.

No Git metadata is present (`git rev-parse --git-dir` fails), so work in place, preserve existing files, and do not claim commits/worktree isolation. Lead owns the tightly coupled money implementation and review. A bounded UI worker may implement presentation after contracts settle. No approval is requested between implementation steps.

### Task 1: Deterministic execution journal

Files: create `src/TradingCompany.Application/ForwardPaperAccount.cs`, `tests/TradingCompany.Domain.Tests/ForwardPaperAccountTests.cs`.

Interfaces: `ForwardPaperAccount` stores version, funding account ID and ordered events. `ForwardPaperLedger` exposes `Open`, `Fund`, `Submit`, `Execute`, `Snapshot` and `Validate`. Orders retain decision time, quantity and policy; quotes retain native currency, FX, observed/available times and costs. The journal, not an AI response, owns balances.

- [ ] Write failing tests using 12,000 THB, buy 2 units at USD 100 with FX 35 and THB 10 fee: cash 4,990, position value 7,000, equity 11,990, PnL -10. Selling those units at USD 110, FX 35 and fee 10 produces cash/equity 12,680 and PnL 680.
- [ ] Run `dotnet test tests/TradingCompany.Domain.Tests --filter FullyQualifiedName~ForwardPaperAccount --verbosity normal` and verify missing implementation failures.
- [ ] Implement immutable event replay, exact order identities, no short sells, positive FX/prices, no pre-decision quote fills, quote freshness, fees, current-state RiskEngine checks and deterministic reconciliation. Duplicate order IDs with different payloads fail; identical requests return their original outcome.
- [ ] Add rejected-risk, missing/future/stale quote, repeated order, changed identity, tampered event, cashflow and no-capital tests; run the focused command again.

### Task 2: Durable ownership and migration

Files: create `src/TradingCompany.Infrastructure/JsonForwardPaperStore.cs`, `tests/TradingCompany.Domain.Tests/ForwardPaperStoreTests.cs`; modify `ForwardFundingService.cs` and `Program.cs` only after persistence tests pass.

Interfaces: `IForwardPaperStore.Read()` and `Update(Func<ForwardPaperAccount?, ForwardPaperAccount>)` serialize updates under an exclusive lease. Open/migrate from verified CFO contributions and v1 receipts, preserving their dates/amounts. Persist immutable events with checksum, flush and atomic replace. Expose the new state independently; old v1 files remain audit evidence, not spendable balances.

- [ ] Test concurrent identical submissions, repeated funding, interrupted commit boundaries, missing initialized state, corrupt hash and mismatched source identity.
- [ ] Implement file transaction ownership and fail-closed migration; no deletion/reset or copying demo capital.
- [ ] Verify exact v1 source bytes survive migration and old/new contribution totals match.

### Task 3: Company scheduling and execution integration

Files: create `src/TradingCompany.Api/ForwardPaperCompanyService.cs`, `tests/TradingCompany.Domain.Tests/ForwardPaperCompanyTests.cs`; modify `Program.cs` registration/status routing.

Interfaces: scheduler synchronizes funding, reconciles account, executes eligible pending intents with later observed evidence, and invokes `IAiCompanyMeetingRunner.RunAsync(AiCompanyMeetingInput, CancellationToken)` under durable daily claims. Submit validated final proposals through the journal. Disabled model produces no proposal and incurs no call.

- [ ] Run a controlled-clock test through real eight-role orchestration with a scripted external gateway: source funding, BUY proposal, later fill, HOLD, SELL, later fill and final reconciliation.
- [ ] Test same-day restart, incomplete meeting halt, invalid final JSON, missing evidence and browser-independent background ticks.
- [ ] Keep old demo coordinator separate and disabled in the forward workflow to prevent competing executions.

### Task 4: UI and final proof

Files: modify `apps/trading-company-web/app/ForwardFundingPanel.tsx`, its rendering tests; add API integration evidence in `docs/research/2026-09-12-forward-paper-company.md`.

- [ ] Add presentation tests for portfolio cash versus contributions, positions, PnL, pending orders, receipts and disabled/unavailable/reconciliation states. Assertions use domain-derived fixtures, not invented live profits.
- [ ] Implement display using the saved forward paper status, preserving office identity and explicitly labeling scripted rehearsal.
- [ ] Run `dotnet test tests/TradingCompany.Domain.Tests --verbosity quiet`, `node --test tests/*.test.cjs`, `npm run typecheck`, and production web build.
- [ ] Start safe sandbox processes with paid AI disabled; verify API and browser view, then restart backend and verify account/order identity and balances persist.
- [ ] Record completed acceptance criteria and any external activation requirements. Do not claim live AI or full completion while any required integration remains unverified.
