# Personal CFO Sandbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simulate 24 monthly salary cycles, protect essential/debt/emergency/discretionary money, and release only a reconciled investing schedule into Quantara's existing paper-company replay.

**Architecture:** A pure Domain allocator owns envelope accounting and conservation. Application orchestration publishes Treasury events and passes an immutable dated contribution schedule into the existing company simulation. One funding resolver supplies identical cash flows to the active strategy and VOO/QQQM benchmarks. A strict API endpoint and a plain-language Operations panel expose the sandbox without mutating the persisted paper portfolio.

**Tech Stack:** .NET 10, C#, Mediator, ASP.NET Core minimal API, xUnit, Next.js, React, TypeScript, SignalR.

**Spec:** `docs/superpowers/specs/2026-09-01-personal-cfo-sandbox-design.md`

## Global Constraints

- V1 stays paper-only: no bank credentials, transfers, broker execution, OTP handling, or live mode.
- `RiskEngine` remains deterministic final authority for every simulated order.
- Treasury arithmetic is deterministic; AI cannot mutate the ledger or protected envelopes.
- Every monthly salary is conserved to the satang and no envelope becomes negative.
- Only dated `InvestmentBudgetReleased` amounts enter the company simulation.
- Active strategy, VOO, QQQM, cash accounting, return, and drawdown use the same resolved funding schedule.
- The persisted `PaperPortfolio` remains unchanged before and after every CFO run.
- Hosted-AI challenger and stress testing are unavailable from the CFO endpoint in this slice.
- Existing fixed-monthly company simulations remain source-compatible and behavior-compatible.
- The workspace root has no `.git`; each task ends in an evidence checkpoint instead of a commit. Do not initialize a repository.

---

## File Map

- Create `src/TradingCompany.Domain/PersonalCfo.cs`: mandate, immutable monthly ledger, allocation engine, contribution contract, CFO result.
- Modify `src/TradingCompany.Domain/HistoricalCompanySimulation.cs`: optional explicit schedule and one validated funding resolver.
- Modify `src/TradingCompany.Domain/EtfDcaBenchmark.cs`: consume the resolved schedule.
- Modify `src/TradingCompany.Domain/ExecutablePassiveBenchmark.cs`: consume the resolved schedule.
- Create `src/TradingCompany.Application/RunPersonalCfoSimulationCommand.cs`: CFO application command.
- Modify `src/TradingCompany.Application/RunCompanySimulationCommand.cs`: optional explicit contribution schedule.
- Create `src/TradingCompany.Application.Handlers/RunPersonalCfoSimulationHandler.cs`: allocation, audit events, and nested paper replay.
- Modify `src/TradingCompany.Application.Handlers/RunCompanySimulationHandler.cs`: forward explicit schedules into loaded and cash-only results.
- Create `src/TradingCompany.Api/PersonalCfoSimulationRequest.cs`: strict transport contract.
- Modify `src/TradingCompany.Api/Program.cs`: register allocator and add guarded endpoint.
- Create `tests/TradingCompany.Domain.Tests/PersonalCfoTests.cs`: allocator and invariant tests.
- Modify `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`: explicit-funding replay tests.
- Modify `tests/TradingCompany.Domain.Tests/EtfDcaBenchmarkTests.cs`: identical benchmark funding tests.
- Create `tests/TradingCompany.Domain.Tests/PersonalCfoSimulationHandlerTests.cs`: orchestration and event tests.
- Create `tests/TradingCompany.Domain.Tests/PersonalCfoSimulationApiTests.cs`: HTTP, concurrency, failure, and isolation tests.
- Modify `apps/trading-company-web/app/types.ts`: CFO response contracts.
- Modify `apps/trading-company-web/app/types.contract.test.ts`: compile-time response fixture.
- Modify `apps/trading-company-web/app/page.tsx`: CFO state, action, and Operations panel.
- Modify `apps/trading-company-web/app/globals.css`: responsive CFO form, summaries, and ledger.
- Modify `README.md`: sandbox workflow and safety boundary.

