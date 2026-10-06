# Quantara

Quantara is the owner's Live trading application. A persistent sidebar connects Office, Account/assets and Trading pages without reloading the app. The colored Pixel Office stays on the homepage; a floating team room opens with the current system conclusion and reasons. It does not invent team activity or import paper reports into Live. Historical paper data is retained; strategy/backtest and accounting verification run headlessly and only when requested.

**Current production status:** authenticated Bitkub wallet viewing works. Real-money execution remains disabled. Wallet access is not verified trading-account binding, an approved investment budget or permission to place orders.

## Start the app

Start Docker Desktop, then double-click `Quantara.cmd`. It opens the app and reuses an already-running installation. Alternatively:

```powershell
.\tools\start-live-readonly.ps1
```

Open [Quantara](https://localhost:3000). The launcher requires the existing trusted localhost certificate and ports 3000/7443 either free or occupied by the verified owned service. It starts only the owner service and internal frontend. Both restart with Docker; closing the terminal does not stop the app. Use `-Rebuild` on the script after source changes. Existing TLS files and remembered viewing remain intact during normal startup.

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

The execution, provisioning, daily baseline, isolated CEO and reconciliation paths are implemented and tested. Current actual broker evidence is THB 0 with successful Trade permission, no supported open orders or history. Funding and real buy/sell settlement proof remain necessary before preparing the verified private BTC profile. Use `tools/bitkub-proof.ps1` to capture and verify that evidence; see [the operator steps](docs/bitkub-small-capital-proof.md).

After the profile passes, the owner intentionally runs `pwsh -NoProfile -File tools/activate-live.ps1`. It validates the current key/profile, initializes a funded account under the approved full-Spot mandate, reconciles and arms through the existing owner controls, then starts the isolated CEO. This permits subsequent real orders that pass the deterministic RiskEngine. It never clears an owner Stop or resets losses. Missing evidence fails closed. A real app order and durable recovery proof remain outstanding; synthetic tests are not completed Live trading.

No withdrawals, bank transfers, margin, leverage or short selling are authorized. Paper ledgers and historical/backtest profits never fund or authorize Live orders. The 700/2100-THB example is illustrative, not a configured target. Jev remains deferred.

Read [wallet operation](docs/bitkub-owner-wallet.md), [Live release evidence](docs/live-release-evidence.md) and [architecture](docs/architecture.md). Preserve saved journals and compatible readers; never rewrite evidence or delete volumes after a reconciliation failure.

## License

See [third-party notices](THIRD_PARTY_NOTICES.md).
