const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const { z } = require('zod');
require.extensions['.ts'] = (module, filename) =>
  module._compile(
    ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
      },
    }).outputText,
    filename,
  );
const { NextRequest } = require('next/server');
const {
  PortfolioControlProxy,
} = require('../apps/web/shared/api/PortfolioControlProxy.ts');
const {
  TradingJobsController,
} = require('../apps/web/app/api/trading/jobs/TradingJobsController.ts');
const targets = [
  {
    name: 'trading',
    path: '/api/trading/disable',
    type: 'application/json',
    valid: '{}',
    padding: ' ',
    budget: 30000,
    call: (request) =>
      PortfolioControlProxy.forward(
        request,
        '/api/trading/disable',
        z.strictObject({}),
      ),
  },
  {
    name: 'statistics',
    path: '/api/trading/jobs/stats',
    type: 'application/x-www-form-urlencoded',
    valid: 'metrics%5B%5D=stats:enabled',
    padding: '&',
    budget: 15000,
    call: (request) =>
      TradingJobsController.get(request, {
        params: Promise.resolve({ path: ['stats'] }),
      }),
  },
];

function bodyFixture(
  value,
  { onPull, close = true, cancelError = false, cancelPending = false } = {},
) {
  let source;
  let cancelled = 0;
  let releaseCancellation;
  const cancellation = cancelPending
    ? new Promise((resolve) => {
        releaseCancellation = resolve;
      })
    : undefined;
  const body = new ReadableStream({
    start(controller) {
      source = controller;
      controller.enqueue(
        typeof value === 'string' ? new TextEncoder().encode(value) : value,
      );
      if (close) controller.close();
    },
    pull() {
      onPull?.();
    },
    cancel() {
      cancelled++;
      if (cancelError) throw new Error('synthetic cleanup secret=do-not-leak');
      return cancellation;
    },
  });
  return {
    body,
    stop: () => source.error(new Error('Synthetic test completed.')),
    cancelled: () => cancelled,
    async finishCancellation() {
      releaseCancellation?.();
      await cancellation;
    },
  };
}

function request(target, body, signal) {
  return new NextRequest('http://127.0.0.1:3000' + target.path, {
    method: 'POST',
    headers: { host: '127.0.0.1:3000', 'content-type': target.type },
    body,
    duplex: 'half',
    ...(signal ? { signal } : {}),
  });
}

async function bounded(pending, fixture) {
  let guard;
  try {
    return await Promise.race([
      pending,
      new Promise((_, reject) => {
        guard = setTimeout(
          () => reject(new Error('Body read exceeded the test guard.')),
          500,
        );
      }),
    ]);
  } finally {
    clearTimeout(guard);
    fixture.stop();
    await fixture.finishCancellation();
    await pending.catch(() => {});
  }
}

