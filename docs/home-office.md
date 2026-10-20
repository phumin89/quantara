# Home research office

Home is a read-only view of recorded Team work. Eight stable seats represent CEO,
Analyst, Reviewer, Intraday, Swing, News, Positions and Quant. A character animates
only when its step and the current run both report running. Research never grants
trading authority.

The Team roster is the keyboard-accessible equivalent of clicking a character.
Selection shows bounded plain-text evidence, its recorded Bangkok time, and a link
to that date and run in Daily Brief's Team chat. Empty roles remain idle; an idle
role is not a completed report. Failed, cancelled, queued, blocked, stale and
unknown remain distinct visible labels.

## Data and lifecycle

- Home polls existing `GET /api/team` and `GET /api/team/brief` queries every ten
  seconds, with an eight-second timeout and one request pair in flight.
- A failure retains the last records as stale. Older or conflicting revisions of
  the same account cannot overwrite retained work. Account changes and an empty
  current run clear prior evidence; unidentified legacy accounts cannot merge
  another account's identified history.
- Records without a current matching running run cannot imply active work. No
  activity is synthesized from company timers or next-role hints.
- Polls pause while the document is hidden and abort on unmount. Animation also
  pauses offscreen, for reduced motion, when explicitly paused, or when no role is
  active. Canvas observers, listeners and animation frames are cleaned up.
- The renderer loads only in the browser. A failed renderer leaves the Team roster
  and recorded evidence available. Browser state contains no broker credentials.

## Renderer and licenses

The renderer uses a reviewed dependency subset of
[Pixel Agents](https://github.com/pixel-agents-hq/pixel-agents) at commit
`b1bafd02896d90d83457266575a1e8e5011ce997` under its MIT license. Six character
adaptations derive from the original
[MetroCity free character pack](https://jik-a-4.itch.io/metrocity-free-topdown-character-pack)
released as CC0. License and asset notices ship in
`apps/web/public/vendor/pixel-agents/`. Desks and chairs are authored in the local
facade; no upstream furniture, floor, wall or pet asset packs are copied.

`vendor/pixel-agents/manifest.json` records copied paths, upstream paths, license
and SHA-256. Normal builds require no upstream checkout or network. To deliberately
recreate the pinned subset from a clean checkout, run:

```powershell
node tools/vendor-pixel-agents.mjs C:\path\to\clean\pixel-agents
npm run build:office
```

The vendoring command rejects another commit or a dirty checkout before copying.
The build verifies all manifest hashes and refuses privileged upstream modules.
The resulting browser module is loaded from the same origin. It has no session
scanner, file watcher, terminal, WebSocket or control transport.
The Web Dockerfile also rebuilds this verified module and copies the public assets
into Next's standalone runtime. Container builds and deployment were not run as
part of the isolated fixture verification.

## Verification

```powershell
npm run test:home-adapter
npm run check
npm run build
```

Browser fixtures require a separate loopback Next server, upstream service URLs
pointing to an unused port, and every API mocked. They refuse the live default
port. With that server at port 3107:

```powershell
$env:TEST_URL='http://127.0.0.1:3107'
$env:PLAYWRIGHT_CHANNEL='msedge'
npm run test:home
```

The fixtures cover the actual renderer, stable seats, keyboard evidence selection,
read-only API requests, plaintext output, connection failure/recovery, account
transitions, report deep links, renderer failure and desktop/tablet/mobile layout.
Screenshots contain synthetic fixture evidence only.
