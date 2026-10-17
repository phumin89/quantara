# Quantara Docker runtime

The Docker runtime is Quantara's canonical local runtime for the three isolated SQL databases and the application services. ManualReview uses individually approved real spot orders; the separate deterministic AutoLive worker requires a one-time owner mandate. New trading state defaults to Paper. Docker Desktop must be running on the powered-on development PC. Keep the repository `.env` unchanged; the helper creates runtime state under `.runtime/docker` and keeps broker credentials in the private `portfolio.env` file. See [trading controls](trading.md) for authority and recovery.

Run the explicit sequence from the repository root:

```powershell
.\tools\Start-QuantaraDocker.ps1 -Action Prepare
.\tools\Start-QuantaraDocker.ps1 -Action Migrate
.\tools\Start-QuantaraDocker.ps1 -Action Start
.\tools\Start-QuantaraDocker.ps1 -Action Status
```

`Prepare` is safe to rerun before migration and on an active deployment: it prepares private configuration and images without starting or recreating running services. It generates a 32-byte cryptographically random `QUANTARA_TRADING_CONTROL_TOKEN` in owner-readable `runtime.env`, adds it to an existing file without changing SQL credentials, and preserves it on later runs. Invalid or duplicate tokens stop preparation rather than silently rotating authority. The token is supplied only to Portfolio as `Trading__OwnerToken` and the Next.js server as `QUANTARA_TRADING_CONTROL_TOKEN`. It never enters Team, AI workers, browser configuration or `.env`. Do not print or commit the private environment file. Possessing server configuration does not activate a mandate.

The first `Migrate` builds the images before pausing native services, fences in-flight orders and AI reports, creates verified `COPY_ONLY`/`CHECKSUM` backups for all three databases, and records per-table schema and data SHA-256 fingerprints. It restores with explicit logical file moves, refuses replacement, and verifies the restored data again after a real SQL container restart. Each owner has a distinct database login. The ready marker is written before the first application start, and the native task guard prevents duplicate schedulers. Rollback is available only before runtime activation, while the marker is absent. Runtime activation is separate from enabling AutoLive. After runtime activation, do not resume a stale native database; use a reverse, verified migration from the latest canonical Docker database. Retained source files and backup artifacts are kept for that review.

Preparation additionally preserves/generates an independent 32-byte random
`QUANTARA_MARKET_EVENTS_TOKEN`. Only MarketData and Portfolio receive it as
`MarketEvents__Token` for their private SSE/watch connection. Team, the web server
and model workers receive neither this value nor access to execution authority
through the stream. Existing runtime files keep SQL credentials and both tokens
unchanged. Apply the new binding to both services in the same reviewed maintenance
window, with AutoLive disabled and canonical backups retained. Missing or mismatched
tokens fail closed; preparing configuration never changes a mandate.

The web service is published only on `127.0.0.1:3000`. The database network is internal. Codex authentication is mounted writable only into Team at `/codex-auth`; it is never copied into an image. Compose uses `restart: unless-stopped` and health checks, but a hung or unhealthy process is not repaired by restart policy alone. `Start` preserves existing containers and their configuration; image or configuration upgrades require controlled maintenance with a current-data backup and review. `Status` lists runtime state and container status.

The migration creates one owned Docker Desktop executable scheduled task for the
current user's logon. It replaces the five native runtime watchers and does not
open a PowerShell terminal. Continuous operation still requires the user to be
logged in, the PC powered and online, and Docker Desktop running. It does not
prevent a manual sleep, lid close or shutdown, and this procedure does not claim
that a physical reboot has been tested.

Existing deployments need a reviewed maintenance update to receive new images
and environment bindings: `Prepare` alone does not apply them, and `Start` keeps
`--no-recreate`. Preserve a current backup of all canonical Docker databases and
the private runtime configuration before any controlled update. Do not rerun a
native-data migration over live Docker data or delete the SQL volume. Updating
containers, preparing the token and running health checks never enables AutoLive.
Only the owner reviews the account, rules hash, symbols, risk/capital caps and
expiry in Daily Brief and presses **Enable Auto Trading** once. No such live
activation is part of implementation verification; a reviewed Paper deployment
may be applied after deterministic checks and current-data backups pass.

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