---

### Task 1: Deterministic Treasury ledger

**Files:**
- Create: `src/TradingCompany.Domain/PersonalCfo.cs`
- Create: `tests/TradingCompany.Domain.Tests/PersonalCfoTests.cs`

**Responsibility:** Convert a mandate into an immutable, reconciled monthly ledger. This layer has no market data, HTTP, AI, or portfolio access.

- [ ] **Step 1: Write failing allocation and guard tests**

Add focused tests with these exact behaviors:

```csharp
[Fact]
public void Demo_mandate_conserves_every_month_and_releases_only_investment()
{
    var result = new PersonalCfoAllocationEngine().Run(DemoMandate());

    Assert.Equal(24, result.Months.Count);
    Assert.All(result.Months, month =>
        Assert.Equal(month.Salary,
            month.Essentials + month.Debt + month.Emergency + month.Investment + month.Discretionary));
    Assert.Equal(result.Months.Sum(x => x.Investment), result.InvestmentSchedule.Sum(x => x.Amount));
    Assert.Equal(72_000m, result.EmergencyBalance);
}

[Fact]
public void Underfunded_fixed_needs_release_zero_investment()
{
    var mandate = DemoMandate() with { MonthlySalary = 10_000m, FixedEssentials = 12_000m };
    var result = new PersonalCfoAllocationEngine().Run(mandate);

    Assert.All(result.Months, month =>
    {
        Assert.Equal(TreasuryAllocationStatus.Deficit, month.Status);
        Assert.Equal(0m, month.Emergency);
        Assert.Equal(0m, month.Investment);
        Assert.Equal(0m, month.Discretionary);
    });
    Assert.Empty(result.InvestmentSchedule);
}

[Fact] public void Emergency_contribution_stops_exactly_at_target_and_rolls_forward_to_later_envelopes();
[Fact] public void Debt_is_funded_before_emergency_or_investment();
[Fact] public void Repeated_runs_return_equal_ledgers();
[Theory] public void Invalid_money_dates_and_rates_are_rejected();
```

Use `DemoMandate()` with 2024-01-01 through 2025-12-31, salary 30,000, essentials 12,000, debt 0, emergency target 72,000, emergency rate `.25m`, and investing rate `.70m`.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests\TradingCompany.Domain.Tests\TradingCompany.Domain.Tests.csproj --filter PersonalCfoTests
```

Expected: compilation fails because the CFO domain types do not exist.

- [ ] **Step 3: Add immutable contracts and computed totals**

```csharp
public enum TreasuryAllocationStatus { Funded, Deficit }

public sealed record PersonalCfoMandate(
    DateOnly Start, DateOnly End, decimal MonthlySalary,
    decimal FixedEssentials, decimal FixedDebtPayment,
    decimal EmergencyTarget, decimal EmergencyRate, decimal InvestingRate);

public sealed record MonthlyTreasuryAllocation(
    DateOnly SalaryDate, decimal Salary, decimal Essentials, decimal Debt,
    decimal Emergency, decimal Investment, decimal Discretionary,
    decimal EmergencyBalance, TreasuryAllocationStatus Status);

public sealed record ScheduledContribution(DateOnly Date, decimal Amount);

