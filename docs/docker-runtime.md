# Quantara Docker runtime

The Docker runtime is Quantara's canonical local runtime for the three isolated SQL databases and the application services that can create individually approved real spot orders. Docker Desktop must be running on the powered-on development PC. Keep the repository `.env` unchanged; the helper creates runtime state under `.runtime/docker` and keeps broker credentials in the private `portfolio.env` file.

Run the explicit sequence from the repository root:

```powershell
.\tools\Start-QuantaraDocker.ps1 -Action Prepare
.\tools\Start-QuantaraDocker.ps1 -Action Migrate
.\tools\Start-QuantaraDocker.ps1 -Action Start
.\tools\Start-QuantaraDocker.ps1 -Action Status
```

`Prepare` is safe to rerun before migration. The first `Migrate` builds the images before pausing native services, fences in-flight orders and AI reports, creates verified `COPY_ONLY`/`CHECKSUM` backups for all three databases, and records per-table schema and data SHA-256 fingerprints. It restores with explicit logical file moves, refuses replacement, and verifies the restored data again after a real SQL container restart. Each owner has a distinct database login. The ready marker is written before the first application start, and the native task guard prevents duplicate schedulers. Rollback is available only before activation, while the marker is absent. After activation, do not resume a stale native database; use a reverse, verified migration from the latest canonical Docker database. Retained source files and backup artifacts are kept for that review.

The web service is published only on `127.0.0.1:3000`. The database network is internal. Codex authentication is mounted writable only into Team at `/codex-auth`; it is never copied into an image. Compose uses `restart: unless-stopped` and health checks, but a hung or unhealthy process is not repaired by restart policy alone. `Start` preserves existing containers and their configuration; image or configuration upgrades require controlled maintenance with a current-data backup and review. `Status` lists runtime state and container status.

The migration creates one owned Docker Desktop executable scheduled task for the
current user's logon. It replaces the five native runtime watchers and does not
open a PowerShell terminal. Continuous operation still requires the user to be
logged in, the PC powered and online, and Docker Desktop running. It does not
prevent a manual sleep, lid close or shutdown, and this procedure does not claim
that a physical reboot has been tested.

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
