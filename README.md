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
cd apps/trading-company-web
node --test tests/*.test.cjs
npm run build
```

The Docker verification runner mounts source/test files only, not broker credentials, Codex sign-in or runtime secrets. It runs deterministic strategy, historical-data parsing, costs, risk, execution/reconciliation and owner-access unit/integration tests using fixtures and fake transports. It does not start the application, consume AI allowance or submit orders. See [verification scope](docs/headless-verification.md).

## Remaining Live gates

The execution, opening inventory, daily baseline, isolated company and reconciliation paths are implemented and tested. The receipt profile supports BTC/THB, ETH/THB and SOL/THB using authenticated private order receipts containing explicit executed amounts, received coins and wallet fees. A manual sell or fee-credit exhaustion is not required. Actual supported opening assets are imported once without inventing application trades or profits. See [the operator steps](docs/bitkub-small-capital-proof.md).

The owner intentionally opens `Enable Auto Trading.cmd`. It verifies the three current market rules, retains a backup of a BTC-only receipt profile, and approves the verified market universe through the existing owner activation controls. Existing capital, per-order/daily limits, maximum open positions, opening balance, losses and owner Stop remain unchanged. After reconciliation it starts the isolated company: full eight-role research after 09:00 Bangkok, and three-role portfolio reviews when owned money/quantities change or bid/ask moves at least 1%, at most once per Bangkok hour. A changed Scout shortlist or document finding adds the market and asset specialists to that review. Pending orders block new portfolio meetings; every new order still passes RiskEngine. Saved conclusions are shown by review round. Missing or contradictory receipts fail closed. A real app order and durable real-broker recovery proof remain outstanding; synthetic tests are not completed Live trading. `Quantara.cmd` opens the app without changing financial authority.

The Scout reads public THB Spot markets every 15 minutes while the isolated company has a valid activated account. It retains monitored holdings and execution markets, then inspects up to five additional liquid markets for spread, volume and order-book depth. These additional symbols are research-only. The source Researcher reads a fixed six-document catalog for BTC, ETH and SOL, saves original bytes and extracted text, and accepts only source-matching quoted claims. Reports reuse unchanged document versions instead of calling AI every poll. The team room keeps the conclusion first and puts the shortlist, citations and missing evidence in one optional section. See [the research review and verified limits](docs/coin-research-review-2026-10-07.md).

No withdrawals, bank transfers, margin, leverage or short selling are authorized. Paper ledgers and historical/backtest profits never fund or authorize Live orders. The 700/2100-THB example is illustrative, not a configured target. Jev remains deferred.

Read [wallet operation](docs/bitkub-owner-wallet.md), [Live release evidence](docs/live-release-evidence.md) and [architecture](docs/architecture.md). Preserve saved journals and compatible readers; never rewrite evidence or delete volumes after a reconciliation failure.

## License

See [third-party notices](THIRD_PARTY_NOTICES.md).