public sealed record PersonalCfoLedger(
    PersonalCfoMandate Mandate,
    IReadOnlyList<MonthlyTreasuryAllocation> Months,
    IReadOnlyList<ScheduledContribution> InvestmentSchedule)
{
    public decimal TotalSalary => Months.Sum(x => x.Salary);
    public decimal TotalEssentials => Months.Sum(x => x.Essentials);
    public decimal TotalDebt => Months.Sum(x => x.Debt);
    public decimal EmergencyBalance => Months.LastOrDefault()?.EmergencyBalance ?? 0m;
    public decimal TotalInvestmentReleased => Months.Sum(x => x.Investment);
    public decimal TotalDiscretionary => Months.Sum(x => x.Discretionary);
}
```

Add `PersonalCfoSimulationResult(PersonalCfoLedger Treasury, string InvestmentReplayStatus, CompanySimulationResult? CompanyReplay)` with `CombinedClosingWealth = Treasury.EmergencyBalance + (CompanyReplay?.FinalEquity ?? 0m)`.

- [ ] **Step 4: Implement the pure allocation engine**

`PersonalCfoAllocationEngine.Run` must:

1. Validate `Start <= End`, positive salary, non-negative amounts, rates in `[0,1]`, and at most 60 included first-of-month salary dates.
2. Iterate the first day of each month from the start month through the end month.
3. Allocate essentials, then debt, each with `Math.Min(remaining, fixedAmount)`.
4. If either fixed envelope is short, mark `Deficit`; leave emergency, investing, and discretionary at zero. The partial fixed allocation consumes all salary, preserving conservation.
5. Otherwise set emergency to `Math.Min(decimal.Round(remaining * rate, 2, MidpointRounding.AwayFromZero), targetGap)`.
6. Set investment to `decimal.Round((remaining - emergency) * investingRate, 2, MidpointRounding.AwayFromZero)`.
7. Set discretionary to the exact subtraction remainder.
8. Validate non-negative envelopes, exact monthly conservation, monotonic emergency balance, and schedule/ledger equality before returning.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all `PersonalCfoTests` pass deterministically.

---

### Task 2: One explicit funding schedule for every replay

**Files:**
- Modify: `src/TradingCompany.Domain/HistoricalCompanySimulation.cs`
- Modify: `src/TradingCompany.Domain/EtfDcaBenchmark.cs`
- Modify: `src/TradingCompany.Domain/ExecutablePassiveBenchmark.cs`
- Modify: `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`
- Modify: `tests/TradingCompany.Domain.Tests/EtfDcaBenchmarkTests.cs`

**Responsibility:** Preserve old fixed-monthly behavior while allowing Treasury's irregular dated releases to fund all replay consumers identically.

- [ ] **Step 1: Write failing schedule tests**

Add tests proving:

```csharp
[Fact]
public void Explicit_schedule_is_applied_on_first_simulated_date_on_or_after_release()
{
    var schedule = new[]
    {
        new ScheduledContribution(new(2024, 1, 1), 9_450m),
        new ScheduledContribution(new(2024, 2, 3), 9_450m)
    };
    var flows = CompanySimulationFunding.Resolve(
        new(2024, 1, 1), new(2024, 2, 29), 9_450m, 0m, schedule);

    Assert.Collection(flows,
        first => Assert.Equal(new DateOnly(2024, 1, 1), first.Date),
        second => Assert.Equal(new DateOnly(2024, 2, 3), second.Date));
    Assert.Equal(18_900m, flows[^1].CumulativeContributed);
}

[Fact] public void Explicit_schedule_rejects_duplicate_out_of_range_nonpositive_or_unordered_entries();
[Fact] public void Fixed_monthly_input_keeps_the_existing_cash_flow_dates_and_totals();
[Fact] public void Replay_equity_curve_credits_each_explicit_release_once();
[Fact] public void Active_company_and_each_etf_benchmark_report_the_same_total_contributed();
```

Include a weekend release case in the engine test: a scheduled Saturday amount is credited once on Saturday to cash accounting; the next tradable bar may invest it, but no earlier bar can consume it.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests\TradingCompany.Domain.Tests\TradingCompany.Domain.Tests.csproj --filter "HistoricalCompanySimulationTests|EtfDcaBenchmarkTests"
```

Expected: compilation fails because explicit schedules and `Resolve` are absent.

- [ ] **Step 3: Extend input without breaking callers**

