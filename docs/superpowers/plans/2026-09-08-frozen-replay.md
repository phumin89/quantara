# Frozen Replay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver immutable, offline-repeatable company/CFO snapshots before starting the separate forward-paper ledger.
**Architecture:** Existing simulation handlers use an optional snapshot store port. Captured windows contain exact engine inputs and provider provenance; replay uses these windows without network access. Infrastructure saves content-addressed files.
**Tech Stack:** .NET 10, existing System.Text.Json/SHA-256, Next.js/React; no added packages.
**Spec:** docs/superpowers/specs/2026-09-08-frozen-replay-design.md (chat-approved intent).

## Global Constraints

- V1 paper only; deterministic RiskEngine final; no strategy/threshold changes.
- Salary 70000, essentials 24400, housing 14000; existing reserve policy.
- No overwrite, no network fallback on replay, no hosted-agent capture.
- Forward-paper activation is a subsequent persisted-ledger slice, not a fake historical replay.

### Task 1: Snapshot contracts and file integrity

Files: Application/SimulationSnapshots.cs, Infrastructure/JsonSimulationSnapshotStore.cs, tests/TradingCompany.Domain.Tests/SimulationSnapshotTests.cs.
Interfaces: `ISimulationSnapshotStore.SaveAsync(CompanyReplaySnapshot, CancellationToken)` returns content ID; `LoadAsync(string, CancellationToken)` verifies and returns the immutable bundle.
- [x] Add roundtrip, tamper, invalid-ID and no-overwrite tests; run filter `SimulationSnapshotTests` and observe missing implementation failure.
- [x] Serialize canonical JSON bytes; name the file `SHA256(bytes).json`; publish with `File.Move(temp, final, false)`; verify bytes hash before parsing. Explicit Symbol converter reconstructs validated values.
- [x] Run focused tests. Preserve existing user files; no git metadata exists in this workspace, so do not fabricate commits/worktrees.

### Task 2: Capture and offline replay through existing handlers

Files: Application command records, Domain result metadata, Application.Handlers/RunCompanySimulationHandler.cs and RunPersonalCfoSimulationHandler.cs, Api/RealHistoricalMarketDataProvider.cs.
- [x] Test capture -> provider throws -> replay; compare serialized domain results excluding snapshot metadata. Test changed request/build rejection and saved failed-symbol exclusions.
- [x] Append optional `CaptureSnapshot` and `SnapshotId` fields. Capture each exact requested window once, freeze source/THB/FX evidence, validate saved request/build before selecting saved windows, fingerprint the completed result and check it on replay.
- [x] Run handler and market-provider tests, then full .NET suite.

### Task 3: API/UI and real-data proof

Files: Api request records and Program.cs, web CFO panel/types, relevant API/UI tests, README.
- [x] Test options forwarded, conflicting options rejected, and replay failure cannot mutate portfolio.
- [x] Register local file store; expose capture/replay options; CFO UI captures and reruns the completed mandate by snapshot ID, showing verified identity.
- [x] Run UI render tests/typecheck and .NET tests/build. Capture one actual 70k/14k CFO run, replay offline, compare result hashes and inspect UI. Record exact evidence and remaining forward-ledger work.

Verification: `docs/research/2026-09-09-frozen-cfo-replay.md`. All first-slice
tasks complete; separate forward-paper ledger remains unimplemented/unactivated.
