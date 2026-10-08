# Engineering Instructions

This repository follows the engineering rules in the source instructions: clean, simple, testable code; Clean Architecture; SOLID; meaningful names; guard clauses; and deterministic tests.

## Agent communication

- Owner requested on 2026-10-07: all company agents, research/learning agents, supporting workers and scheduled follow-ups use Caveman full in clear Thai. Lead with the conclusion; keep reports to one to three short lines where feasible, aiming for at most 280 characters in summaries. Remove filler, repeated explanations and process disclaimers.
- Preserve negation, numbers, units, limits, uncertainty, critical risks and decision conditions. Keep citations, exact source quotations, JSON keys, enums, IDs and numeric proposal fields intact. Concise reporting must not reduce analysis or validation.
- Preserve historical reports as recorded. Apply the style to new work; do not spend model quota re-reading unchanged documents or repeating a completed daily learning job solely to restyle it.

## Execution safety rules

- Owner direction on 2026-10-08: owner supplies funding; CEO and team plan allocation and trades to pursue evidence-supported net profit after costs. Funding activation may restore the per-order ceiling to existing authorized capital only when the owner explicitly runs it. Models cannot increase authority or bypass RiskEngine; startup/UI updates do not change actual limits. The 100-THB trial remains separate. No claimed profitability without real execution and reproducible performance evidence.

- Product direction approved on 2026-09-27: support separate Sandbox and Live modes. Existing V1 remains paper-only until the live implementation is verified; do not describe design work as working live execution.
- Sandbox remains the default. Existing paper ledgers, simulated salary and historical results must never fund or authorize a live order.
- Owner direction on 2026-10-05 supersedes the UI default: ship the Live application only, without a Sandbox mode switch. Keep paper/backtest verification headless and opt-in; do not start scheduled Sandbox AI work with the Live app. Default Live execution remains disabled until the owner explicitly activates a verified account and limits.
- Live implementation is authorized; activation and real-money transactions are not. Require explicit owner activation, broker/account binding, positive owner-defined limits and successful reconciliation before live execution. Missing configuration fails closed.
- Keep broker credentials outside prompts, browser code, logs and model child processes. No withdrawal, bank-transfer, margin, leverage or short-selling implementation is authorized.
- Do not add Ollama/local-AI packages, configuration, HTTP clients, or runtime wiring.
- `RiskEngine` is the deterministic final authority. LLM output is untrusted and cannot mutate portfolios or bypass permissions/risk.
- Tests must be deterministic. UI state must reflect real domain events.
- Owner requested on 2026-10-01: independently review the actual changed code and run relevant checks after every implementation round before accepting it. Review responsive UI in bounded desktop/tablet/phone passes. Record findings, fixes, results and remaining Live blockers; never label a readiness screen as completed Live trading.
