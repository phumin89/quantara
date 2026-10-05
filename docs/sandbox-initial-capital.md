# Sandbox initial capital

Owner approved 100,000 THB of simulated initial investment capital on 2026-10-01. This is not salary, an emergency-reserve transfer, a backtest gain or real broker money.

The existing launcher now defaults to this Sandbox amount. Use `start-sandbox.ps1 -Action Restart -InitialPaperCapital 100000` when replacing a running API; `Start` reuses an already-running healthy API and does not change its settings.

The API's `ForwardFunding:InitialPaperCapital` setting otherwise defaults to zero. Initial capital is recorded once as an `INITIAL_CAPITAL` event in the existing append-only paper journal. Matching restart/repeated startup requests do not add another event, and the amount cannot be changed after initialization. A portfolio that has already placed paper orders cannot be retroactively initialized. The old journal and treasury identities remain unchanged.

Salary allocations still pass the existing CFO reserve gate. Only salary-credit events reconcile against those releases; the independently approved initial capital is included in paper cash, contributions, equity and risk sizing, not in salary, reserve or profit. The funding API reports `portfolio.initialCapital` separately. AI uses the same daily meeting and deterministic paper-execution gates; funding does not force a purchase.

Setting the startup amount back to zero does not delete already-recorded capital. An older API that does not understand the new event must not be used to rewrite this journal; it will fail validation. Existing journals without the new event remain readable with their original checksums. Preserve the journal on rollback rather than deleting/resetting it.

Verification: new seeding behavior first failed six checks; a captured-time startup race was separately reproduced and fixed without adding duplicate money. Full .NET solution passed 1,029 main tests and 38 PostgreSQL tests, zero skips. Logs and runtime pre-seed backups are in `work/sandbox-initial-capital/`. No Live services, credentials, activation or real-money orders were changed.

Runtime follow-up on 2026-10-01: the Sandbox launcher successfully restarted the Docker web/office and host API. Read-only API checks confirmed initial capital, cash, contributions and equity of 100,000 THB, P&L zero, and no positions, pending orders or receipts. The company cycle reached `COMPLETED`; the web returned HTTP 200. This proves simulated funding and a completed cycle, not a broker fill or Live readiness.
