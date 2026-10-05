# Forward funding acceptance

Scope: isolated cash-only monthly payroll. User specified month-end, once/month.
Salary 70000; essentials 24400; housing 14000; reserve target 115200; rates .25/.70.

Local account `34e8c881-0dd6-4957-8305-be758e52e9bf` opened
2026-09-09T21:06:20.3230742+07:00. GET and Operations UI both show
next payday 2026-09-30, zero posted salary, reserve and investment cash, zero
postings, and trading disabled. No time advancement was applied to this account.

Verification: .NET 623 passed; six UI render tests passed; TypeScript and Next
production build passed; Compose config valid. Browser screenshot confirmed a
separate Forward paper treasury panel with the zero-balance state and no
imported historical profit. Desktop inspected; no separate mobile viewport pass.

Deterministic file-backed tests cover Bangkok midnight at month-end, multiple
store instances posting concurrently, reopen/downtime, year transition and leap
February, backward clock, corruption, missing account marker protection and
salary conservation. API tests cover disabled read-only status and enabled
zero-balance initialization without changing the separate shared portfolio.

Expected first-month allocation, proven by test: essentials 24400, housing
14000, reserve 24490, investment 0, discretionary 7110. This is a fixture
expectation, not an actual September salary posting.

Funding worker is enabled in the current local backend process only. It needs
the backend running and the same persisted path after restart; deployments
remain opt-in through FORWARD_FUNDING_ENABLED. No bank or paid model calls.

Remaining: forward decision journal, proposals from the chosen AI workflow,
deterministic risk checks against this funded account, later observed-price
fills and atomic trade/account reconciliation. No AI decisions or forward
trades are fabricated by the funding worker.