Append the optional property:

```csharp
public sealed record CompanySimulationInput(
    DateOnly Start,
    DateOnly End,
    decimal InitialCapital,
    IReadOnlyDictionary<Symbol, IReadOnlyList<HistoricalBar>> Histories,
    StrategyCostModel Costs,
    decimal MonthlyContribution = 0m,
    IReadOnlyList<ScheduledContribution>? ContributionSchedule = null);
```

- [ ] **Step 4: Add one validated resolver**

```csharp
public static IReadOnlyList<SimulationCashFlow> Resolve(
    DateOnly start, DateOnly end, decimal initialCapital,
    decimal monthlyContribution,
    IReadOnlyList<ScheduledContribution>? contributionSchedule)
```

When the explicit schedule is null, delegate to the existing `Schedule`. Otherwise require a non-empty list, strictly ascending unique dates, positive amounts, all dates within the range, the first date equal to `start`, and the first amount equal to `initialCapital`. Build cumulative values only after all entries pass validation.

- [ ] **Step 5: Route every consumer through `Resolve`**

Replace all four production calls to `CompanySimulationFunding.Schedule` in:

- `HistoricalCompanySimulationEngine.RunCoreAsync`
- `EtfDcaBenchmark.Calculate`
- `ExecutablePassiveBenchmark.Calculate`
- `RunCompanySimulationHandler.CashResult` in Task 3

Pass `input.ContributionSchedule` or `command.ContributionSchedule` as appropriate. Keep `MonthlyContribution` at zero for explicit schedules so the serialized result cannot imply fixed monthly funding.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: old fixed-monthly tests and new explicit-schedule tests pass.

---

### Task 3: CFO application orchestration and audit events

**Files:**
- Create: `src/TradingCompany.Application/RunPersonalCfoSimulationCommand.cs`
- Modify: `src/TradingCompany.Application/RunCompanySimulationCommand.cs`
- Create: `src/TradingCompany.Application.Handlers/RunPersonalCfoSimulationHandler.cs`
- Modify: `src/TradingCompany.Application.Handlers/RunCompanySimulationHandler.cs`
- Create: `tests/TradingCompany.Domain.Tests/PersonalCfoSimulationHandlerTests.cs`

**Responsibility:** Run allocation first, publish truthful events, then invoke the existing paper replay only with released investment money.

- [ ] **Step 1: Write failing handler tests**

Use fake `ISender`/historical provider and a recording `IRealtimeEventPublisher`. Assert:

- funded mandate sends one `RunCompanySimulationCommand` whose start/capital/schedule equal the first and all Treasury releases;
- deficit mandate sends no company command and returns `NO_INVESTMENT_BUDGET`;
- `RunInvestmentReplay = false` returns `NOT_REQUESTED` and still returns the reconciled Treasury ledger;
- normal event order starts with `PersonalCfoSimulationStarted`, contains 24 `SalaryReceived`, matching allocation/release events, and ends with `PersonalCfoSimulationCompleted`;
- a deficit month emits `TreasuryDeficitDetected` and no release for that month;
- total market-data unavailability preserves Treasury and returns `MARKET_DATA_UNAVAILABLE` without fabricated company equity;
- accounting failure emits a failed Controller event and returns no partial result.

- [ ] **Step 2: Run handler tests and verify RED**

Run:

```powershell
dotnet test tests\TradingCompany.Domain.Tests\TradingCompany.Domain.Tests.csproj --filter PersonalCfoSimulationHandlerTests
```

Expected: compilation fails because the command and handler are absent.

- [ ] **Step 3: Add application commands**

```csharp
public sealed record RunPersonalCfoSimulationCommand(
    PersonalCfoMandate Mandate,
    IReadOnlyList<Symbol> Universe,
    bool RunInvestmentReplay = true,
    Action<DateOnly>? ProgressCallback = null)
    : ICommand<PersonalCfoSimulationResult>;
```

