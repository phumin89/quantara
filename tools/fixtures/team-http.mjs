import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';

// Synthetic read-only backend for an internal verification network. Never use as a service replacement.
const started = new Date().toISOString();
const rules = {
  version: 'ema20-50-breakout-v1',
  symbols: ['BTC'],
  allocatedThb: 1000,
  perOrderThb: 100,
  maxPositions: 2,
  dailyLossThb: 100,
  maxSpreadBps: 50,
  slippageBps: 20,
  maxDataAgeSeconds: 90,
  takeProfitBps: 200,
  stopLossBps: 100,
  trailingStopBps: 100,
  timeExitMinutes: 120,
  cooldownMinutes: 15,
  orderLifetimeMinutes: 15,
  feeBps: 25,
  volumeRatio: 1.5,
};
const book = {
  cashThb: '1000',
  equityThb: null,
  realizedGrossThb: null,
  realizedNetThb: null,
  feesThb: '0',
  unrealizedThb: null,
  positions: [],
  orders: [],
};
let reads = 0;
let writes = 0;
const server = createServer(async (request, response) => {
  response.setHeader('Content-Type', 'application/json');
  if (request.method !== 'GET') {
    writes++;
    response
      .writeHead(405)
      .end(JSON.stringify({ error: 'Fixture is read-only.' }));
    return;
  }
  reads++;
  if (request.url === '/__fixture/stats') {
    response.end(JSON.stringify({ reads, writes }));
    return;
  }
  let mode;
  try {
    ({ mode } = JSON.parse(await readFile(process.argv[2], 'utf8')));
  } catch {
    response
      .writeHead(503)
      .end(JSON.stringify({ error: 'Synthetic fixture mode is changing.' }));
    return;
  }
  if (mode === 'unavailable') {
    response
      .writeHead(503)
      .end(JSON.stringify({ error: 'Synthetic backend unavailable.' }));
    return;
  }
  const now = new Date().toISOString();
  const today = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Bangkok',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  const address = new URL(request.url, 'http://fixture');
  const run = {
    id: '218d734e-0446-492d-8b62-ffbd106d044e',
    accountKey: 'fixture-account-a',
    objective: 'Synthetic preview research. No real account data.',
    status: 'running',
    createdAt: started,
    updatedAt:
      mode === 'older' ? new Date(Date.now() - 3600000).toISOString() : now,
    error: null,
    steps: [
      {
        role: 'intraday',
        status: 'running',
        output:
          'Synthetic preview: checking fixture evidence only. No live research or trading.',
        error: null,
      },
      { role: 'reviewer', status: 'queued', output: null, error: null },
    ],
  };
  if (request.url.startsWith('/api/team/brief?')) {
    response.end(
      JSON.stringify({
        date: new URL(request.url, 'http://fixture').searchParams.get('date'),
        runs:
          mode === 'empty' || address.searchParams.get('date') !== today
            ? []
            : [run],
      }),
    );
    return;
  }
  if (request.url === '/api/team') {
    response.end(
      JSON.stringify({
        configured: true,
        latestRun: mode === 'empty' ? null : run,
        company: {
          enabled: true,
          status: 'running',
          nextRunAt: new Date(Date.now() + 3600000).toISOString(),
          lastRunId: run.id,
          activeRole: 'intraday',
          error: null,
        },
      }),
    );
    return;
  }
  if (address.pathname === '/api/crypto/portfolio') {
    response.end(
      JSON.stringify({
        positions: [
          {
            symbol: 'BTC',
            name: 'Synthetic Bitcoin',
            status: 'Holding',
            quantity: 1,
            averageCost: null,
            currentPrice: 7500,
            value: 7500,
            unrealizedPnl: null,
            realizedPnl: null,
            costStatus: 'Unknown',
          },
          {
            symbol: 'ETH',
            name: 'Synthetic Ethereum',
            status: 'Holding',
            quantity: 1,
            averageCost: null,
            currentPrice: 5000,
            value: 5000,
            unrealizedPnl: null,
            realizedPnl: null,
            costStatus: 'Unknown',
          },
        ],
        summary: { totalAssets: 12500, availableThb: 2500, holdingCount: 2 },
        updatedAt: now,
        priceUpdatedAt: now,
        historyUpdatedAt: now,
        historyCoverage: 'Synthetic preview only',
        syncing: false,
        completedSymbols: 2,
        totalSymbols: 2,
        error: null,
      }),
    );
    return;
  }
  if (address.pathname === '/api/crypto/history') {
    response.end(
      JSON.stringify({ trades: [], coverage: 'Synthetic preview only' }),
    );
    return;
  }
  if (address.pathname === '/api/crypto/orders') {
    response.end(JSON.stringify({ proposals: [] }));
    return;
  }
  if (address.pathname === '/api/trading') {
    response.end(
      JSON.stringify({
        accountKey: 'fixture-account-a',
        mode: 'paper',
        rules,
        rulesHash: 'synthetic-preview-rules',
        mandate: null,
        emergencyStopped: false,
        heartbeatAt: now,
        fault: null,
        liveActivationAvailable: false,
        paper: book,
        live: book,
        reports: [
          {
            date: today,
            mode: 'autoLive',
            revision: 1,
            createdAt: now,
            text: 'Synthetic retained engine report. No real fills.',
            status: 'local',
            startingEquityThb: null,
            endingEquityThb: null,
            realizedNetThb: '12.25',
          },
        ],
      }),
    );
    return;
  }
  if (address.pathname === '/api/team/news') {
    response.end(
      JSON.stringify({
        schema: 'news-v1',
        observedAt: now,
        executionAuthority: false,
        events: [],
        sourceStates: [],
        omittedEventCount: 0,
        pilot: {
          startedAt: null,
          lastPollAt: null,
          completed72Hours: false,
          gapCount: 0,
        },
      }),
    );
    return;
  }
  response
    .writeHead(404)
    .end(JSON.stringify({ error: 'Unmocked fixture path.' }));
});
server.listen(5052, '0.0.0.0', () =>
  console.log('Synthetic read-only fixture listening.'),
);
