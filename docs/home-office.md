# Home research office

Home embeds the original [Pixel Agents](https://github.com/pixel-agents-hq/pixel-agents)
App, default office layout, renderer, furniture, floor/wall/carpet assets and font.
The full-width office is the primary Home view. Eight role identities represent
CEO, Analyst, Reviewer, Intraday, Swing, News, Positions and Quant using recorded
Team state. Research never grants trading authority.

Native office selection and the keyboard-accessible role controls select the
same evidence; parent selection also focuses the native office camera. Evidence
is bounded plain text with its recorded Bangkok time and a date/run link to Daily
Brief. Working, completed, queued, failed, cancelled, blocked, stale, unknown and
idle remain distinct. Idle does not imply a completed report.

## Shared activity and chat

The root `TeamActivityProvider` owns one current-day `GET /api/team` and
`GET /api/team/brief` pair every ten seconds, shared by Home, Daily Brief and the
persistent Team chat widget. Refreshes coalesce, requests time out after eight
seconds, and polling pauses while hidden. Hiding or unmounting cancels in-flight
work. Selected historical dates use separate reads as needed.

Failed reads retain the last records as stale. Older or conflicting revisions
cannot overwrite retained work for the same account. Account changes and an empty
current run clear prior evidence. Only a fresh running step in the matching
current running run projects active work; timers and next-role hints do not.

Team chat remains mounted in the workspace shell across navigation. It opens as
a modal right panel on desktop and full screen on smaller viewports, retaining
conversation position. Its freshness, errors and Retry remain visible.

## Upstream provenance and boundary

`vendor/pixel-agents/manifest.json` records 224 byte-identical copied upstream
UI/core/asset/font files at commit `b1bafd02896d90d83457266575a1e8e5011ce997`.
Original files remain unchanged. `tools/build-pixel-office.mjs` verifies their
SHA-256 hashes and applies small tracked adaptations during bundling: read-only
transport, research labels, keyboard selection, camera selection and motion
gating. The generated `provenance.json` records these adaptations.

The same-origin iframe loads `/vendor/pixel-agents/office/index.html` with a
restricted transport and CSP `connect-src 'none'`. Only role labels/statuses,
selection and pause state cross the origin-checked parent bridge. Upstream host,
terminal, launch, filesystem and hook commands are discarded; upstream socket,
server, terminal and transcript-reader transports do not execute. Role evidence
and broker credentials are not sent into the iframe.

Animation pauses for reduced motion, hidden or offscreen content, explicit Pause
animation, or no active role. A failed office load leaves the role controls and
evidence available. Normal builds use the checked-in sources; recreating the copy
requires a clean upstream checkout at the pinned commit:

```powershell
node tools/vendor-pixel-agents.mjs C:\path\to\clean\pixel-agents
npm run build:office
```

Code and bundled assets retain upstream MIT notices. The six character assets
are upstream MIT adaptations of the CC0 MetroCity pack. Credits and licenses ship
in `apps/web/public/vendor/pixel-agents/`; see `NOTICE.txt` and `LICENSE`.

## Dashboard and dates

`/dashboard` holds the summary separately from the office: current wallet,
selected Bangkok-day confirmed engine fills and realized net P/L, retained
research, and current attention items. Paper simulation and manual trades are
excluded from the engine result. Missing evidence stays unavailable; the view
does not construct whole-account P/L or historical equity. Wallet and current
operating state do not change with the selected report day. Critical wallet,
engine, authority and research errors remain visible.

The shared date-only control uses MUI X Community `9.15.0` with Dayjs, Bangkok
timezone, a `DD/MM/YYYY` display and validation before date changes are applied.

## Verification scope

The upstream-App, shared-chat, Dashboard and date control passed isolated browser
fixtures and a production build. The independent finish review requested two
responsive fixes for role controls and summary metrics. Those fixes are applied
and the production build passes. All five affected browser suites passed again
after the fixes. Fresh desktop/tablet/phone screenshots received an independent
`ship` verdict with both responsive findings resolved. Current local screenshots
contain synthetic evidence from the upstream App. Earlier Library uploads depict
the superseded custom office.

Earlier successful builds of four Docker images targeted the previous UI. SQL
fixture tests completed. The revised Web image and container-readiness check have
not been revalidated. No live deployment was performed.
