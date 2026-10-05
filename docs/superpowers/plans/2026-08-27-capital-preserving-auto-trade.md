# Capital-Preserving Auto Trade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace raw one-year SMA decisions with cost-aware walk-forward strategy selection that defaults to cash and is visible in company status.

**Architecture:** Domain owns deterministic strategy simulation, benchmarks, promotion gates, and walk-forward selection. Application handler loads sufficient real data and returns validation evidence. Autonomous company consumes only promoted evidence; web renders the same API state.

**Tech Stack:** .NET 10, xUnit, ASP.NET Core, Mediator, Next.js/TypeScript, SignalR

**Spec:** `docs/superpowers/specs/2026-08-27-capital-preserving-auto-trade-design.md`

## Global Constraints

- Paper trading only; no live execution path.
- No leverage or short selling.
- `RiskEngine` remains deterministic final authority.
- Tests are deterministic; external HTTP appears only in explicit smoke checks.
- No Ollama/local-AI packages or runtime wiring.

---

### Task 1: Cost-aware deterministic strategy simulations

**Files:**
- Create: `src/TradingCompany.Domain/StrategyValidation.cs`
- Modify: `src/TradingCompany.Domain/Trading.cs`
- Test: `tests/TradingCompany.Domain.Tests/TradingTests.cs`

**Interfaces:**
- Produces: `StrategyKind`, `StrategyCostModel`, `StrategySimulation`, `IDeterministicStrategy`, `SmaTrendStrategy`, `BreakoutStrategy`, `MeanReversionStrategy`.

- [ ] Write failing tests proving each strategy is deterministic, fees/slippage lower equity, and no strategy trades without its signal.
- [ ] Run focused tests; confirm failures because strategy types do not exist.
- [ ] Implement minimal daily-bar long/cash simulators using prior completed bars only and `TradeQuantity.FloorFor`.
- [ ] Run focused tests; confirm pass.

### Task 2: Walk-forward selection and capital gates

**Files:**
- Modify: `src/TradingCompany.Domain/StrategyValidation.cs`
- Test: `tests/TradingCompany.Domain.Tests/TradingTests.cs`

**Interfaces:**
- Produces: `StrategyValidationEvidence`, `StrategySelection`, `WalkForwardStrategySelector.Select(IReadOnlyList<HistoricalBar>, Symbol, decimal, StrategyCostModel)`.

- [ ] Write failing tests proving unseen future bars cannot change an earlier fold, drawdown above 10% rejects, fewer than two round trips rejects, and no eligible candidate selects Cash.
- [ ] Run focused tests; confirm expected failures.
- [ ] Implement chronological train/validation split, cash and buy-and-hold benchmarks, promotion gates, deterministic ranking by return-to-drawdown then net return.
- [ ] Run focused tests; confirm pass.

### Task 3: Application and autonomous-company integration

**Files:**
- Modify: `src/TradingCompany.Application/OneYearBacktestCommand.cs`
- Modify: `src/TradingCompany.Application.Handlers/OneYearBacktestHandler.cs`
- Modify: `src/TradingCompany.Api/AutonomousCompany.cs`
- Modify: `src/TradingCompany.Api/Program.cs`
- Test: `tests/TradingCompany.Domain.Tests/TradingTests.cs`

**Interfaces:**
- Produces: strategy-selection evidence on backtest response and `CompanyStatus`.

- [ ] Write failing API/company tests proving rejected selection yields HOLD/no fill and promoted selection reaches existing `RiskEngine`.
- [ ] Run focused tests; confirm expected failures.
- [ ] Inject selector, load sufficient history, replace raw `result.TotalPnl > 0` entry authority with `selection.Promoted`.
- [ ] Keep exits, shared ledger, cooldown, option caps, and company kill switch unchanged.
- [ ] Run focused tests; confirm pass.

### Task 4: Operations evidence UI

**Files:**
- Modify: `apps/trading-company-web/app/types.ts`
- Modify: `apps/trading-company-web/app/page.tsx`
- Modify: `apps/trading-company-web/app/globals.css`

**Interfaces:**
- Consumes: strategy-selection fields from `CompanyStatus`.
- Produces: strategy validation card with selected strategy, promotion state, OOS return, drawdown, benchmarks, costs, and rejection reason.

- [ ] Add TypeScript types matching API JSON.
- [ ] Render explicit CASH/HOLD state when no strategy passes.
- [ ] Render paper-only and non-guarantee labels beside validation evidence.
- [ ] Run `npm run build` in `apps/trading-company-web`; confirm pass.

### Task 5: End-to-end verification

**Files:**
- Modify: `README.md` only if run instructions or status fields changed.

- [ ] Run `dotnet build TradingCompany.sln --no-restore`.
- [ ] Run full test assembly with `dotnet vstest tests/TradingCompany.Domain.Tests/bin/Debug/net10.0/TradingCompany.Domain.Tests.dll`.
- [ ] Run web production build.
- [ ] Start API with autonomous startup disabled and POST BTC-THB to the backtest endpoint.
- [ ] Confirm response uses `real-public`, `paper`, `THB`, includes selection evidence, and no live execution capability.
- [ ] Report measured result honestly; do not claim profitability when evidence is negative.
