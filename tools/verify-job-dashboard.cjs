const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');

require.extensions['.ts'] = (module, filename) => {
  module._compile(
    ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
      },
    }).outputText,
    filename,
  );
};
const { NextRequest } = require('next/server');
const {
  TradingJobsController,
} = require('../apps/web/app/api/trading/jobs/TradingJobsController.ts');

async function run() {
  const previousFetch = global.fetch;
  const previousToken = process.env.QUANTARA_TRADING_CONTROL_TOKEN;
  const previousTimeout = AbortSignal.timeout;
  let calls = 0;
  let expectedMethod = 'GET';
  let upstream = new Response('dashboard', {
    headers: { 'Content-Type': 'text/html' },
  });
  const request = (options = {}) =>
    new NextRequest('http://127.0.0.1:3000/api/trading/jobs', {
      headers: { host: '127.0.0.1:3000', ...options.headers },
      method: options.method ?? 'GET',
      body: options.body,
    });
  const get = (input = request(), path = []) =>
    TradingJobsController.get(input, { params: Promise.resolve({ path }) });
  try {
    process.env.QUANTARA_TRADING_CONTROL_TOKEN = 'fixture-server-only-token';
    global.fetch = async (url, options) => {
      calls++;
      assert.equal(
        options.headers['X-Quantara-Trading-Control'],
        'fixture-server-only-token',
      );
      assert.equal(options.method, expectedMethod);
      assert.equal(options.redirect, 'manual');
      assert.match(String(url), /\/api\/trading\/jobs/);
      return upstream;
    };
    const accepted = await get();
    assert.equal(accepted.status, 200);
    assert.equal(await accepted.text(), 'dashboard');
    assert.equal(accepted.headers.get('X-Quantara-Trading-Control'), null);
    assert.equal(accepted.headers.get('Cache-Control'), 'no-store');
    const beforeDenied = calls;
    for (const input of [
      request({ method: 'POST' }),
      request({ headers: { origin: 'https://evil.test' } }),
      request({ headers: { 'sec-fetch-site': 'cross-site' } }),
    ]) {
      assert.equal((await get(input)).status, 403);
    }
    assert.equal((await get(request(), ['..'])).status, 400);
    assert.equal((await get(request(), ['%2f..'])).status, 400);
    assert.equal(
      calls,
      beforeDenied,
      'Rejected input never reaches Portfolio.',
    );
    expectedMethod = 'POST';
    upstream = new Response('{}', {
      headers: { 'Content-Type': 'application/json' },
    });
    const stats = await get(
      request({
        method: 'POST',
        body: 'metrics%5B%5D=succeeded%3Acount',
        headers: { 'content-type': 'application/x-www-form-urlencoded' },
      }),
      ['stats'],
    );
    assert.equal(stats.status, 200, 'Read-only statistics POST is allowed.');
    const beforeInvalidStats = calls;
    assert.equal(
      (
        await get(
          request({
            method: 'POST',
            body: 'jobs%5B%5D=1',
            headers: { 'content-type': 'application/x-www-form-urlencoded' },
          }),
          ['stats'],
        )
      ).status,
      400,
    );
    assert.equal(
      (
        await get(
          request({
            method: 'POST',
            body: 'x'.repeat(8193),
            headers: { 'content-type': 'application/x-www-form-urlencoded' },
          }),
          ['stats'],
        )
      ).status,
      413,
    );
    assert.equal(
      (await get(request({ method: 'POST' }), ['recurring', 'trigger'])).status,
      403,
    );
    assert.equal(
      calls,
      beforeInvalidStats,
      'No command or invalid form reaches Hangfire.',
    );
    expectedMethod = 'GET';
    upstream = new Response(null, {
      status: 302,
      headers: { location: 'https://evil.test' },
    });
    assert.equal((await get()).status, 503);
    upstream = new Response(null, {
      status: 302,
      headers: { location: '/api/trading/jobs/recurring' },
    });
    assert.equal(
      (await get()).headers.get('location'),
      '/api/trading/jobs/recurring',
    );
    upstream = new Response(null, { status: 204 });
    assert.equal((await get()).status, 204);
    upstream = new Response(new Uint8Array(2_000_001));
    assert.equal((await get()).status, 503);
    const cleanupErrors = [];
    const onCleanupError = (error) => cleanupErrors.push(error);
    process.on('unhandledRejection', onCleanupError);
    try {
      upstream = new Response(
        new ReadableStream({
          start(controller) {
            controller.enqueue(new Uint8Array(2_000_001));
          },
          cancel() {
            throw new Error('Synthetic cancellation failure.');
          },
        }),
      );
      assert.equal((await get()).status, 503);
      for (const { oversized, abortDeadline } of [
        { oversized: true, abortDeadline: false },
        { oversized: true, abortDeadline: true },
        { oversized: false, abortDeadline: true },
      ]) {
        let releaseCancellation;
        let cancelled = 0;
        let guard;
        const cleanup = new Promise((resolve) => {
          releaseCancellation = resolve;
        });
        const deadline = new AbortController();
        AbortSignal.timeout = () => deadline.signal;
        upstream = new Response(
          new ReadableStream({
            start(controller) {
              controller.enqueue(new Uint8Array(oversized ? 2_000_001 : 1));
            },
            cancel() {
              cancelled++;
              return cleanup;
            },
          }),
        );
        const pending = get();
        try {
          await new Promise((resolve) => setImmediate(resolve));
          if (abortDeadline) deadline.abort();
          const response = await Promise.race([
            pending,
            new Promise((_, reject) => {
              guard = setTimeout(
                () =>
                  reject(
                    new Error(
                      'Response must finish before cancellation cleanup.',
                    ),
                  ),
                500,
              );
            }),
          ]);
          assert.equal(response.status, 503);
          assert.equal(cancelled, 1);
        } finally {
          clearTimeout(guard);
          releaseCancellation();
          await cleanup;
          await pending.catch(() => {});
          AbortSignal.timeout = previousTimeout;
        }
      }
      await new Promise((resolve) => setImmediate(resolve));
      assert.deepEqual(
        cleanupErrors,
        [],
        'Response cancellation failures must be observed.',
      );
    } finally {
      process.removeListener('unhandledRejection', onCleanupError);
    }
    delete process.env.QUANTARA_TRADING_CONTROL_TOKEN;
    const beforeMissing = calls;
    assert.equal((await get()).status, 503);
    assert.equal(calls, beforeMissing);
    console.log(
      'Job dashboard: local GET, origin/path guards, server-only credentials, bounded responses and safe redirects passed.',
    );
  } finally {
    global.fetch = previousFetch;
    AbortSignal.timeout = previousTimeout;
    if (previousToken === undefined)
      delete process.env.QUANTARA_TRADING_CONTROL_TOKEN;
    else process.env.QUANTARA_TRADING_CONTROL_TOKEN = previousToken;
  }
}
run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
