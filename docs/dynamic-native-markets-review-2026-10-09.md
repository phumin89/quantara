# Dynamic native THB markets review — 2026-10-09

The prepared implementation removes the BTC/ETH/SOL restriction from native THB exchange SPOT market discovery, per-market rules, account readers and proposal validation. Execution still requires a verified bound receipt profile, explicit owner mandate, current market evidence and successful reconciliation. This work did not update the actual saved profile or limits, activate an account, call trading models, submit orders or prove real fills or profit.

## Market and evidence scope

- Canonical application symbols use an uppercase ASCII letter/digit base of 1–15 characters followed by `-THB`; `THB-THB`, lowercase, query/path text and trailing newlines are rejected. The 1,000-market payload bound is defensive input validation, not a portfolio limit or execution allowlist.
- Native market discovery accepts current active, unfrozen THB markets with `source=exchange` and `market_segment=SPOT`. Each market must satisfy the existing strict receipt-rule checks. Buy/sell units, native price steps, base precision, minimum notional and fee handling remain per-market rules rather than inferred common BTC settings.
- The Scout ranks liquid public markets and prioritizes holdings. Scout shortlists and meeting candidate evidence contain at most eight symbols to bound research cost and time. With eight or more holdings, each 15-minute scan rotates up to seven holdings and reserves one slot for the highest-ranked new eligible opportunity. With no new opportunity it rotates eight holdings. Full actual positions, balances and held quotes remain available; a completed review saves only candidate and held quotes, avoiding unrelated quote churn. This does not reduce the verified market universe to eight, limit actual holdings to eight or authorize an order. The owner's mandate and RiskEngine decide execution scope and position limits.
- Source research follows the reviewed catalog for shortlisted symbols rather than a fixed three-coin/six-document assumption. Original bytes, extracted text, content hashes and source-matching quotations remain required. Uncovered documents and missing native price history remain explicit gaps. The existing eight-finding, sixteen-source and 24 KiB research bounds remain; the meeting bound remains 60 KiB.
- Activity and saved meeting readers now accept eight distinct symbol-specific history gaps. Issue lists retain canonical symbol checks, valid issue codes, duplicate rejection and a 1,000-item bound. Browser readers use the same canonical symbol rule for research watchlists, sources and findings.

