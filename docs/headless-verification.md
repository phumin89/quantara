# Headless trading verification

Owner direction on 2026-10-05: the shipped application is Live only. Sandbox/backtest verification remains off the product UI and outside its normal startup. Existing paper accounts, reports and database volume are preserved, while the prior scheduled AI API, Pixel and report database are stopped.

`tools/test-trading.ps1` runs the existing deterministic checks in a disposable .NET SDK container. It copies source/test files into its temporary filesystem and removes only copied build/output/data directories there. It never mounts root `.env`, `.runtime`, Codex auth or the Docker socket. Dependencies restore from the standard NuGet registry; no model call or live trading request is made by the selected tests.

- `StrategyValidationTests`: deterministic strategy simulation, costs, sizing, signal decisions and chronological walk-forward validation with frozen bars. This already tests the backtest machinery without a UI or AI call.
- `TradingTests`: market-history parsing/validation with fake HTTP responses and deterministic risk tests.
- `ForwardCryptoCostsTests`: spread, fee, quote freshness/depth and paper fill accounting.
- Live risk/accounting/execution/reconciliation/activation tests: bounded order and cash behavior with fake broker/store evidence.
- Owner access/endpoint/proxy/remembered-browser tests: transport, authentication, expiry, restart, CSRF and separation of viewing from financial controls.

Fixture test success proves software behavior, not profitability. Investment performance requires reproducible frozen real-market datasets, fees/slippage assumptions and out-of-sample evaluation. These checks do not establish actual broker fee-credit semantics, production quantities, current account identity or successful real-money execution.

The full suite can still run through `dotnet test TradingCompany.sln` on a host where .NET execution is permitted. Its database integration checks require Docker and disposable PostgreSQL databases. Optional `tools/ForwardSandboxRehearsal --live-codex` is a separate, explicit paper rehearsal that consumes AI allowance; it is not part of the no-AI check runner or Live startup.