Append `IReadOnlyList<ScheduledContribution>? ContributionSchedule = null` to `RunCompanySimulationCommand`. Existing positional callers remain valid because it is last and optional.

- [ ] **Step 4: Implement CFO handler**

Inject `PersonalCfoAllocationEngine`, `ISender`, and `IRealtimeEventPublisher`.

```csharp
var treasury = allocator.Run(command.Mandate);
if (!command.RunInvestmentReplay)
    return new(treasury, "NOT_REQUESTED", null);
if (treasury.InvestmentSchedule.Count == 0)
    return new(treasury, "NO_INVESTMENT_BUDGET", null);

var first = treasury.InvestmentSchedule[0];
var replay = await sender.Send(new RunCompanySimulationCommand(
    first.Date,
    command.Mandate.End,
    first.Amount,
    command.Universe,
    command.ProgressCallback,
    0m,
    false,
    false,
    treasury.InvestmentSchedule), cancellationToken);
```

If distinct skipped symbols cover the complete requested universe, return Treasury with `MARKET_DATA_UNAVAILABLE` and no company replay. Otherwise return `COMPLETED`. Publish each event with a single correlation ID and existing `AgentRuntimeEventPayload` roles/locations:

- Treasury / `FinanceOffice`: started, salary received, allocation, release.
- Controller / `FinanceOffice`: deficit and reconciliation failure.
- Audit / `AuditOffice`: completed.

Do not publish a completed event until combined reconciliation passes.

- [ ] **Step 5: Forward explicit schedules in the existing company handler**

Pass `command.ContributionSchedule` to `CompanySimulationInput` in `RunLoaded`. In `CashResult`, call `Resolve`, start cash at the first resolved amount, and credit later flows exactly once. Preserve current provider-failure handling and fixed-monthly behavior.

- [ ] **Step 6: Run handler tests and verify GREEN**

Run the Step 2 command, then:

```powershell
dotnet test tests\TradingCompany.Domain.Tests\TradingCompany.Domain.Tests.csproj --filter CompanySimulationHandlerTests
```

Expected: new orchestration and existing company handler tests pass.

---

### Task 4: Strict guarded Personal CFO API

**Files:**
- Create: `src/TradingCompany.Api/PersonalCfoSimulationRequest.cs`
- Modify: `src/TradingCompany.Api/Program.cs`
- Create: `tests/TradingCompany.Domain.Tests/PersonalCfoSimulationApiTests.cs`

**Responsibility:** Expose safe demo defaults, strict validation, shared simulation concurrency, and serialized evidence without changing portfolio state.

- [ ] **Step 1: Write failing API tests**

Add a `WebApplicationFactory<Program>` harness following `CompanySimulationApiTests`. Cover:

```csharp
[Fact] public async Task Empty_request_uses_approved_24_month_demo_defaults();
[Fact] public async Task Unknown_fields_return_400();
[Theory] public async Task Negative_money_invalid_rates_and_bad_dates_return_400();
[Fact] public async Task More_than_60_salary_cycles_returns_400();
[Fact] public async Task Future_end_date_returns_400();
[Fact] public async Task Cfo_and_company_replays_share_one_409_concurrency_gate();
[Fact] public async Task Market_failure_returns_treasury_with_unavailable_replay();
[Fact] public async Task Cancellation_releases_the_simulation_lease();
[Fact] public async Task Portfolio_snapshot_is_identical_before_and_after_cfo_run();
```

- [ ] **Step 2: Run API tests and verify RED**

Run:

```powershell
dotnet test tests\TradingCompany.Domain.Tests\TradingCompany.Domain.Tests.csproj --filter PersonalCfoSimulationApiTests
```

Expected: endpoint requests return 404 and CFO request types are absent.

- [ ] **Step 3: Add strict request contract**

