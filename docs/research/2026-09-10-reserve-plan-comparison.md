# Month-end payroll: reserve-first comparison

Historical research only, January 2024–December 2025. Same 70000 salary,
24400 essentials, 14000 housing and 115200 reserve target in both plans.
Both receive salary at calendar month-end; these figures are not directly
comparable to prior month-start CFO results.

| Metric | Existing CFO | Reserve-first 25000 |
|---|---:|---:|
| Reserve target reached | 2024-07-31 | 2024-05-31 |
| First investment release | 2024-03-31 | 2024-05-31 |
| Investment contributions | 419776.00 | 426020.00 |
| Portfolio close | 546505.69 | 556809.87 |
| Investment profit | 126729.69 | 130789.87 |
| Separate reserve | 115200.00 | 115200.00 |
| Portfolio plus reserve | 661705.69 | 672009.87 |
| Discretionary allocations, assumed spent | 223424.00 | 217180.00 |
| Contribution-adjusted drawdown | 20.41% | 20.51% |
| Paper trades | 39 | 40 |

Closing liquid assets differ by 10304.19: 6244.00 additional contributions
and 4060.19 additional investment profit. Contributions are not profit.
This is one observed historical period, not evidence that reserve-first will
always outperform. It achieves its reserve target two months earlier.

Reserve-first implementation: save up to 25000 per month, capped at the target.
No investment while the reserve remains below target. In May, reserve receives
15200 to reach 115200. Remaining 16400 is subject to the existing 70% investment
rate and 50% opening-reserve gate, releasing 5740; the remainder is discretionary.
From June the investment release is 22120/month. This final partial-month rule
is an explicit research assumption; it is not an exact fixed-6600 spending plan
for every month. No application funding mandate was changed.

Source bundle: `6e910a4ce3f424ee4e55f6f2726ec292f15c9d114f8850930ad54e7ff29b27b5`.
The store verifies its SHA-256 before reading. No market or AI calls are made.
Existing engine, 0.1% fee and 0.1% slippage assumptions, historical THB prices.
Both outputs reconcile scheduled contributions and pass the engine's cutoff
audit with zero violations. Current fixed-universe selection, provider/data
exclusions and adjusted-price limitations remain. Data has been inspected in
prior research: this is NOT untouched out-of-sample validation. Taxes and AI
costs are not included. No strategy thresholds were tuned for this run.

Reproduction command:
`dotnet run --project tools/ReservePlanBacktest -- src/TradingCompany.Api/data/simulation-snapshots 6e910a4ce3f424ee4e55f6f2726ec292f15c9d114f8850930ad54e7ff29b27b5`

The companion JSON is a report (JSON numeric values parsed as floating point),
not a replacement for the original decimal-precision frozen input bundle.
