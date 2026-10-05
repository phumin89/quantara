# Frozen replay and forward-paper boundary

Approved in chat: immutable file-backed data, offline identical reruns, then a separate forward-paper account; salary THB 70,000, essentials THB 24,400, housing THB 14,000, existing reserve policy. No paid API, strategy changes, live money or local-AI runtime.

First independently usable slice: capture the exact company/CFO replay request, per-window normalized market inputs, source bars and applied historical FX where the provider supplies them, exclusions, build identity and result fingerprint. Content-addressed JSON files use SHA-256 filenames, atomic no-overwrite publication and read-time verification. They are tamper-evident, not signed or a substitute for filesystem permissions/backups. New inputs create a new snapshot; replay by ID cannot silently refresh, change the mandate, or contact a provider. The existing independent-window validation remains authoritative.

Application ports own the capture contract; filesystem storage belongs in Infrastructure; API binds snapshot options and the existing handlers run the same domain engine. The UI exposes snapshot identity and an offline rerun action using the completed result's mandate. It must distinguish captured from reproduced, and show a failure rather than falling back to live data. Hosted challenger capture is excluded because model output is not deterministic. Existing requests remain compatible.

Acceptance: captured/replayed trades, cash flows, equity curves, fees, exclusions, stress outcomes and result fingerprint agree under identical builds even if the provider changes or is unavailable. Tampering, invalid IDs, missing snapshots, changed mandate/build and incomplete window manifests fail closed. Shared paper balances never change.

Second slice: forward-paper accounting starts from separately recorded virtual contributions, not backtest profits. It must persist dated decisions before later market outcomes exist; salary postings are idempotent, reserves stay outside trading, and no already-observed 2026 history is relabeled as forward evidence. Do not activate the existing shared company wallet as a shortcut. Activation follows snapshot verification and a dedicated persisted-ledger integration; it is not claimed complete by the first slice.

File snapshots are preferred over a new database/service for this stage. Merely saving result totals cannot prove reproducibility. Snapshotting does not repair historical universe membership, within-window provider rejection or adjusted-price bias; these remain disclosed.
