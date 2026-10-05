# Quantara Personal CFO Sandbox Design

Date: 2026-09-01  
Status: Approved by user on 2026-09-02

## Approved amendment: CFO Safety Gate (2026-09-07)

This amendment supersedes the original unconditional investing allocation and
combined-wealth display described below. The existing replay and risk authorities
remain in place.

- Required monthly expenses are essentials plus housing/debt payments.
- Evaluate each month's opening reserve: below one month releases no investment;
  one to under three months releases 50% of the planned budget; three months or
  more releases the full configured budget. Deficit always releases zero.
- Effective emergency target is at least three months of required expenses.
- First calculate the original base reserve contribution and planned investment.
  Apply the gate to planned investment; redirect withheld money to reserves up to
  the effective target and put any remainder in discretionary. Upgrades apply on
  the next month. Never take money out of existing reserves to invest.
- Optional initialEmergencyBalance defaults to zero, is non-negative, and is
  existing savings rather than salary or a contribution to the trading company.
- Report opening/closing reserve coverage, gap to three months, gate decisions,
  redirected amounts, and Controller events tied to actual allocations.
- FULL_BUDGET is funding permission, not forced trading or a change to RiskEngine.
- Display liquid assets as reserve plus valued paper portfolio. Valuation is
  unknown when investment money was released but replay is absent. Keep legacy
  combinedClosingWealth for API compatibility. Net worth remains unknown until
  property values, outstanding debts and other assets/liabilities are supplied.
- Housing payments are cash outflows, not entirely expenses; principal/interest
  and mortgage-equity modeling are not fabricated from the monthly payment alone.

## Purpose

Add a paper-only Treasury workflow that receives simulated salary, protects required money, and releases only an approved investing budget to the existing paper-trading company. This proves the end-to-end financial-management flow without bank credentials, real transfers, or live broker execution.

## Scope

The first slice simulates 24 monthly salary cycles and uses a deterministic hybrid allocation policy:

1. Fixed essential expenses.
2. Fixed minimum debt payment.
3. Emergency-fund contribution as a percentage of the remaining salary, capped at the emergency target.
4. Investing contribution as a percentage of the money remaining after the emergency contribution.
5. Discretionary allocation receives the exact rounding remainder.

All amounts are THB. User-entered values are simulation parameters, not financial advice.

## Selected approach

Treasury becomes a bounded subsystem inside the existing Quantara company. It produces a dated investing schedule consumed by the existing historical company simulation. This preserves one audit trail, one RiskEngine authority, and one UI instead of creating a second application or spreadsheet workflow.

## Demonstration defaults

The UI starts with clearly labeled demonstration values:

- Monthly salary: 30,000 THB
- Fixed essential expenses: 12,000 THB
- Fixed minimum debt payment: 0 THB
- Emergency target: 72,000 THB
- Emergency rate: 25% of salary remaining after fixed allocations
- Investing rate: 70% of the amount remaining after the emergency contribution
- Start: 2024-01-01
- End: 2025-12-31

Every value is editable before a run. Defaults are not personalized recommendations.

## Allocation rules

For each salary month, `PersonalCfoAllocationEngine` performs these steps in order:

1. Credit salary to Salary Inbox.
2. Allocate up to the fixed essential amount.
3. Allocate up to the fixed debt amount from what remains.
4. If either fixed allocation is underfunded, mark the month `DEFICIT`, allocate zero to emergency, investing, and discretionary, and leave no negative balance.
5. Otherwise calculate emergency contribution as `remaining × emergencyRate`, rounded to two decimals and capped by the emergency-target gap.
6. Calculate investing contribution as `(remaining - emergencyContribution) × investingRate`, rounded to two decimals.
7. Allocate the exact remainder to discretionary so monthly allocations equal salary exactly.

Rates must be between 0 and 1 inclusive. Monetary inputs must be non-negative. Salary must be positive. The date range must contain no more than 60 salary cycles.

## Money authority

- Essential, debt, emergency, and discretionary allocations are protected from trading.
- Only an `InvestmentBudgetReleased` entry can become a company-simulation cash flow.
- AI may later propose a mandate, but cannot edit ledger entries, release protected money, create cash, or bypass the deterministic allocator or RiskEngine.
- Any invalid, incomplete, duplicated, or non-conserving allocation stops the run. No partial investment schedule is sent to trading.

## Domain model

New domain concepts:

- `PersonalCfoMandate`: dates, salary, fixed allocations, emergency target, and rates.
- `MonthlyTreasuryAllocation`: salary date, each envelope amount, status, and cumulative emergency balance.
- `PersonalCfoLedger`: immutable ordered monthly allocations and invariant checks.
- `PersonalCfoAllocationEngine`: pure deterministic calculation with no I/O.
- `PersonalCfoSimulationResult`: Treasury totals, monthly ledger, released investment schedule, optional company replay, and combined closing wealth.

