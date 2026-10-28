# Quantara Docker runtime

The Docker runtime is Quantara's canonical local runtime for the three isolated SQL
databases and the application services. See [trading controls](trading.md) for authority
and recovery.

- ManualReview uses individually approved real spot orders; the separate deterministic
  AutoLive worker requires a one-time owner mandate. New trading state defaults to Paper.
- Docker Desktop must be running on the powered-on development PC.
- Keep the repository `.env` unchanged. The helper creates runtime state under
  `.runtime/docker` and keeps broker credentials in the private `portfolio.env` file.

## Prepare, migrate and start

Run the explicit sequence from the repository root:

```powershell
.\tools\Start-QuantaraDocker.ps1 -Action Prepare
.\tools\Start-QuantaraDocker.ps1 -Action Migrate
.\tools\Start-QuantaraDocker.ps1 -Action Start
.\tools\Start-QuantaraDocker.ps1 -Action Status
```

| Action          | Behavior                                                                                                                                                     |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Prepare`       | Safe to rerun before migration and on an active deployment. Prepares private configuration and images without starting or recreating running services.       |
| First `Migrate` | Builds images, pauses native services, fences in-flight orders and AI reports, then migrates verified data as described below.                               |
| `Start`         | Preserves existing containers and their configuration. Image or configuration upgrades require controlled maintenance with a current-data backup and review. |
| `Status`        | Lists runtime state and container status.                                                                                                                    |

The first migration preserves this order:

1. Build the images before pausing native services, then fence in-flight orders and
   AI reports.
2. Create verified `COPY_ONLY`/`CHECKSUM` backups for all three databases and record
   per-table schema and data SHA-256 fingerprints.
3. Restore with explicit logical file moves, refuse replacement, and verify the restored
   data again after a real SQL container restart. Each owner has a distinct database login.
4. Write the ready marker before the first application start. The native task guard
   prevents duplicate schedulers.

- Rollback is available only before runtime activation, while the marker is absent.
  Runtime activation is separate from enabling AutoLive.
- After runtime activation, do not resume a stale native database. Use a reverse,
  verified migration from the latest canonical Docker database. Retained source files
  and backup artifacts are kept for that review.

## Private configuration

Preparation generates and preserves these independent tokens in private `runtime.env`:

| Token                            | Supplied only to                                                                          |
| -------------------------------- | ----------------------------------------------------------------------------------------- |
| `QUANTARA_TRADING_CONTROL_TOKEN` | Portfolio as `Trading__OwnerToken`; Next.js server as `QUANTARA_TRADING_CONTROL_TOKEN`.   |
| `QUANTARA_MARKET_EVENTS_TOKEN`   | MarketData and Portfolio as `MarketEvents__Token` for their private SSE/watch connection. |

- The owner-control token is a 32-byte cryptographically random value in owner-readable
  `runtime.env`. Preparation adds it to an existing file without changing SQL credentials
  and preserves it on later runs.
- Invalid or duplicate owner-control tokens stop preparation rather than silently
  rotating authority. The value never enters Team, AI workers, browser configuration
  or `.env`.
- The market-events token is an independent 32-byte random value. Team, the web server
  and model workers receive neither this value nor access to execution authority
  through the stream.
- Existing runtime files keep SQL credentials and both tokens unchanged.
- Do not print or commit the private environment file.
- Apply the new market-events binding to both services in the same reviewed maintenance
  window, with AutoLive disabled and canonical backups retained.
- Missing or mismatched market-events tokens fail closed.
- Possessing server configuration does not activate a mandate; preparing configuration
  never changes a mandate.

## Runtime and availability

| Area                 | Behavior and limits                                                                                                        |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Web                  | Published only on `127.0.0.1:3000`.                                                                                        |
| Database network     | Internal.                                                                                                                  |
| Codex authentication | Mounted writable only into Team at `/codex-auth`; never copied into an image.                                              |
| Compose recovery     | Uses `restart: unless-stopped` and health checks, but a hung or unhealthy process is not repaired by restart policy alone. |

The migration creates one owned Docker Desktop executable scheduled task for the
current user's logon. It replaces the five native runtime watchers and does not
open a PowerShell terminal.

- Continuous operation still requires the user to be logged in, the PC powered and
  online, and Docker Desktop running.
- It does not prevent a manual sleep, lid close or shutdown.
- This procedure does not claim that a physical reboot has been tested.

## Existing deployments and maintenance

Existing deployments need a reviewed maintenance update to receive new images
and environment bindings: `Prepare` alone does not apply them, and `Start` keeps
`--no-recreate`.

1. Preserve a current backup of all canonical Docker databases and the private runtime
   configuration before any controlled update.
2. Do not rerun a native-data migration over live Docker data or delete the SQL volume.
3. Leave activation to the owner. Updating containers, preparing the token and running
   health checks never enables AutoLive.

In **Crypto → Trading**, only the owner opens **Review & enable** when available,
reviews the account, rules hash, symbols, risk/capital caps and expiry, and presses
**Enable Auto Trading** in the mandate dialog. Implementation verification never
activates live trading. A reviewed Paper deployment may be applied after deterministic
checks and current-data backups pass.

## Background delivery

| Responsibility                                         | Owner and limits                                         |
| ------------------------------------------------------ | -------------------------------------------------------- |
| Fill notifications and minute-level report preparation | Portfolio uses Hangfire SQL storage in its own database. |
| Streaming prices, broker execution and owner callbacks | Remain in their existing realtime paths.                 |
| Local read-only Dashboard                              | Available at `/api/trading/jobs` through the Web server. |

- The existing account outbox remains authoritative; `sending`/`unknown` outcomes are
  never automatically reset.
- Only the exact `POST /api/trading/jobs/stats` route is permitted for stats capture.
  No job command, retry or trading authority is exposed.
- `Trading__UseHangfire=false` selects the compatible legacy delivery scheduler in
  this build. Stop/recreate Portfolio during reviewed maintenance before switching
  ownership; never run both schedulers.
- After new receipt/study fields are written, recovery must use a binary that understands
  them, not an old strict-JSON image. Keep the current canonical SQL data and Hangfire schema.

See [the release review](hangfire-adaptive-exit-review.md).

## Logs and deterministic verification

To view logs explicitly:

```powershell
Import-Module ./tools/QuantaraDocker.psm1
Invoke-QuantaraCompose -Arguments @('logs', '--tail', '100')
```

The verifier is deterministic and does not require Docker, SQL Server, broker credentials, or Codex authentication:

```powershell
.\tools\verify-docker.ps1
```

References: [Docker Compose](https://docs.docker.com/compose/), [.NET container images](https://learn.microsoft.com/dotnet/core/docker/introduction), [Microsoft SQL Server backup and restore](https://learn.microsoft.com/sql/relational-databases/backup-restore/backup-overview-sql-server), and [OpenAI Codex authentication](https://developers.openai.com/codex/auth/).
