# Quantara

Quantara is the owner's Live trading application. A persistent sidebar connects Office, Account/assets and Trading pages without reloading the app. The colored Pixel Office stays on the homepage; a floating team room opens with the current system conclusion and reasons. It does not invent team activity or import paper reports into Live. Historical paper data is retained; strategy/backtest and accounting verification run headlessly and only when requested.

Authenticated Bitkub wallet viewing works. Execution requires explicit owner activation of a verified account and mandate; the current authority and reconciliation state are shown on Trading. Wallet viewing and installing new code do not approve orders or expand an existing mandate.

## Start the app

Start Docker Desktop, then double-click `Quantara.cmd`. It opens the app and reuses an already-running installation. Alternatively:

```powershell
.\tools\start-live-readonly.ps1
```

Open [Quantara](https://localhost:3000). The launcher requires the existing trusted localhost certificate and ports 3000/7443 either free or occupied by the verified owned service. It starts only the owner service and internal frontend. Both restart with Docker; closing the terminal does not stop the app. Use `-Rebuild` on the script after source changes. Existing TLS files and remembered viewing remain intact during normal startup.

From Docker Desktop, click **2080:2080** on the `owner` service. Its loopback-only HTTP entry redirects to `https://localhost:3000`, preserving the existing browser identity. Port 2080 must also be available. Ports 3000 and 7443 require HTTPS; Docker's HTTP/IP links for those ports are not launch links. The HTTP entry serves only redirects for the three public pages; owner data and actions remain on authenticated HTTPS.

Pair once in Account or Trading using the local code window. Select **จำเบราว์เซอร์นี้สำหรับดูบัญชี 30 วัน** to retain viewing access across browser closures and service restarts. Navigation and normal startup do not ask for a new code. Without that choice, access lasts eight hours and is lost on restart. Logging out forgets the browser. A fresh local code is available through `tools/pair-live-owner.ps1`; issuing it revokes the prior session, so request one only when needed.

The owner-entered Bitkub key and secret stay in the ignored project `.env`, mounted read-only only in the owner service. Never paste them into chat. Remembered viewing is encrypted, revocable and separate from financial control access. Installing, pairing, remembering, restarting or opening the app does not activate Live trading. Pixel artwork is bundled with the frontend; it needs no additional Pixel server or scheduled AI process.

## Headless verification

```powershell
.\tools\test-trading.ps1
.\tools\test-live-integration.ps1
cd apps/trading-company-web
node --test tests/*.test.cjs
npm run build
```

The Docker verification runners mount source/test files, not broker credentials, Codex sign-in or runtime secrets. The database runner additionally uses Docker's socket to create disposable test PostgreSQL containers on a unique test network. Both fail if no tests run. They run deterministic strategy, historical-data parsing, costs, risk, execution/reconciliation and owner-access checks using fixtures and fake transports. They do not start the application, consume AI allowance or submit orders. See [verification scope](docs/headless-verification.md).

## Remaining Live gates

The execution, opening inventory, daily baseline, isolated company and reconciliation paths are implemented and tested. The receipt path supports the verified native THB exchange SPOT universe through saved per-market rules, using authenticated private order receipts containing explicit executed amounts, received coins and wallet fees. Its symbol validation no longer restricts markets to BTC, ETH and SOL. A public listing alone grants no execution permission: the owner's bound receipt profile, mandate, current market rules and reconciliation must all agree. A manual sell or fee-credit exhaustion is not required. Actual supported opening assets are imported once without inventing application trades or profits. See [the current native-market review](docs/dynamic-native-markets-review-2026-10-09.md) and [the operator steps](docs/bitkub-small-capital-proof.md).

The owner intentionally opens `Enable Auto Trading.cmd` to update saved bound market rules and the mandate. After isolated software checks, it verifies the current native THB exchange SPOT markets, backs up the existing receipt profile, and approves the verified universe through the existing owner activation controls. Funding mode restores the per-order ceiling to the existing authorized capital allocation and expands a legacy one-position mandate to the number of verified Spot markets, preserving a larger saved position ceiling. CEO chooses allocation within these limits. Capital allocation, daily limits, opening balance, losses and owner Stop remain unchanged. Installing code or ordinary startup does not modify the actual saved profile or limits. After reconciliation it starts the isolated company: full eight-role research after 09:00 Bangkok, and three-role portfolio reviews when owned money/quantities change or bid/ask moves at least 1%, at most once per 15-minute Bangkok slot when the executor advertises quarter-hour report support. During a mixed-version rollout, an older executor keeps hourly review identities until the owner intentionally reloads it; unchanged evidence does not consume another AI meeting. A changed Scout shortlist or document finding adds the market and asset specialists to that review. Pending orders block new portfolio meetings; every new order still passes RiskEngine. Live BUY proposals register persistent entry/quantity/target/stop/deadline plans. Multiple plans can be registered in one meeting; a separate executor monitor checks prices every 10 seconds, using real fills and the same risk/receipt pipeline. Saved conclusions are shown by review round. Missing or contradictory receipts fail closed. A real app order and durable real-broker recovery proof remain outstanding; synthetic tests are not completed Live trading. `Quantara.cmd` opens the app without changing financial authority.

Owner direction on 2026-10-08: the owner supplies funding and delegates portfolio planning to CEO and the team, aiming for evidence-supported net profit after trading costs. The company compares buying, holding, reducing and selling; it may not invent confidence, force trades or expand financial authority. The 100-THB launcher remains a separate trial. Existing running accounts retain their actual limits until the owner chooses funding activation. See [the funding review](docs/funding-only-ceo-review-2026-10-08.md).

For the owner's requested small-order trial, `Enable 100 Baht Auto Trading.cmd` runs both isolated verification suites before the existing owner activation path, then lowers the saved per-order ceiling to **at most 100 THB including fee reserve**. A failed check stops before financial activation. A smaller existing ceiling stays smaller; all other limits and Stop remain intact. CEO still decides whether to buy, sell owned coins or HOLD. Orders require verified saved native-market rules and the owner's mandate; the trial does not force purchases across listed coins. On 2026-10-08, 836 headless and 108 database checks passed, and read-only current Bitkub binding/Trade verification passed. Owner activation and real application order evidence remain separate. See [the 100-THB review](docs/hundred-baht-auto-trade-review-2026-10-08.md).

The Scout reads public native THB exchange SPOT markets every 15 minutes while the isolated company has a valid activated account. It prioritizes held markets and ranks liquid candidates by spread, volume and order-book depth. Scout and meeting evidence batches contain up to eight symbols to bound cost and time; eight is not a trading-universe limit, holding ceiling or grant of financial authority. Larger portfolios rotate held evidence and keep a slot for a new eligible opportunity, while the model retains all actual positions. The source Researcher follows the reviewed public-document catalog for shortlisted coins, saves original bytes and extracted text, and accepts only source-matching quoted claims. Uncovered coins remain explicit evidence gaps. Reports reuse unchanged document versions instead of calling AI every poll. The team room keeps the conclusion first and puts the shortlist, citations and missing evidence in one optional section. The 2026-10-09 public-only smoke check returned 247 verified native markets and 247 usable quotes, with price scales 2–7 and base scales 2 or 8. Synthetic rule expansion used those public values; no authenticated order or real fill was proved. See [the current native-market review](docs/dynamic-native-markets-review-2026-10-09.md); [the earlier research review](docs/coin-research-review-2026-10-07.md) retains its historical scope.

No withdrawals, bank transfers, margin, leverage or short selling are authorized. Paper ledgers and historical/backtest profits never fund or authorize Live orders. The 700/2100-THB example is illustrative, not a configured target. Jev remains deferred.

Read [wallet operation](docs/bitkub-owner-wallet.md), [Live release evidence](docs/live-release-evidence.md) and [architecture](docs/architecture.md). Preserve saved journals and compatible readers; never rewrite evidence or delete volumes after a reconciliation failure.

## License

See [third-party notices](THIRD_PARTY_NOTICES.md).

See [price-plan implementation and activation](docs/price-plan-review-2026-10-08.md) for verification, owner handoff and remaining real-broker evidence.