async function verify() {
  const previous = {
    fetch: global.fetch,
    timeout: AbortSignal.timeout,
    token: process.env.QUANTARA_TRADING_CONTROL_TOKEN,
    service: process.env.PORTFOLIO_SERVICE_URL,
  };
  const calls = [];
  const failures = [];
  const unhandled = [];
  const onUnhandled = (error) => unhandled.push(error);
  process.on('unhandledRejection', onUnhandled);
  try {
    process.env.QUANTARA_TRADING_CONTROL_TOKEN = 'synthetic-control-body-token';
    process.env.PORTFOLIO_SERVICE_URL = 'http://portfolio.fixture.invalid';
    global.fetch = async (url, init) => {
      assert(
        targets.some(
          (target) =>
            String(url) === 'http://portfolio.fixture.invalid' + target.path,
        ),
      );
      assert.equal(init.method, 'POST');
      assert.equal(
        init.headers['X-Quantara-Trading-Control'],
        'synthetic-control-body-token',
      );
      calls.push({ url, body: init.body });
      return Response.json({ accepted: true });
    };
    for (const target of targets) {
      for (const { mode, cancelPending = false } of [
        { mode: 'deadline' },
        { mode: 'caller' },
        { mode: 'already-cancelled' },
        { mode: 'deadline', cancelPending: true },
        { mode: 'caller', cancelPending: true },
      ]) {
        let deadline;
        let configuredBudget;
        AbortSignal.timeout = (milliseconds) => {
          configuredBudget = milliseconds;
          deadline = new AbortController();
          return deadline.signal;
        };
        const caller = new AbortController();
        if (mode === 'already-cancelled') caller.abort();
        const fixture = bodyFixture(target.valid.slice(0, 1), {
          close: false,
          cancelPending,
          onPull: () => {
            if (mode === 'deadline')
              deadline?.abort(
                new DOMException('Synthetic deadline.', 'TimeoutError'),
              );
            else caller.abort();
          },
        });
        try {
          const response = await bounded(
            target.call(request(target, fixture.body, caller.signal)),
            fixture,
          );
          assert.equal(
            response.status,
            408,
            `${target.name}: ${mode} rejects before forwarding.`,
          );
          assert.equal(configuredBudget, target.budget);
          assert.equal(
            fixture.cancelled(),
            1,
            'Cancelled reads release the input stream.',
          );
          assert.equal(
            calls.length,
            0,
            'No cancelled or timed out request reaches the backend.',
          );
        } catch (error) {
          failures.push(
            `${target.name} ${mode}${cancelPending ? ' pending cleanup' : ''}: ${error.message}`,
          );
        }
      }
    }
    assert.deepEqual(failures, []);
    AbortSignal.timeout = previous.timeout;
    for (const target of targets) {
      for (const cancelError of [false, true]) {
        const fixture = bodyFixture('x'.repeat(8193), {
          close: false,
          cancelError,
        });
        const response = await bounded(
          target.call(request(target, fixture.body)),
          fixture,
        );
        assert.equal(response.status, 413);
        assert.equal(fixture.cancelled(), 1);
        assert.equal(calls.length, 0);
        assert(!JSON.stringify(await response.json()).includes('do-not-leak'));
      }
      const malformed = bodyFixture(new Uint8Array([0xc3]));
      assert.equal(
        (await target.call(request(target, malformed.body))).status,
        400,
      );
      assert.equal(calls.length, 0);
    }
    for (const target of targets) {
      const exactLimit =
        target.valid + target.padding.repeat(8192 - target.valid.length);
      const fixture = bodyFixture(exactLimit);
      assert.equal(
        (await target.call(request(target, fixture.body))).status,
        200,
      );
    }
    assert.equal(
      calls.length,
      2,
      'Only the two valid synthetic requests reach fake fetch.',
    );
    assert.equal(
      calls[0].body,
      '{}',
      'Validation preserves the normalized trading payload.',
    );
    assert.equal(
      calls[1].body,
      new URLSearchParams(targets[1].valid).toString(),
    );
    for (const method of ['POST', 'GET']) {
      for (const mode of ['transport', 'body']) {
        const before = calls.length;
        global.fetch = async (url, init) => {
          assert.equal(
            String(url),
            'http://portfolio.fixture.invalid/api/trading/disable',
          );
          assert.equal(init.method, method);
          calls.push({ url, body: init.body });
          if (mode === 'transport')
            throw new TypeError(
              'Synthetic upstream failure secret=do-not-leak',
            );
          return new Response(
            new ReadableStream({
              start(controller) {
                controller.error(
                  new TypeError('Synthetic upstream body secret=do-not-leak'),
                );
              },
            }),
          );
        };
        const input =
          method === 'POST'
            ? request(targets[0], bodyFixture('{}').body)
            : new NextRequest('http://127.0.0.1:3000/api/trading/disable', {
                headers: { host: '127.0.0.1:3000' },
              });
        const failed = await PortfolioControlProxy.forward(
          input,
          '/api/trading/disable',
          method === 'POST' ? z.strictObject({}) : undefined,
        );
        assert.equal(failed.status, 503);
        const body = await failed.json();
        assert.equal(
          body.error,
          method === 'POST'
            ? 'The action may have completed. Refresh status before taking another action.'
            : 'Trading service is unavailable. Refresh to try again.',
        );
        assert(!JSON.stringify(body).includes('do-not-leak'));
        assert.equal(
          calls.length,
          before + 1,
          'Proxy failures never repeat the attempted request.',
        );
      }
    }
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(
      unhandled,
      [],
      'Stream cancellation failures are observed.',
    );
    console.log(
      'Control bodies: deterministic deadlines/cancellation, bounded bytes, malformed UTF-8 and exact-limit valid requests passed; fake fetch only.',
    );
  } finally {
    global.fetch = previous.fetch;
    AbortSignal.timeout = previous.timeout;
    process.removeListener('unhandledRejection', onUnhandled);
    for (const [key, value] of [
      ['QUANTARA_TRADING_CONTROL_TOKEN', previous.token],
      ['PORTFOLIO_SERVICE_URL', previous.service],
    ]) {
      if (value === undefined) delete process.env[key];
      else process.env[key] = value;
    }
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
