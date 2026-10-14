# Local continuous operation

Run from the repository root in PowerShell 7 after `npm ci` and local Codex
sign-in:

```powershell
./tools/Start-Quantara.ps1 -Plan
./tools/Start-Quantara.ps1
```

The installer builds isolated Release binaries and the production frontend,
then registers four Windows Scheduled Tasks for the current interactive user.
Each task starts immediately and after that user signs in. It uses the same
user's SQL LocalDB and Codex login; no administrator account, stored Windows
password or SYSTEM service is needed. Task names include a hash of this checkout
so another checkout has separate task names. All windows stay hidden.

Each watcher adopts an existing verified service on its configured port, or
starts its built executable when the port becomes available. An adopted process
that exits is checked again every 15 seconds. A watcher failure is retried by
Windows after one minute. Occupied ports belonging to unverified processes are
left alone; inspect the host log to resolve the conflict. Watchers do not kill
processes, call trading endpoints, replay reports or retry broker orders. They
restart exited processes, not an alive process that has stopped responding.

The watcher requests that Windows stay awake during idle time. The display may
still turn off and the computer may lock. This request ends with the watcher;
the power plan is unchanged. Manual Sleep, closing a laptop lid, shutdown and
logoff can still interrupt operation. See Microsoft's
[SetThreadExecutionState documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate).
For uninterrupted operation, keep this user logged in, the PC powered and its
network available. Tasks resume after login, not before it.

### Existing LocalDB identity

LocalDB started from an app's virtualized data area can differ from the default
instance reached by a Windows task, even for the same user. For this case, pass
the **master file of the already verified original instance** to the installer:

```powershell
./tools/Start-Quantara.ps1 -SqlMasterFile '<verified original master.mdf path>'
```

The path and current Windows SID are saved in ignored `.runtime/sql-instance.json`.
On service launch, the watcher resolves the running LocalDB process by that exact
master file and owner, then supplies its current pipe with each service's own
database name. Missing or ambiguous matches wait; they never fall back to another
instance or create a replacement database. No database is copied, moved or shared.

This checkout is pinned to the original instance on this PC. That SQL instance
must also be running. Login restarts the watchers; it does not prove the original
SQL instance has started. If it is absent, the host log shows the waiting state.
Independent SQL startup or a move to a normally hosted SQL Server is separate
infrastructure work. Existing services continue using their current connection
until they exit; a restarted service receives the verified binding.

## Inspect and stop

```powershell
$plan = ./tools/Start-Quantara.ps1 -Plan
$plan | ForEach-Object { Get-ScheduledTask -TaskName $_.TaskName }
Get-Content .runtime/logs/MarketData.host.log -Tail 10
```

`.runtime/logs` contains host state and separate service output for each launch.
`IdleSleepBlocked=True` confirms the native idle-sleep request succeeded. Build
outputs and logs stay ignored by Git. Credentials are not written to host logs.

Use **Stop company** in Daily Brief to persistently stop AI research. Market
monitoring and broker-status reconciliation can remain available. Removing the
host tasks is a separate maintenance action:

```powershell
./tools/Start-Quantara.ps1 -Remove
```

This stops and removes this checkout's watchers. Adopted services can remain
running; it is not a replacement for Stop company. Removing a task can terminate
the child process it launched, so do it during maintenance with no active report
or order submission. Existing live watchers keep their original build path when
the installer is rerun; a newly registered task uses the new build when it next
starts. Use a planned maintenance window for upgrades.
When restarting a watcher manually, wait for its task to leave **Running** before
starting it again. Windows task stopping is asynchronous and an immediate start
can be ignored by the single-instance setting.

## Research and orders

Market monitoring continues overnight and across Bangkok midnight. Research
keeps the existing hourly review and deduplicated signal assessment cadence;
midnight does not stop research, reset capital or close positions. Current spot
setups and actual positions/exit plans take priority over unrelated simulations.
The 500 THB daily realized net-profit target remains an objective, not a guarantee
or a reason to force a trade.

Every real spot LIMIT order still requires the configured owner's explicit
Telegram or app approval of that exact proposal. Restarting services does not
approve orders. Submission outcomes that are uncertain remain unretried.
`npm run test:runtime` checks native helper compilation, scoped paths and port
ownership with a local fixture; it does not register tasks or call Bitkub.
