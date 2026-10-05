# Company Historical Replay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replay Quantara's autonomous paper company from 2024-01-01 through 2025-12-31 with THB 100,000, strict no-lookahead controls, isolated accounting, and visible Operations-view results.

**Architecture:** Domain owns a deterministic multi-asset replay engine, historical clock, isolated ledger, and look-ahead audit. Application loads a fixed configured universe with pre-period warmup, publishes company-role events, and calls the engine. API exposes one guarded endpoint; web runs it and renders verified results without touching the live sandbox portfolio.

**Tech Stack:** .NET 10, C#, Mediator, ASP.NET Core minimal API, xUnit, Next.js/React/TypeScript, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-08-27-company-historical-replay-design.md`

## Global Constraints

- V1 remains paper-trading only; no broker execution path.
- `RiskEngine` remains deterministic final authority.
- Simulation portfolio is isolated from persisted sandbox portfolio.
- Decisions may consume only bars dated on or before their recorded cutoff.
- A decision executes only on the next eligible bar after its cutoff.
- Any look-ahead violation invalidates the complete result.
- Initial defaults are 2024-01-01, 2025-12-31, and THB 100,000.
- Long-only, cash-backed, no leverage; fees and slippage are each 0.1%.
- Workspace has no `.git`; commit steps are intentionally omitted.

---

## File Structure

- Create `src/TradingCompany.Domain/HistoricalCompanySimulation.cs`: result contracts, audit, isolated ledger, scheduling, replay engine.
- Create `src/TradingCompany.Application/RunCompanySimulationCommand.cs`: application command.
- Create `src/TradingCompany.Application.Handlers/RunCompanySimulationHandler.cs`: data loading and role events.
- Create `src/TradingCompany.Api/CompanySimulationRequest.cs`: HTTP request contract.
- Modify `src/TradingCompany.Api/Program.cs`: registrations and guarded simulation endpoint.
- Create `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`: deterministic domain and application regression tests.
- Modify `tests/TradingCompany.Domain.Tests/TradingTests.cs`: API defaults/validation and persisted-portfolio isolation checks.
- Modify `apps/trading-company-web/app/types.ts`: simulation response types.
- Modify `apps/trading-company-web/app/page.tsx`: run action and result panel.
- Modify `apps/trading-company-web/app/globals.css`: responsive chart, audit, and result styling.
- Modify `README.md`: document historical company replay and limitations.

---

### Task 1: Historical clock and look-ahead audit

**Files:**
- Create: `src/TradingCompany.Domain/HistoricalCompanySimulation.cs`
- Test: `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`

**Interfaces:**
- Produces: `SimulationDecisionAudit`, `LookaheadVerification`, `HistoricalSimulationClock.Dates()`, and `LookaheadAudit.Verify(...)`.

- [ ] **Step 1: Write failing audit tests**

```csharp
[Fact]
public void Clock_advances_monotonically_through_requested_dates()
{
    var dates = new HistoricalSimulationClock(new(2024, 1, 1), new(2024, 1, 3)).Dates();
    Assert.Equal([new(2024, 1, 1), new(2024, 1, 2), new(2024, 1, 3)], dates);
}

[Fact]
public void Audit_rejects_observation_after_decision_cutoff()
{
    var entry = new SimulationDecisionAudit(new(2024, 2, 1), new(2024, 2, 2), new(2024, 2, 2), new(2024, 2, 5));
    var result = LookaheadAudit.Verify([entry]);
    Assert.False(result.Verified);
    Assert.Contains(result.Violations, x => x.Code == "FUTURE_OBSERVATION");
}

[Fact]
public void Audit_requires_execution_after_cutoff()
{
    var entry = new SimulationDecisionAudit(new(2024, 2, 1), new(2024, 2, 1), new(2024, 2, 1), new(2024, 2, 1));
    Assert.Contains(LookaheadAudit.Verify([entry]).Violations, x => x.Code == "EARLY_EXECUTION");
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/TradingCompany.Domain.Tests/TradingCompany.Domain.Tests.csproj --filter HistoricalCompanySimulationTests`

Expected: compile failure because historical simulation types do not exist.

- [ ] **Step 3: Implement minimal immutable audit types**

```csharp
public sealed record SimulationDecisionAudit(DateOnly DecisionDate, DateOnly DataCutoff, DateOnly LatestObservation, DateOnly? ExecutionDate);
public sealed record LookaheadViolation(string Code, string Message, DateOnly DecisionDate);
public sealed record LookaheadVerification(bool Verified, IReadOnlyList<LookaheadViolation> Violations);
```

Implement inclusive monotonic dates. Audit adds `FUTURE_OBSERVATION` when `LatestObservation > DataCutoff` and `EARLY_EXECUTION` when `ExecutionDate <= DataCutoff`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all Task 1 tests pass.

---

### Task 2: Isolated multi-asset company replay

**Files:**
- Modify: `src/TradingCompany.Domain/HistoricalCompanySimulation.cs`
- Modify: `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`

