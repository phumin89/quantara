# Persistent Sandbox team conversation

The company workspace now prioritizes a dated transcript of real saved reports, proposals, risk rejections and paper execution results. Pixel Office is an optional disclosure below it. There is no fabricated chat or message composer.

## Storage and evidence

PostgreSQL 17 runs in the Sandbox Compose project with a persistent `sandbox-reports` volume and loopback-only port 55435. It uses the existing Npgsql dependency. Connection credentials are generated once by `start-sandbox.ps1` and kept in ignored `.runtime/report-db.env` and `.runtime/report-db.json`. They are not placed in browser code, model prompts, process arguments or logs. Restore both files together; missing halves fail rather than regenerating credentials over an existing volume.

`CompanyConversationService` projects the configured Sandbox artifacts, checksum-validated daily journal and validated paper ledger into `sandbox_reports.days` and `sandbox_reports.messages` every ten seconds. These are report tables, not authoritative financial storage. Existing JSON files remain intact and continue to govern the company and paper account.

Messages are keyed by business date and source identity. Re-importing is idempotent. Conflicting content under an existing identity rolls back the complete import. The date and message read uses one consistent database snapshot. Dates use Bangkok time; proposal weights are ratios rendered as percentages.

Old raw artifacts can be read without being promoted into accepted meeting evidence. A report is marked accepted only when a completed journal explicitly references its ID, role, context hash, cutoff and summary. Cached reports retain their original timestamps and can be linked to a later round without inventing a new authoring time. Legacy reports retain their original language.

Paper order/receipt events are separate system messages. An order submitted for future execution is not a fill. Only validated ledger receipts establish paper execution or RiskEngine rejection. The no-orders statement is shown only when the ledger was successfully verified for the imported day. No Live account data is included.

## Read endpoints

- `GET /sandbox/conversations`: available dates, newest first, up to 366.
- `GET /sandbox/conversations/yyyy-MM-dd`: selected day, status, chronologically ordered messages, projection timestamp and execution-verification flag; up to 512 messages.
- Invalid dates return 400. Unavailable database or invalid source import returns 503, never a fabricated empty result.

The browser defaults to the most recent saved date. It supports a date picker, Today and Latest Report. Date selection survives polling; old-day messages cannot appear beneath a new day. Failed reads retry automatically. Switching to Live unmounts this Sandbox reader.

## Upgrade and rollback

Run `start-sandbox.ps1 -Action Restart` to build/restart an already-running API after upgrading; plain Start reuses a healthy existing API. The script starts the report database before the API. Keep the named volume and both configuration files during restart and backup.

This is an additive read-model migration. No financial data is migrated or deleted. To roll back the feature, restore the previous application version while retaining the original source files, report volume and configuration files. There is no authorized table/volume purge or destructive contraction step. Report import can be unavailable without granting trading authority or clearing risk/reconciliation guards.

## Verification — 2026-10-02

Lead code review and two bounded desktop/mobile visual rounds completed. Actual import contains all eight reports from 2026-10-01 with legacy provenance. Database restart and repeated import still leave eight records. Today (2026-10-02) returns an honest empty day; the chosen date stays selected across database outage and automatic recovery. Malformed date returns 400, outage returns 503.

523 backend tests passed, including PostgreSQL persistence, conflict rollback, cached-report linkage, proposal/receipt distinction and risk rejection. 48 web tests passed, including escaped text, malformed contracts, percentage units, date isolation, absence-of-execution verification and explicit failures. TypeScript, API publish and production web build passed. Eight original funding/account/control files remained hash-identical. Evidence is in `work/company-conversation/`.

The local worker supplied an initial UI fallback after its proposal bridge failed. Lead review rejected incomplete date/error handling, an invalid test import and incorrect action handling, then implemented and verified the corrections. This workspace has no Git metadata; no Git check is claimed.
