# Docker startup socket recovery — 2026-10-03

Observed: Docker Desktop 4.85.0.235549, Windows build 26200.9457. Backend startup failed with Windows inaccessible-file/invalid-name errors on `Docker/run/dockerInference`. After that path was quarantined, the same failure appeared on `docker-secrets-engine/engine.sock`. This happened before the Linux engine was usable; it explains the PostgreSQL fixture startup failure, not a failed accounting assertion.

Matching first-person reports on Docker's issue tracker: [disabled inference still binds a socket](https://github.com/docker/desktop-feedback/issues/448), [recurring socket failures on build 26200](https://github.com/docker/desktop-feedback/issues/527). These reports support the observed failure mechanism; they do not prove a permanent fix for this machine. Disabling Model Runner alone is not established as sufficient.

Recovery performed:

1. Confirm the backend log contains this exact startup failure. Inspect Docker process paths; close only Docker processes from its known install directory. Confirm they are gone before changing runtime paths.
2. Inspect both runtime directories, verifying exact absolute paths and socket-only contents. Preserve them by renaming to timestamped sibling directories, rather than deleting them. Both original paths must be absent before restarting: recovering only one allows the next crash to leave the other freshly created socket stale again.
3. Start Docker Desktop and verify server response and existing containers. No reset to factory defaults, volume deletion, WSL distribution unregister, secret upload or engine upgrade was performed.

Preserved directories:

- `C:/Users/poolm/AppData/Local/Docker/run-stale-20261003-165556`
- `C:/Users/poolm/AppData/Local/Docker/run-stale-20261003-165837`
- `C:/Users/poolm/AppData/Local/docker-secrets-engine-stale-20261003-165726`

Outcome: engine 29.6.2 responds; the existing Sandbox web, Pixel Office and report-db containers resumed; report-db healthy. All 39 Live PostgreSQL integration cases passed with disposable fixture databases. This resolves this occurrence. It does not guarantee stale sockets will never recur after an abnormal Docker exit. Do not add automatic runtime-directory cleanup on every start: only apply the narrowly verified recovery while Docker is stopped and this specific error is present.
