# Local continuous operation

Docker Compose is the canonical local runtime; see
[Docker runtime](docker-runtime.md). The native watcher path below is retained
for development and for a deliberate, verified reverse migration only. Do not
start it while Docker owns the active databases.

## Install and start

Run from the repository root in PowerShell 7 after `npm ci` and local Codex sign-in:

```powershell
./tools/Start-Quantara.ps1 -Plan
./tools/Start-Quantara.ps1
```

The installer:

1. Builds isolated Release binaries and the production frontend.
2. Registers four service tasks for the current interactive user, plus a SQL watcher
   when an original LocalDB binding is configured.

| Task property | Behavior                                                                                                                         |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| Startup       | Each task starts immediately and after that user signs in.                                                                       |
| Identity      | Uses the same user's SQL LocalDB and Codex login; no administrator account, stored Windows password or SYSTEM service is needed. |
| Naming        | Includes a hash of this checkout so another checkout has separate task names.                                                    |
| Windows       | Stay hidden.                                                                                                                     |

## Watcher behavior and availability

| Situation                                        | Watcher behavior                                                           |
| ------------------------------------------------ | -------------------------------------------------------------------------- |
| Existing verified service on the configured port | Adopts it.                                                                 |
| Port becomes available                           | Starts its built executable.                                               |
| Adopted process exits                            | Checks again every 15 seconds.                                             |
| Watcher fails                                    | Windows retries after one minute.                                          |
| Stopped watcher while the user is logged in      | A one-minute recurring trigger restarts it; running instances are ignored. |
| Port occupied by an unverified process           | Leaves it alone; inspect the host log to resolve the conflict.             |
| Alive process stops responding                   | Does not restart it; watchers restart exited processes.                    |

- These triggers never Resume a persistently stopped company or approve orders.
- Watchers do not kill processes, call trading endpoints, replay reports or retry broker orders.

The watcher requests that Windows stay awake during idle time:

- The display may still turn off and the computer may lock.
- This request ends with the watcher; the power plan is unchanged.
- Manual Sleep, closing a laptop lid, shutdown and logoff can still interrupt operation.
- For uninterrupted operation, keep this user logged in, the PC powered and its
  network available. Tasks resume after login, not before it.

See Microsoft's
[SetThreadExecutionState documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate).

## Optional verified LocalDB identity

LocalDB started from an app's virtualized data area can differ from the default
instance reached by a Windows task, even for the same user. For this case, pass
the **master file of the already verified original instance** to the installer:

```powershell
./tools/Start-Quantara.ps1 -SqlMasterFile '<verified original master.mdf path>'
```

The binding preserves database identity:

1. The installer saves the path and current Windows SID in ignored `.runtime/sql-instance.json`.
2. On service launch, the watcher resolves the running LocalDB process by that exact
   master file and owner.
3. The watcher supplies its current pipe with each service's own database name.

Missing or ambiguous matches wait; they never fall back to another instance or create
a replacement database. No database is copied, moved or shared.

When this optional binding is configured and verified, the dedicated SQL watcher
adopts that instance or starts its registered engine with the same master, transaction
logs and Windows owner. Confirm the intended instance before supplying its path;
the repository does not establish a machine-specific binding.

- Startup requires a unique matching HKCU registration, the installed HKLM engine
  location, existing system database files and a valid Microsoft executable signature.
- It does not create an instance, copy databases, edit registry values or change
  Windows security policy.
- Missing or ambiguous identity fails visibly.
- Existing API services receive the current verified pipe on their next launch.

## Existing artifacts and Windows policy

To update task configuration without replacing an existing compiled backend:

```powershell
./tools/Start-Quantara.ps1 -ExistingArtifactsPath '<existing repository build directory>'
```

Windows App Control can still reject an application assembly:

- A database recovery does not remove that block; the application must be permitted
  by Windows policy or use an approved trusted build.
- See Code Integrity event 3077 and the service error log.
- Do not disable protection, relocate binaries or load blocked code by another route
  as a workaround.

## Inspect and stop

```powershell
$plan = ./tools/Start-Quantara.ps1 -Plan
$plan | ForEach-Object { Get-ScheduledTask -TaskName $_.TaskName }
Get-Content .runtime/logs/MarketData.host.log -Tail 10
```

| Evidence                | Meaning and limits                                             |
| ----------------------- | -------------------------------------------------------------- |
| `.runtime/logs`         | Host state and separate service output for each launch.        |
| `IdleSleepBlocked=True` | Confirms the native idle-sleep request succeeded.              |
| Build outputs and logs  | Stay ignored by Git; credentials are not written to host logs. |

Use **Pause research** in Daily Brief to persistently stop AI research. Market
monitoring and broker-status reconciliation can remain available. Removing the
host tasks is a separate maintenance action:

```powershell
./tools/Start-Quantara.ps1 -Remove
```

- `-Remove` stops and removes this checkout's watchers. Adopted services can remain
  running; it is not a replacement for **Pause research**.
- Removing a task can terminate the child process it launched. Do it during maintenance
  with no active report or order submission.
- Existing live watchers keep their original build path when the installer is rerun;
  a newly registered task uses the new build when it next starts. Use a planned
  maintenance window for upgrades.
- When restarting a watcher manually, wait for its task to leave **Running** before
  starting it again. Windows task stopping is asynchronous and an immediate start
  can be ignored by the single-instance setting.

## Research and orders

| Area                           | Behavior and limits                                                                                                                     |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| Monitoring and research        | Continue overnight and across Bangkok midnight, with the existing hourly review and deduplicated signal assessment cadence.             |
| Priority                       | Current spot setups and actual positions/exit plans take priority over unrelated simulations.                                           |
| Configured objective           | Account-scoped total wallet equity, including confirmed deposits, with the target amount and closing date supplied by account evidence. |
| ManualReview spot LIMIT orders | Require the configured owner's explicit Telegram or app approval of that exact proposal.                                                |
| AutoLive orders                | Use the separate exact owner-enabled mandate described in [trading controls](trading.md).                                               |
| `npm run test:runtime`         | Checks native helper compilation, scoped paths and port ownership with a local fixture; it does not register tasks or call Bitkub.      |

- Midnight does not stop research, reset capital or close positions.
- Valuation, deposits and trading P&L remain separate; incomplete evidence stays Unknown.
  The objective is not a guarantee or a reason to force a trade.
- Restarting services does not approve orders. Submission outcomes that are uncertain
  remain unretried.
- Implementation, development and testing never activate AutoLive or send real orders.
