import { createServer } from 'node:http';

// A task-owned wrapper keeps the synthetic label outside the application's hydration root.
const target = new URL(process.env.PREVIEW_URL ?? 'http://127.0.0.1:3109/');
if (target.origin !== 'http://127.0.0.1:3109')
  throw new Error('Only the isolated fixture port is allowed.');
target.search = '';
target.hash = '';
const pages = new Set(['/', '/dashboard', '/daily-brief', '/crypto']);
const server = createServer((request, response) => {
  if (request.method !== 'GET') {
    response.writeHead(405).end('Read-only synthetic preview.');
    return;
  }
  const address = new URL(request.url, 'http://preview');
  if (address.pathname === '/health') {
    response
      .writeHead(200, { 'Content-Type': 'text/plain' })
      .end('Synthetic preview ready.');
    return;
  }
  const selected = address.searchParams.get('page') ?? '/';
  target.pathname = pages.has(selected) ? selected : '/';
  response.writeHead(200, {
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': 'no-store',
    'Content-Security-Policy':
      "default-src 'none'; frame-src http://127.0.0.1:3109; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'",
  });
  response.end(`<!doctype html><html lang="en"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Quantara · Synthetic preview</title>
<style>html,body{margin:0;height:100%;font:14px/1.4 system-ui,sans-serif}body{display:flex;flex-direction:column}header{padding:9px 16px;background:#dbeafe;color:#1e3a8a;font-weight:600}iframe{border:0;width:100%;flex:1;min-height:0}</style>
<header role="status">Synthetic preview · fixture data only · trading controls unavailable</header>
<iframe title="Quantara synthetic preview" sandbox="allow-scripts allow-same-origin" src="${target.href}"></iframe></html>`);
});
server.listen(3110, process.env.PREVIEW_HOST ?? '127.0.0.1', () =>
  console.log('Synthetic preview wrapper ready on3110.'),
);