**Interfaces:**
- Consumes: `WalkForwardStrategySelector`, `RiskEngine`, `StrategyCostModel`, `HistoricalBar`.
- Produces: `HistoricalCompanySimulationEngine.Run(CompanySimulationInput) : CompanySimulationResult`.

- [ ] **Step 1: Write failing replay tests**

Add tests proving:

```csharp
[Fact] public void Replay_is_deterministic_and_never_spends_future_bars();
[Fact] public void Every_fill_occurs_after_its_decision_cutoff();
[Fact] public void Flat_market_stays_in_cash_and_counts_hold_days();
[Fact] public void Ledger_never_has_negative_cash_or_exposure_above_equity();
[Fact] public void Fees_and_slippage_reduce_equity();
[Fact] public void Deliberate_future_observation_invalidates_result();
```

Use generated 2023-2025 bar series with known dates. Repeat the same input twice and compare complete results. Name the production mutation that each assertion catches in a test comment.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: compile failure for `CompanySimulationInput`, `CompanySimulationResult`, and `HistoricalCompanySimulationEngine`.

- [ ] **Step 3: Add result contracts**

```csharp
public sealed record CompanySimulationInput(
    DateOnly Start, DateOnly End, decimal InitialCapital,
    IReadOnlyDictionary<Symbol, IReadOnlyList<HistoricalBar>> Histories,
    StrategyCostModel Costs);

public sealed record SimulationEquityPoint(DateOnly Date, decimal Equity, decimal Cash, decimal Exposure);
public sealed record SimulationTrade(DateOnly DecisionDate, DateOnly ExecutionDate, Symbol Symbol, OrderSide Side, decimal Quantity, decimal Price, decimal Fee, decimal Slippage, decimal RealizedPnl);
public sealed record SimulationContribution(Symbol Symbol, decimal RealizedPnl, decimal UnrealizedPnl, decimal FinalValue);
public sealed record SimulationTimelineItem(DateOnly Date, string Role, string Phase, string Summary, DateOnly DataCutoff);
public sealed record SkippedSimulationSymbol(Symbol Symbol, string Reason);
public sealed record CompanySimulationResult(DateOnly Start, DateOnly End, decimal InitialCapital, decimal FinalEquity, decimal TotalPnl, decimal ReturnPercent, decimal MaxDrawdown, decimal TotalFees, decimal TotalSlippage, int HoldDays, int RejectedProposals, IReadOnlyList<SimulationTrade> Trades, IReadOnlyList<SimulationEquityPoint> EquityCurve, IReadOnlyList<SimulationContribution> Contributions, IReadOnlyList<SimulationTimelineItem> Timeline, IReadOnlyList<SkippedSimulationSymbol> SkippedSymbols, LookaheadVerification Lookahead, string UniversePolicy, string TradingMode = "paper", string Currency = "THB");
```

- [ ] **Step 4: Implement minimal weekly replay**

Implementation rules:

- validate start/end/capital and unique ascending bars;
- iterate calendar dates and mark positions only from bars visible that date;
- on the first available day of each ISO week, slice each history with `bar.Date <= decisionDate`;
- require at least 200 visible bars;
- call selector using only the slice;
- require promoted evidence plus bullish `DeterministicForecaster` result;
- rank by selected out-of-sample return, then symbol;
- hold at most four positions; target no more than 20% of current equity per position;
- exit a held symbol when bearish, when strategy is no longer promoted, or at 5% stop loss;
- construct each order from the next bar with `Date > decisionDate`, using open price plus/minus slippage;
- call `RiskEngine.Evaluate` before every fill;
- keep cash non-negative; mark equity daily; track high-water drawdown;
- close remaining positions on the final available bar only for reporting;
- record daily role timeline only for scheduled work, not every role every day;
- run `LookaheadAudit.Verify` over all decisions before returning.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: Task 1-2 tests pass with deterministic output.

---

### Task 3: Application orchestration and real-data loading

**Files:**
- Create: `src/TradingCompany.Application/RunCompanySimulationCommand.cs`
- Create: `src/TradingCompany.Application.Handlers/RunCompanySimulationHandler.cs`
- Modify: `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`

**Interfaces:**
- Consumes: `IHistoricalMarketDataProvider`, `HistoricalCompanySimulationEngine`, `IRealtimeEventPublisher`.
- Produces: `RunCompanySimulationCommand` handled through Mediator.

- [ ] **Step 1: Write failing handler tests**

Test a fake provider containing two symbols and a persisted `PaperPortfolio` sentinel snapshot. Assert:

- provider is called with `start.AddDays(-400)` through `end`;
- one failed symbol becomes `SkippedSymbols` while successful symbols still run;
- event order includes Treasury mandate, CEO objective, Chief of Staff agenda, Quant research, Risk review, Paper execution, Controller reconciliation, and completion;
- repeat runs return identical accounting;
- external `PaperPortfolio.Snapshot` remains exactly unchanged.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing command/handler compile failure.

- [ ] **Step 3: Add command and handler**

