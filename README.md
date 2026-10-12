# Quantara

Single-page frontend using Next.js, TypeScript and Material UI, with a
C# ASP.NET Core/Mediator backend and SQL Server portfolio storage.
The owner-provided DESIGN.md controls the visual language.

## Run locally

```powershell
npm ci
npm run dev
```

Open http://127.0.0.1:3000. Production: `npm run build`, then `npm start`.
Run `npm run typecheck` to check TypeScript.

With the app running, run `npm run test:ui` for responsive layout and keyboard
navigation checks. Install the test browser once with `npx playwright install chromium`,
or use installed Edge on Windows: `$env:PLAYWRIGHT_CHANNEL = 'msedge'`.

The left navigation is permanent at widths of 768px and above. On smaller
screens, the menu button opens a left drawer, closed by selecting Home or Crypto,
pressing Escape or tapping the backdrop.

Home is intentionally empty. Crypto reads actual Bitkub wallet balances,
prices and exchange trade history. There is no AI or trading capability.
Saans is not bundled; the documented system font fallback is used.
Keep `.env` and runtime secrets out of Git.

Home is available at `/`, and Crypto at `/crypto`. Next.js client navigation
switches pages while keeping the shared navigation shell mounted.

## Portfolio backend

Run `npm run dev:api` in a second terminal. It listens on 127.0.0.1:5050.
On this Windows development machine it uses the existing SQL Server LocalDB
instance and creates the Quantara database and table if absent. To use a
different SQL Server, set `ConnectionStrings__Quantara` in the backend
environment. Existing data is never dropped. The website proxies requests
on the same origin; no exchange credentials are sent to the browser.

Backend credentials come only from BITKUB_API_KEY and BITKUB_API_SECRET
in the server environment or root `.env`. Portfolio documents are partitioned
by an API-key fingerprint. A new key starts a separate partition and retains
the old one. The backend has no order, withdrawal or transfer endpoints.

## Crypto behavior

Refresh starts a paced background import. All/Holding/Closed filters and
coin buttons show retained fills and fee credits. Imported coins remain in
SQL Server even when their balance is zero. A failed sync retains the last
successful snapshot and shows an error. Prices display their update time.

Bitkub exposes about 90 days of exchange trade history. Broker conversions,
older activity and unavailable markets can be missing. Average cost and
profit remain unknown until a complete ledger is verified. Imported fills
are deduplicated and retained without pruning. Scroll wide tables with
touch, mouse or keyboard; the navigation itself does not overflow.

## Crypto verification

With both services running: `npm run test:ui` and `npm run test:crypto`.
The latter reads real data and simulates backend failure only within its
isolated test browser. Set PLAYWRIGHT_CHANNEL to msedge as above.

Domain, signature, pagination and persistence checks:

```powershell
dotnet run --project backend/Quantara.Checks/Quantara.Checks.csproj -- --sql
```

SQL checks use an isolated test account partition, never the owner's
partition. Stop the backend before rebuilding changed backend code on
Windows to avoid locking its executable.
