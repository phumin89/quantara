# First-market screen: US ETFs versus Bitkub majors

Status: **NO LIVE MARKET SELECTED**. This is historical paper research, not a live-trading approval or an AI-agent performance test.

## Fixed comparison

- Candidate sets: VOO + QQQ versus BTC-THB + ETH-THB. Two instruments per market; no leverage.
- The existing deterministic historical-company strategy and `RiskEngine` were used in both sets. This does not measure the eight hosted AI roles.
- Each two-year fold uses the same CFO mandate: THB 70,000 monthly salary, THB 24,400 essentials, THB 14,000 housing, THB 115,200 reserve target, 25% emergency allocation, 70% investing rate, and month-end investment releases. Each fold contributed THB 419,776 to its portfolio.
- Four periods were specified before reading results: 2018–19, 2020–21, 2022–23, 2024–25. Fees and slippage are each 0.1% per fill; the tool also reruns both at 0.2%. No broker-specific fee schedule, tax, transfer costs, AI costs, or live spreads are included.
- The source is the SHA-256-verified snapshot `6e910a4ce3f424ee4e55f6f2726ec292f15c9d114f8850930ad54e7ff29b27b5`. When the 2024–25 snapshot lacked crypto data, the tool fetched **all four** candidate histories again; it did not mix fresh and saved prices within that fold.

Normal-cost results below. Profit is final portfolio value minus contributions, **not** salary or reserve growth. Alpha compares the strategy against an executable passive benchmark in the same two instruments with the same cash flows and modeled costs. It is not cross-market alpha.

| Period | Market | Profit (THB) | Alpha versus own passive | Max drawdown | Data status |
|---|---|---:|---:|---:|---|
| 2018–19 | VOO + QQQ | 10,151.62 | -1.19 percentage points | 4.89% | Frozen |
| 2018–19 | BTC + ETH | -10,113.49 | Not available | 15.00% | Frozen; passive warmup insufficient |
| 2020–21 | VOO + QQQ | 49,373.32 | +2.94 percentage points | 3.34% | Frozen |
| 2020–21 | BTC + ETH | 157,910.16 | -28.41 percentage points | 28.11% | Frozen |
| 2022–23 | VOO + QQQ | 27,173.35 | +0.84 percentage points | 3.30% | Frozen |
| 2022–23 | BTC + ETH | 21,009.67 | -14.23 percentage points | 7.99% | Frozen |
| 2024–25 | VOO + QQQ | 3,626.65 | -3.21 percentage points | 5.09% | Fresh public history |
| 2024–25 | BTC + ETH | — | — | — | Rejected: invalid/duplicate upstream candles |

Doubling both modeled costs did not reverse the 2018–23 US-versus-crypto profit ordering. It did not cure the failed alpha/drawdown gates. In 2020–21, crypto's high profit came with 28.11% drawdown and substantially lower return than the same-asset passive benchmark; high absolute profit alone is not evidence of trading skill. The 2018–19 crypto passive benchmark had zero eligible warmup symbols, so its alpha is intentionally **not** reported.

For 2024–25 the public Bitkub history endpoint returned data, but the app's OHLC validator rejected it. A direct read found a BTC daily bar dated 2025-02-17 whose high/low did not cover open/close, plus repeated timestamps in BTC and ETH history. Do not silently clean these after seeing the result. A predeclared data-repair policy and independent source check would be required before rerunning this period.

## Decision

Neither candidate meets a four-period promotion rule of positive net return, positive alpha against its own valid passive benchmark, drawdown at most 25%, complete data, and valid lookahead/accounting in every fold. US ETFs are the stronger **research candidate** from the available comparison; this is not a Webull recommendation or approval to send orders. Crypto remains research-only. The frozen periods have been inspected in prior work, so this is not untouched out-of-sample proof.

The running forward-paper account currently supports five US instruments but no crypto orders. Its hosted AI and live broker execution have not been proven end-to-end. Account/API eligibility, true execution costs, quote-to-fill behavior, reconciliation, and a genuinely forward paper period remain separate gates before choosing a first live account.

Reproduce: `dotnet run --project tools/MarketSelectionBacktest -- src/TradingCompany.Api/data/simulation-snapshots 6e910a4ce3f424ee4e55f6f2726ec292f15c9d114f8850930ad54e7ff29b27b5`.

Verification on 2026-09-25: 708 .NET tests passed; the research tool completed and marked 2024–25 crypto `DATA_UNAVAILABLE` rather than inventing a result.