```csharp
public sealed record RunCompanySimulationCommand(
    DateOnly Start, DateOnly End, decimal InitialCapital,
    IReadOnlyList<Symbol> Universe) : ICommand<CompanySimulationResult>;
```

Load symbols with bounded parallelism of 6, preserving deterministic symbol ordering in collected results. Catch per-symbol HTTP/data exceptions as skipped diagnostics. Never inject or reference persisted `PaperPortfolio`. Publish high-level role events only; do not emit thousands of daily SignalR events.

- [ ] **Step 4: Run handler tests and verify GREEN**

Run the focused test command. Expected: Task 3 tests pass.

---

### Task 4: Guarded API endpoint

**Files:**
- Create: `src/TradingCompany.Api/CompanySimulationRequest.cs`
- Modify: `src/TradingCompany.Api/Program.cs`
- Modify: `tests/TradingCompany.Domain.Tests/TradingTests.cs`

**Interfaces:**
- Produces: `POST /simulations/company`.

- [ ] **Step 1: Write failing endpoint tests**

Use `WebApplicationFactory<Program>` to prove:

- empty JSON uses 2024-01-01, 2025-12-31, THB 100,000;
- non-positive capital returns 400;
- end before start returns 400;
- end after yesterday returns 400;
- range longer than five years returns 400;
- a second concurrent request returns 409.

- [ ] **Step 2: Run API tests and verify RED**

Expected: 404 from `/simulations/company`.

- [ ] **Step 3: Add request, registrations, and endpoint**

```csharp
public sealed record CompanySimulationRequest(DateOnly? Start, DateOnly? End, decimal? InitialCapital);
```

Register `HistoricalCompanySimulationEngine` and a singleton non-blocking `SemaphoreSlim(1, 1)`. Map defaults, validate request, read configured `AutonomousCompanyOptions.Universe`, send command, release guard in `finally`, return 409 when guard cannot be acquired immediately.

- [ ] **Step 4: Run API tests and verify GREEN**

Run API-focused tests. Expected: all endpoint tests pass.

---

### Task 5: Operations-view simulation panel

**Files:**
- Modify: `apps/trading-company-web/app/types.ts`
- Modify: `apps/trading-company-web/app/page.tsx`
- Modify: `apps/trading-company-web/app/globals.css`

**Interfaces:**
- Consumes: JSON `CompanySimulationResult` from Task 4.
- Produces: interactive `CompanySimulationPanel` in Operations view.

- [ ] **Step 1: Add TypeScript contracts and component state**

Add exact camel-case mirrors for result, trades, equity points, contributions, timeline, skipped symbols, and look-ahead verification. Add `simulation`, `simulationBusy`, and `simulationError` state in `Home` and pass them to `Operations`.

- [ ] **Step 2: Add run action**

POST:

```ts
await fetch(`${api}/simulations/company`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ start: "2024-01-01", end: "2025-12-31", initialCapital: 100000 }),
});
```

Disable action while running. Show useful 400/409/network errors. Do not modify `company` or existing `result` state.

- [ ] **Step 3: Render verified evidence**

Render final equity, PnL, return, drawdown, fees, trades, HOLD days, fixed-universe label, lightweight SVG equity curve, top contributions, skipped count, and bounded timeline. Show `NO LOOKAHEAD VERIFIED` only when `lookahead.verified`; otherwise show `INVALID` and neutralize PnL color/emphasis.

- [ ] **Step 4: Style responsive states**

Add `.company-simulation`, `.simulation-status`, `.equity-chart`, `.simulation-timeline`, `.simulation-contributions`, loading, invalid, positive, and negative rules. Extend mobile breakpoint to stack metrics and timeline.

- [ ] **Step 5: Build web and fix type/style errors**

Run: `npm run build` in `apps/trading-company-web`.

Expected: production build succeeds with no TypeScript errors.

---

### Task 6: Full verification and real 2024-2025 evidence

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document operation and limits**

Document the Operations-view action, API request, fixed-universe survivorship limitation, no historical news/options, no-lookahead contract, paper-only isolation, and that profit is evidence rather than a guarantee.

- [ ] **Step 2: Run full deterministic suite**

Run: `dotnet test`

Expected: all existing and new tests pass with zero failures.

- [ ] **Step 3: Run clean builds**

Run: `dotnet build --no-restore` and `npm run build` in the web app.

Expected: zero errors and zero warnings attributable to this change.

- [ ] **Step 4: Rebuild Docker services**

Run: `docker compose up -d --build api web`

Expected: API health reports `paper-only`; web loads at `http://localhost:3000/`.

- [ ] **Step 5: Preserve sandbox state and run real replay**

Capture `/portfolio`, POST the 2024-2025 simulation, capture `/portfolio` again, and assert snapshots are byte-equivalent. Record returned final equity, PnL, drawdown, trades, fees, skipped symbols, and look-ahead status. Never change implementation based on locked-period profitability.

- [ ] **Step 6: Visually verify Operations view**

Open Operations view, run simulation, and verify loading, verified/invalid badge, metrics, equity curve, contributions, timeline, and responsive layout reflect the real API response.