The trading engine gains an optional explicit contribution schedule at the end of `CompanySimulationInput`. Existing fixed-monthly callers remain compatible. A single funding resolver produces validated `SimulationCashFlow` values for the active company, VOO DCA, QQQM DCA, and every return calculation so all comparisons use identical money timing.

For an explicit schedule:

- Entries are positive, ordered, unique by date, and inside the simulation range.
- The first positive entry supplies initial trading capital and becomes the trading replay start; the loader still fetches the required 400-day warmup.
- Later entries are credited on the first simulated date on or after their scheduled date.
- If Treasury releases no investment money, the Personal CFO result is valid with no company replay and zero investment equity.

## Application and API

Add `POST /simulations/personal-cfo` with a strict JSON contract containing only the mandate fields and `runInvestmentReplay`.

V1 defaults:

- `runInvestmentReplay`: true
- Stress testing and the hosted AI challenger: unavailable from this endpoint in the first slice

The handler:

1. Validates the request.
2. Runs the deterministic Treasury allocation.
3. Verifies conservation and protected-envelope invariants.
4. If requested and funded, loads the same historical public market data and runs the existing paper company with the released schedule.
5. Returns Treasury and trading results together.

The endpoint uses the existing simulation lease, so company and Personal CFO replays cannot mutate or compete for the same simulation status concurrently.

## Audit events

Publish real application events, not decorative animation:

- `PersonalCfoSimulationStarted` — Treasury receives the mandate.
- `SalaryReceived` — one event per salary cycle.
- `TreasuryAllocationCompleted` — allocation totals and status.
- `TreasuryDeficitDetected` — fixed needs were underfunded; no investment released.
- `InvestmentBudgetReleased` — dated amount handed to the paper company.
- `PersonalCfoSimulationCompleted` — combined reconciliation passed.

Events use the existing SignalR channel and move the existing Treasury and Controller roles in the pixel office.

## UI

Add a `Personal CFO sandbox` section above Company Simulation in Operations view.

Inputs use plain labels and THB/percentage formatting. The primary action is `Run salary-to-portfolio simulation`. While running, controls are disabled and the existing progress language is reused.

The result shows:

- Total salary received
- Essential and debt outflows
- Emergency balance
- Total released for investing
- Discretionary total
- Company final equity and investment PnL when replayed
- Combined closing wealth: emergency balance plus company final equity
- A monthly Treasury ledger with deficit and protected-money status expressed in text, not color alone

The existing Company Simulation panel remains available as a direct fixed-contribution experiment.

## Error handling

- Validation errors return HTTP 400 with a specific recovery message.
- Concurrent replay returns HTTP 409.
- Market-data failure preserves and returns the completed Treasury ledger with investment replay marked unavailable; it never fabricates trading results.
- Cancellation stops market loading/replay and makes no persistent portfolio change.
- Accounting mismatch returns no partial result and publishes a failed Controller event.

## Testing

Deterministic domain tests cover:

- Allocation conservation to the satang for every month.
- Priority ordering and deficit behavior.
- Emergency target cap and post-target rollover.
- Zero leakage from protected envelopes to investment cash flows.
- Repeated runs producing identical ledgers.
- Explicit contribution schedules applied on the correct simulated dates.
- Active company and ETF benchmarks receiving identical dated cash flows.

API tests cover strict JSON fields, numeric/date validation, concurrency, failure behavior, and serialized results. Frontend contract tests and production type checking cover the new response. Responsive visual QA covers desktop and mobile layouts.

## Non-goals

- Bank account connection, credential storage, screen scraping, OTP handling, or real payments.
- Live broker execution or any change to V1 paper-only enforcement.
- Tax calculation, debt interest modeling, bill settlement, credit scoring, or personalized financial advice.
- AI-authored ledger mutations or automatic mandate changes.
- Persisting the sandbox result as a real financial record.

## Acceptance criteria

1. A 24-month demo run visibly traces salary through every envelope and into the paper company.
2. Monthly allocated amounts exactly equal monthly salary; balances never become negative.
3. Deficit months release zero investment money.
4. Trading receives exactly the dated `InvestmentBudgetReleased` amounts and nothing else.
5. VOO and QQQM benchmarks receive the identical released schedule, fees, slippage, FX, and adjusted price treatment.
6. The current sandbox portfolio remains unchanged.
7. UI and events label all results as paper/sandbox evidence.
8. All existing tests plus new deterministic, API, type, build, and responsive checks pass.
