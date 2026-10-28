# Home research office

Home embeds the original [Pixel Agents](https://github.com/pixel-agents-hq/pixel-agents)
App, default office layout, renderer, furniture, floor/wall/carpet assets and font.
The full-width office is the primary Home view. Eight role identities represent
CEO, Analyst, Reviewer, Intraday, Swing, News, Positions and Quant using recorded
Team state. Research never grants trading authority.

Native office selection and the keyboard-accessible role controls select the
same role, including the first touch on a character on tablet or phone. Parent
selection also focuses the native office camera, including a
click on the already-selected role. Status refreshes and pause changes preserve
manual pan; they do not issue another selection command. Role controls
sit vertically beside the office on desktop, below it on smaller screens. Full
role evidence stays in Daily Brief. Working, completed, queued, failed, cancelled, blocked, stale, unknown and
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

Team chat remains mounted outside route templates in the workspace shell. It
uses a right-side panel with Expand and Collapse controls. When collapsed, Expand
lives in a reserved header slot, in header keyboard order, and leaves page controls
unobstructed while scrolling. Desktop starts expanded;
phone uses a modal drawer with a backdrop and focus management. Collapse or Escape
returns focus to the Expand control. Reopening, normal navigation and Back preserve
the reading position. It follows phone visual
viewport and safe-area bounds, including keyboard resize/pan. Freshness, errors
and Retry remain visible. The read-only reports have no message composer or
invented unread count. Daily Brief owns the dated archive, with each Reviewer
report rendered once in a bounded selected-day reader and no embedded chat duplicate.

Dashboard keeps this shared provider even on a cold deep link, but its financial
charts use existing Trading and Risk reads. Historical archive dates use separate
requests guarded by date, account scope revision and cancellation. Unmounting a consumer cancels its own request, never the shared
provider request. A small Next route template announces committed route changes
to the shared header navigation; the provider and widget remain outside it.

## Upstream provenance and boundary

`vendor/pixel-agents/manifest.json` records 224 byte-identical copied upstream
UI/core/asset/font files at commit `b1bafd02896d90d83457266575a1e8e5011ce997`.
Original files remain unchanged, including the pinned furniture directory `BIN`;
its exact ignore exception keeps those source assets out of the compiled-output
ignore rule. `tools/build-pixel-office.mjs` verifies their
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
animation. Idle character motion remains available and does not imply active research.
A failed office load leaves the role controls and
archive link available. Normal builds use the checked-in sources; recreating the copy
requires a clean upstream checkout at the pinned commit:

```powershell
node tools/vendor-pixel-agents.mjs C:\path\to\clean\pixel-agents
npm run build:office
```

Code and bundled assets retain upstream MIT notices. The six character assets
are upstream MIT adaptations of the CC0 MetroCity pack. Credits and licenses ship
in `apps/web/public/vendor/pixel-agents/`; see `NOTICE.txt` and `LICENSE`.

## Dashboard and dates

`/dashboard` owns financial KPIs and charts: current verified account equity,
cash-flow-adjusted recorded return when available, selected Bangkok-day live net
exits and seven-day retained engine marks. Engine equity is allocated strategy
capital, not whole-account history. Account drawdown and contribution history
stay unavailable. Missing dates remain gaps. Paper totals and hypothetical
strategy studies have separate labels and never become actual trading profit.
Current valuation does not change with the selected performance day. Failed
refreshes retain original timestamps; account switches clear prior values and
ignore late responses. Crypto owns visible trading controls and holding/history
details; Daily Brief owns full research.

The shared date-only control uses MUI X Community `9.15.0` with Dayjs, Bangkok
timezone, a `DD/MM/YYYY` display and validation before date changes are applied.

## Verification scope

`tools/verify-workspace.cjs` verifies all four canonical routes at desktop,
tablet and phone sizes with synthetic GET fixtures. It covers source scopes,
repeated dates, empty/failed refreshes, account races and archive deep links.
`tools/verify-team-widget.cjs` covers collapse/expand, focus, retained reading,
normal navigation/Back, shared polling, account-switch failure/timeout and the
keyboard-reduced phone viewport. `tools/verify-report-reader.cjs` covers safe
Markdown/GFM, explicit missing summaries, footnotes, bounded keyboard scrolling,
horizontal phone tables, late date responses, retry and Crypto account-context
resets. Raw HTML/images and unsafe source-link schemes are disabled. Current news
is not presented as an archive for historical dates. Final capture and command evidence is retained
under `.impeccable/review`; consult the current release checkpoint for acceptance.
Earlier Library uploads depict the superseded custom office.

Earlier container fixtures validated isolated frontend/backend behavior. Current
frontend-only maintenance preserves a rollback image, verifies canonical backups,
replaces only web with no dependency restarts, and checks actual served assets and
unchanged backend identities. Runtime evidence stays in ignored local storage.

The container fixture uses its own ordinary Docker bridge and publishes only
loopback ports. On this host an internal bridge left the configured Web port
unpublished, despite a healthy container; the same image returned HTTP 200 on the
separate ordinary bridge. It has no production network, mounts or credentials.
Set `QUANTARA_CONTAINER_RUNTIME=true` as compose does; local Host and Origin
validation remains enabled.
`tools/verify-container-home.cjs` verifies actual HTTP proxies, static assets,
backend loss/recovery, state transitions and Web restart readiness. Set
`VERIFY_TAG` and `FIXTURE_MODE_PATH` for the task namespace.

The optional `tools/fixtures/preview-shell.mjs` wrapper adds a persistent synthetic
data label outside the app, with no hydration changes. It accepts only GET and
frames the isolated Web port. Its backend fixture rejects every write. Runtime
image IDs, exact commit, test outcomes and helper state are recorded in the task
checkpoint; none of these helpers connects to the running account.
