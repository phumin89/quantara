# Funding-only CEO review — 2026-10-08

## Problem and resulting behavior

Owner supplies funding and wants the company to manage allocation for net profit. The preceding 100-THB trial left a small per-order ceiling in the actual account. Separately, the team reader presented an old advisory round as if execution were currently disabled.

The 13:00 saved round began at 12:59 Bangkok, before owner activation at 13:22. A read-only check confirmed Armed=true, reconciliation current, a 100-THB order ceiling and no application orders. Missing SOL history was a symbol-specific issue, not evidence that every market was blocked. The current hour's saved decision is not replayed after activation; the next eligible meeting must evaluate fresh evidence.

The regular `Enable Auto Trading.cmd` now runs isolated software checks before the existing owner activation path. Its explicit full-funding command restores only the per-order ceiling to the persisted authorized capital allocation. It does not increase allocation, daily limits or other constraints; does not clear owner Stop or reset opening inventory, baselines or losses. Pending orders block the mandate change. Repeated funding activation is idempotent. The separate 100-THB trial still retains an existing smaller ceiling.

Live meeting instructions now state the owner's funding role and delegated portfolio planning. The objective is evidence-supported net profit after fees, spread and slippage, comparing BUY/HOLD/TRIM/SELL. No fabricated confidence, compulsory trade or permission expansion is allowed. Models give decisions and reasons; actual dispatch/fill outcomes remain the verified UI's responsibility. This is a planning instruction, not evidence of improved returns or forecasting. Existing validation, RiskEngine and BTC/ETH/SOL execution scope are retained.

Saved advisory outcomes now say “รอบนี้วิเคราะห์เท่านั้น · ไม่ได้ส่งคำสั่ง”. Graph issues explicitly refer to that round and its symbol. Raw historical reports were not rewritten. Both legacy and current report envelopes remain readable.

## Independent review and verification

The lead inspected the actual financial flow and UI diff. The bounded UI worker's local bridge was unavailable, so its prescribed fallback changed only the report component and tests. The lead added legacy READ_ONLY coverage and reviewed it directly. No new dependency or provider was introduced.

- Deterministic headless checks: **837 passed**, zero failures/skips. Includes the Live net-profit planning instruction and unchanged capability restrictions.
- Isolated PostgreSQL integration: **112 passed**, zero failures/skips. Includes restoring the trial cap to funded capital, complete mandate preservation apart from cap/version, baseline/loss/activation preservation, idempotence, conflicting modes rejected before private reads, and Stop/pending-order preservation in both modes. Broker transports were fixtures; no production credential was mounted.
- Frontend: **98 passed**, TypeScript check passed and production image built. The Impeccable detector returned no findings.
- Launcher verification: **4 harmless CMD cases plus 14 isolated PowerShell cases passed**. Failed software checks never reached activation. These calls used stubs, not the real financial launcher.
- Actual remembered-browser review at **1440×1000, 768×1000, 390×844**: no horizontal overflow, new wording visible and old saved conclusions retained. Temporary viewport overrides were reset. Screenshots are private ignored `work/funding-company-*.png`.
- Owner and company release images were built. Only the frontend service was replaced; the existing armed owner/company services were not restarted. Current account authority and order ceiling were not changed by this work. The owner must initiate the full-funding activation if choosing that policy.

Ignored logs are `work/funding-company-*.log`. Software acceptance does not prove a real app order, durable broker recovery or profitability. No activation, broker placement/cancellation, real model call, ledger editing or volume deletion was performed by this review. Existing jobs and cached learning/research were not rerun to restyle history.

## Handoff

To replace the trial ceiling with funded sizing, the owner runs `Enable Auto Trading.cmd` once while Docker is running. It verifies, reconciles and starts the new isolated company after owner activation. CEO can then plan within funded capital and the remaining actual limits; HOLD remains valid. Ordinary viewing continues through `Quantara.cmd`. A real authenticated receipt and exact reconciliation are still needed to prove real execution; financial activation alone is not that proof.