Bitkub publishes symbols and ticker as public read-only market endpoints. Its symbols response includes market identity, native precision, steps, minimum quote size and freeze flags. These contracts guide parsing; their examples do not prove authenticated settlement for every listed market. Primary reference: [official Bitkub REST V3](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md#market-data-read-only).

## Controls retained

Explicit owner activation, broker/account binding, positive owner-defined limits, successful reconciliation and deterministic RiskEngine approval remain required. Missing configuration, stale or contradictory rules, invalid receipts and unresolved execution outcomes fail closed. Models cannot expand permissions or mutate the portfolio directly.

Capital allocation, daily limits, losses, opening inventory, baselines and owner Stop remain protected. Funding activation can restore the per-order ceiling only to existing authorized capital. The separate 100-THB trial keeps its fee-inclusive ceiling and any smaller existing ceiling. No withdrawal, bank transfer, margin, leverage or short-selling path was added. Paper balances, simulated salaries and backtest profits remain outside Live authority. Broker credentials remain outside browser code, prompts and model child processes.

## Verification and corrected failures

| Evidence | Result | What it establishes |
| --- | --- | --- |
| Headless domain/application/API checks | 996 passed | Deterministic software behavior with fixtures and fake transports |
| Isolated PostgreSQL integration checks | 146 passed | Persistence, execution/reconciliation and owner-control behavior in disposable test databases |
| Complete frontend checks | 110 passed | Canonical symbols, dynamic arrays, account/plan readers, eight history gaps and saved-report rendering |
| Isolated launcher checks | 14 passed | Success/failure/rollback cases without real Docker or activation |
| Frontend typecheck and changed-file whitespace check | Passed | Type compatibility and clean changed-file formatting |
| Public-only API smoke on 2026-10-09 | 247 native markets; 247 fresh usable quotes | Actual public metadata and quote parsing, without account authentication |
| Rule expansion in that public smoke | 247 synthetic rule pairs; price scales 2–7; base scales 2 or 8 | Prepared rule construction across observed native precisions; no real profile mutation |
| Native public history after the range correction | BTC and USDT: 400 completed bars each through 2026-10-08 | Actual native THB history parsed without FX or synthetic bars |
| Native public history unavailable | XRP and DOGE rejected | Market eligibility does not make malformed or conflicting history usable |

The primary lead ran the headless, database, complete frontend and launcher suites, reviewed the actual changed code and checked synthetic source components at 1440, 768 and 390 pixels without horizontal page overflow. The frontend worker independently inspected its changed TypeScript, and a separate worker reviewed the financial diff. The public smoke used only public GETs, made zero financial requests and zero model calls, and used a synthetic constructor/profile. Market counts and usable books are observations from that run, not a permanent exchange guarantee. All order, fill, rounded-settlement and restart proofs in these automated suites use deterministic fakes; they are not authenticated real-order evidence.

Failures found during implementation were corrected before the passing runs:

- `BitkubQuoteTests.Unsupported_symbol_fails_before_network` and `BitkubLiveRuntimeTests.Unsupported_symbol_is_rejected` used `DOGE-THB` as an unsupported symbol. DOGE is canonical in the prepared native-market universe, so the tests now use malformed `DOGE-THB?injected=1` and names describing malformed input. Reject-before-network coverage remains.
- Two `BitkubExecutionFlowTests` receipt fixtures omitted `base_quantity_scale=8` and `quote_quantity_scale=2`, causing the strict native-rule recheck to fail. The fake fixtures now include the required metadata; production validation was not relaxed.
- Frontend fixtures that formerly rejected DOGE now reject `DOGE-USDT`. A separate review found the five-issue bound would discard a meeting with eight missing native histories; backend and browser bounds now agree at 1,000, with regressions for eight gaps and the 1,000/1,001 boundary. A large-position fixture also confirms computed-equity overflow is rejected.
- Independent review found receipt-mode captures still inherited the legacy canary's two-order bound. Receipt mode now permits up to 1,000 history/detail records within the unchanged 512 KiB byte cap; canary mode retains 20 fills/two orders. Three native-market history and count/byte boundary regressions pass. No capture is silently truncated.
- Independent review found the 200ms batch gap made two large catalogs intrinsically exceed the 30-second evidence window. Eight concurrent scans now use a 100ms inter-batch gap, and additional history pages also wait 100ms. Deterministic virtual-timer tests prove pacing and cancellation. Slow network or excessive history still fails closed within existing deadlines; timestamps are not refreshed to conceal age.
- Public native charts include prior-day bars before the requested start. The provider now selects only the requested completed date range before applying its unchanged 200-bar, positive OHLC and ordered-unique-day checks. XRP's 2026-05-02 timestamp `1777680000` had two conflicting OHLC rows; it remains rejected. DOGE also failed the strict historical validation. No conflicting candle was selected, merged or invented.
- UI history-gap messages now name the failure once and retain every affected native symbol. Large portfolios rotate held evidence and keep a new-opportunity slot; deterministic tests register a new authorized plan with 8 or 12 existing holdings without changing the owner's limits.

## Owner handoff and remaining Live proof

The owner intentionally runs the existing `Enable Auto Trading.cmd` when choosing to apply the prepared market scope. It performs isolated checks, verifies current native rules, backs up the saved bound receipt profile and updates rules/mandate through the existing owner controls. Existing Stop and risk limits remain effective; reconciliation must succeed before execution. Installing code, ordinary startup and `Quantara.cmd` do not change the actual saved profile or financial limits.

All three release images were built. Only `owner-web` was replaced; HTTPS `/` returned 200. The running financial owner and ledger were not restarted, and the company stayed stopped. Current financial permissions, broker profile and actual mandate remain unchanged until the owner's explicit activation command.

A public quote, synthetic receipt, successful test or activated permission is not a real trade. An authenticated application order receipt, exact real-account reconciliation and durable recovery evidence remain necessary to establish real execution. No real fill, trading performance or net-profit claim is made by this review. Historical review documents retain the scope and observations recorded at their original dates.