```csharp
public sealed record PersonalCfoSimulationRequest(
    DateOnly? Start,
    DateOnly? End,
    decimal? MonthlySalary,
    decimal? FixedEssentials,
    decimal? FixedDebtPayment,
    decimal? EmergencyTarget,
    decimal? EmergencyRate,
    decimal? InvestingRate,
    bool RunInvestmentReplay = true)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnrecognizedProperties { get; init; }
}
```

- [ ] **Step 4: Register and map the endpoint**

Register `PersonalCfoAllocationEngine` as a singleton. Add `POST /simulations/personal-cfo` with defaults from the spec. Reject unknown fields, non-positive salary, negative money, rates outside `[0,1]`, `end < start`, future `end`, insufficient warmup, and more than 60 salary months.

Use the existing `CompanySimulationGate.TryEnter(start, end)`, configured company universe, and lease progress callback. Send only `RunPersonalCfoSimulationCommand`; do not expose exit challenger, stress test, live mode, or risk-bypass fields.

Return:

- 200 for a reconciled Treasury result, including `MARKET_DATA_UNAVAILABLE`;
- 400 for request/domain validation;
- 409 while either replay endpoint owns the shared lease;
- 500 for an unexpected accounting or orchestration failure, after logging server-side without leaking credentials or raw provider errors.

- [ ] **Step 5: Run API tests and verify GREEN**

Run the Step 2 command. Expected: all CFO API tests pass and `/portfolio` remains byte-for-byte equivalent across a run.

---

### Task 5: Operations UI for salary-to-portfolio evidence

**Files:**
- Modify: `apps/trading-company-web/app/types.ts`
- Modify: `apps/trading-company-web/app/types.contract.test.ts`
- Modify: `apps/trading-company-web/app/page.tsx`
- Modify: `apps/trading-company-web/app/globals.css`

**Responsibility:** Let a non-technical user edit the demo mandate, run it once, and understand protected money versus invested money.

- [ ] **Step 1: Add a failing compile-time response fixture**

Import `PersonalCfoSimulationResult` and construct a complete fixture with one funded month, one schedule entry, `investmentReplayStatus: "COMPLETED"`, a company replay, and `combinedClosingWealth`. Include `TreasuryAllocationStatus` as both string and numeric forms because ASP.NET enum serialization may be numeric.

- [ ] **Step 2: Run typecheck and verify RED**

Run:

```powershell
npm --prefix apps\trading-company-web run typecheck
```

Expected: missing CFO TypeScript contracts.

- [ ] **Step 3: Add frontend contracts**

```typescript
export type TreasuryAllocationStatus = "Funded" | "Deficit" | 0 | 1;
export type MonthlyTreasuryAllocation = {
  salaryDate: string; salary: number; essentials: number; debt: number;
  emergency: number; investment: number; discretionary: number;
  emergencyBalance: number; status: TreasuryAllocationStatus;
};
export type PersonalCfoLedger = {
  mandate: PersonalCfoMandate;
  months: MonthlyTreasuryAllocation[];
  investmentSchedule: { date: string; amount: number }[];
  totalSalary: number; totalEssentials: number; totalDebt: number;
  emergencyBalance: number; totalInvestmentReleased: number; totalDiscretionary: number;
};
export type PersonalCfoSimulationResult = {
  treasury: PersonalCfoLedger;
  investmentReplayStatus: string;
  companyReplay?: CompanySimulationResult;
  combinedClosingWealth: number;
};
```

Add the corresponding `PersonalCfoMandate` fields exactly matching the API names.

- [ ] **Step 4: Add state and run action**

In `page.tsx`, keep controlled defaults from the spec and add `runPersonalCfoSimulation()`. POST only the approved fields to `/simulations/personal-cfo`. Disable inputs/action while running, surface API recovery text, and refresh company/status events without writing to `/portfolio` or company control endpoints.

- [ ] **Step 5: Render `PersonalCfoPanel` above `CompanySimulationPanel`**

