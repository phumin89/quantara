import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';

// Synthetic read-only backend for an internal verification network. Never use as a service replacement.
const started = new Date().toISOString();
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
  const { mode } = JSON.parse(await readFile(process.argv[2], 'utf8'));
  if (mode === 'unavailable') {
    response
      .writeHead(503)
      .end(JSON.stringify({ error: 'Synthetic backend unavailable.' }));
    return;
  }
  const now = new Date().toISOString();
  const run = {
    id: '218d734e-0446-492d-8b62-ffbd106d044e',
    accountKey: 'fixture-account-a',
    objective: 'Synthetic container integration evidence.',
    status: 'running',
    createdAt: started,
    updatedAt:
      mode === 'older' ? new Date(Date.now() - 3600000).toISOString() : now,
    error: null,
    steps: [
      {
        role: 'intraday',
        status: 'running',
        output: 'Synthetic container evidence only.',
        error: null,
      },
      { role: 'reviewer', status: 'queued', output: null, error: null },
    ],
  };
  if (request.url.startsWith('/api/team/brief?')) {
    response.end(
      JSON.stringify({
        date: new URL(request.url, 'http://fixture').searchParams.get('date'),
        runs: mode === 'empty' ? [] : [run],
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
  response
    .writeHead(404)
    .end(JSON.stringify({ error: 'Unmocked fixture path.' }));
});
server.listen(5052, '0.0.0.0', () =>
  console.log('Synthetic read-only fixture listening.'),
);
