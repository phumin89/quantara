import { createServer } from 'node:http';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import next from 'next';

const port = Number(process.argv[2] ?? '3108');
if (![3108, 3112].includes(port)) {
  throw new Error('Fixture server port must be 3108 or 3112.');
}

const appRoot = resolve(
  fileURLToPath(new URL('../../apps/web', import.meta.url)),
);
const app = next({ dev: false, dir: appRoot, hostname: '127.0.0.1', port });
const handle = app.getRequestHandler();
const allowedPages = new Set([
  '/',
  '/crypto',
  '/dashboard',
  '/daily-brief',
  '/team',
]);

function allowedPath(pathname) {
  if (pathname === '/__fixture_health') return true;
  if (allowedPages.has(pathname)) return true;
  if (
    pathname.startsWith('/_next/static/') ||
    pathname.startsWith('/vendor/') ||
    pathname.startsWith('/fonts/') ||
    pathname.startsWith('/landscapes/')
  )
    return true;
  return ['/favicon.ico', '/icon.svg', '/apple-icon.png'].includes(pathname);
}

function safePath(pathname) {
  return (
    !pathname.includes('%') &&
    pathname.split('/').every((part) => part !== '.' && part !== '..')
  );
}

await app.prepare();
createServer((req, res) => {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.writeHead(405, { allow: 'GET, HEAD' });
    res.end();
    return;
  }
  let pathname;
  try {
    pathname = decodeURIComponent(
      new URL(req.url ?? '/', 'http://127.0.0.1').pathname,
    );
  } catch {
    res.writeHead(400);
    res.end();
    return;
  }
  if (
    !safePath(pathname) ||
    !allowedPath(pathname) ||
    pathname.includes('\\')
  ) {
    res.writeHead(404);
    res.end();
    return;
  }
  if (pathname === '/__fixture_health') {
    res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
    res.end('Quantara fixture frontend');
    return;
  }
  void handle(req, res).catch(() => {
    if (!res.headersSent) res.writeHead(500);
    res.end();
  });
})
  .listen(port, '127.0.0.1', () => {
    console.log(`Fixture frontend listening at http://127.0.0.1:${port}`);
  })
  .on('error', (error) => {
    console.error(error);
    process.exitCode = 1;
  });
