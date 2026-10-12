# Quantara

Next.js/TypeScript/Material UI workspace backed by two independently runnable C# services. Current functionality is read-only Bitkub portfolio tracking; no AI or order execution.

## Structure

- `apps/web`: routes and API proxy, Crypto components/hooks/API/contracts, shared navigation/theme.
- `services/market-data`: public Bitkub coins and THB prices. No credentials or database.
- `services/portfolio`: signed account reads, retained history, portfolio projection and its SQL Server database.
- Each service has separate Api, Application, Domain and Infrastructure projects. Controllers delegate to Mediator CQRS handlers; Domain has no framework dependencies.
- `tests`: Portfolio, MarketData and architecture checks. One C# type or React component per named file.

Open `Quantara.sln` in Visual Studio, or this root folder in VS Code. Editor settings, analyzers, typed ESLint and strict compiler flags are committed alongside CI checks.

## Run

Install frontend dependencies once with `npm ci`, then use three terminals:

```powershell
npm run dev:market-data
npm run dev:api
npm run dev
```

Web: http://127.0.0.1:3000. Portfolio: port 5050. MarketData: port 5051. Production frontend: `npm run build`, then `npm start`. Both services expose `/health`; Portfolio checks SQL connectivity and persisted ledger readability.

Portfolio reads only BITKUB_API_KEY and BITKUB_API_SECRET from environment variables or the repository-root `.env`. MarketData never receives credentials. Configure `ConnectionStrings__Quantara` for another SQL Server; local default is the existing MSSQLLocalDB Quantara database. Set `Services__MarketDataUrl` on Portfolio and `PORTFOLIO_SERVICE_URL` on the Next.js server when services run elsewhere.

The Portfolio database preserves the original `CryptoPortfolioLedger` table, account-key fingerprint and JSON fields. Its idempotent SQL migration creates missing schema and never drops data. API-key rotation creates a separate partition. Old source is archived locally under ignored `work/prestructure`; `.env` remains outside Git.

## Behavior

Home remains empty; Crypto shows actual balances, retained closed coins and trade fills. Available THB excludes reserved funds. Total assets means held crypto only; a missing price makes the total unknown. Filters do not alter the summary. Snapshot errors retain saved records and offer Retry. Status details expose separate wallet/price/history update times.

Bitkub exchange history is limited to about 90 days and excludes broker conversions. Cost and profit remain unknown until complete accounting is verified. There are no trading, withdrawal, transfer, AI or RiskEngine paths in this version. Saans is not bundled; the documented system font fallback is used.

## Verification

```powershell
npm run format:check
npm run check
npm run build
dotnet build Quantara.sln
npm run test:architecture
npm run test:editor
dotnet run --project tests/Portfolio.Checks/Portfolio.Checks.csproj
dotnet run --project tests/MarketData.Checks/MarketData.Checks.csproj
```

Opt into real SQL verification with `-- --sql` on Portfolio.Checks. It uses a separate deterministic test account, not the owner's partition. Stop running services before rebuilding their assemblies on Windows.

Browser checks with frontend running: `npm run test:ui`, `npm run test:contracts`. Real-data `npm run test:crypto` also needs both services and configured credentials. Install Chromium with `npx playwright install chromium`, or use installed Edge via `$env:PLAYWRIGHT_CHANNEL = 'msedge'`. Test fixtures/failure injection remain confined to the isolated test browser.

CI runs formatting, lint/type checks, builds, deterministic checks, architecture checks, dependency audit, isolated browser checks and Gitleaks scanning. It never needs broker credentials or starts real account imports. GitHub branch protection must require these checks to enforce merge gates; the workflow alone does not change remote branch policy.

Project-owned Roslyn diagnostics QNT001/QNT002 enforce one type per matching
file in C# editors/builds. QNT003 rejects null-forgiving suppression and QNT004
requires explicit sealed/abstract/static class intent. ESLint's
`quantara/file-ownership` reports multiple components/types and mismatched names
while typing. `npm run test:editor` proves these rules reject invalid probes and
accept valid source without adding bad code to the application.