The panel must show:

- `PERSONAL CFO SANDBOX` and `PAPER / NO BANK CONNECTION` labels;
- editable salary, essentials, debt, emergency target, emergency %, investing %, start, and end;
- primary action `Run salary-to-portfolio simulation`;
- total salary, protected essentials/debt, emergency balance, investment released, discretionary, company equity/PnL, and combined closing wealth;
- replay state in text, including unavailable/no-budget states;
- a monthly table with date, five envelopes, emergency balance, and `Funded`/`Deficit` text;
- a note that combined wealth excludes spent essentials/debt/discretionary and is not a guaranteed return.

Use existing typography, color tokens, buttons, cards, number formatters, and responsive patterns. Do not imply AI made or guaranteed the allocation.

- [ ] **Step 6: Add responsive styles**

Desktop: compact two-row form, summary grid, horizontally stable ledger. Mobile: single-column controls/cards and a scrollable ledger with visible row labels. Ensure status meaning is not color-only and all controls have explicit labels.

- [ ] **Step 7: Verify type and production build**

Run:

```powershell
npm --prefix apps\trading-company-web run typecheck
npm --prefix apps\trading-company-web run build
```

Expected: both commands exit successfully.

- [ ] **Step 8: Perform browser visual QA**

Start the API and web app using the repository's existing development commands. In the in-app browser:

1. Run the default CFO simulation and confirm 24 visible ledger months.
2. Confirm summary totals match the response and company replay.
3. Confirm office events correspond to actual Treasury work.
4. Check desktop width near 1440px and mobile width near 390px.
5. Confirm no overlap, clipped action, unreadable table, horizontal page overflow, or color-only deficit signal.

---

### Task 6: Documentation and full acceptance gate

**Files:**
- Modify: `README.md`
- Verify: all files above

**Responsibility:** Document what the CFO sandbox proves and stop only after the complete system passes regression and safety checks.

- [ ] **Step 1: Document the user flow and safety boundary**

Add a `Personal CFO sandbox` section describing the approved defaults, allocation priority, editable inputs, output meanings, and how to run it. State clearly that it uses simulated salary and historical market data; it neither connects to a bank nor sends live orders.

- [ ] **Step 2: Scan production funding paths**

Run:

```powershell
rg -n "CompanySimulationFunding\.Schedule" src
rg -n "CompanySimulationFunding\.Resolve" src
rg -n "InvestmentBudgetReleased|simulations/personal-cfo|PersonalCfo" src apps tests
```

Expected: direct `Schedule` usage remains only inside `Resolve`; all replay consumers call `Resolve`; CFO contracts/events/UI/tests are present.

- [ ] **Step 3: Run the complete backend suite**

Run:

```powershell
dotnet test tests\TradingCompany.Domain.Tests\TradingCompany.Domain.Tests.csproj
```

Expected: every existing and new test passes with zero failures.

- [ ] **Step 4: Run complete frontend gates**

Run:

```powershell
npm --prefix apps\trading-company-web run typecheck
npm --prefix apps\trading-company-web run build
```

Expected: typecheck and production build pass.

- [ ] **Step 5: Run final accounting acceptance**

For the default 24-month response assert and report:

```text
total salary
= essentials + debt + emergency + investment released + discretionary

combined closing wealth
= emergency balance + company final equity

company total contributed
= sum of InvestmentBudgetReleased amounts
= VOO total contributed
= QQQM total contributed
```

Also record that `lookahead.verified` is true, trading mode is `paper`, no envelope is negative, no deficit month released investment, and `/portfolio` is unchanged.

- [ ] **Step 6: Final implementation review**

Inspect the actual diff/file changes directly. Verify no bank/broker credential path, no live execution mode, no AI ledger mutation, no duplicate funding implementation, no swallowed accounting error, and no UI number derived independently from the API result. Re-run any focused gate affected by review fixes.
