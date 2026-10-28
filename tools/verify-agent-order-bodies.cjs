const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
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
  TeamController,
} = require('../apps/web/features/team/api/TeamController.ts');
const {
  OrdersController,
} = require('../apps/web/features/orders/api/OrdersController.ts');
const id = '00000000-0000-4000-8000-000000000001';
const targets = [
  {
    name: 'company',
    valid: '{"enabled":false}',
    limit: 1000,
    budget: 10000,
    path: '/api/team/company',
    upstream: '/api/team/company',
    call: (request) => TeamController.company(request),
  },
  {
    name: 'research',
    valid: '{"objective":"Synthetic research"}',
    limit: 12000,
    budget: 10000,
    path: '/api/team/runs',
    upstream: '/api/team/runs',
    call: (request) => TeamController.start(request),
  },
  {
    name: 'proposal',
    valid:
      '{"symbol":"BTC","side":"buy","amount":"10.00","limitPrice":"1.00","reason":"Synthetic only"}',
    limit: 2000,
    budget: 30000,
    path: '/api/crypto/orders',
    upstream: '/api/crypto/orders',
    call: (request) => OrdersController.create(request),
  },
  ...['approve', 'reject', 'reconcile'].map((action) => ({
    name: action,
    valid: action === 'approve' ? '{"confirmed":true}' : '{}',
    limit: 2000,
    budget: 30000,
    path: '/api/crypto/orders/' + id + '/' + action,
    upstream: '/api/crypto/orders/' + id + '/' + action,
    call: (request) =>
      OrdersController[action](request, { params: Promise.resolve({ id }) }),
  })),
];

function input(
  target,
  value,
  { pending = false, signal, onPull, headers = {} } = {},
) {
  let controller;
  let cancellations = 0;
  const body = new ReadableStream({
    start(source) {
      controller = source;
      source.enqueue(
        typeof value === 'string' ? new TextEncoder().encode(value) : value,
      );
      if (!pending) source.close();
    },
    pull() {
      onPull?.();
    },
    cancel() {
      cancellations++;
    },
  });
  return {
    request: new NextRequest('http://127.0.0.1:3000' + target.path, {
      method: 'POST',
      headers: {
        host: '127.0.0.1:3000',
        'content-type': 'application/json',
        ...headers,
      },
      body,
      duplex: 'half',
      ...(signal ? { signal } : {}),
    }),
    stop() {
      try {
        controller.error(new Error('Synthetic input stopped.'));
      } catch {}
    },
    cancellations: () => cancellations,
  };
}

async function verify() {
  const previousFetch = global.fetch;
  const previousTimeout = AbortSignal.timeout;
  const calls = [];
  const failures = [];
  const unhandled = [];
  const onUnhandled = (error) => unhandled.push(error);
  const check = async (name, action) => {
    try {
      await action();
    } catch (error) {
      failures.push(name + ': ' + error.message);
    }
  };
  const bounded = async (pending, fixture) => {
    let timer;
    try {
      return await Promise.race([
        pending,
        new Promise((_, reject) => {
          timer = setTimeout(
            () => reject(new Error('Input read did not finish within guard.')),
            300,
          );
        }),
      ]);
    } finally {
      clearTimeout(timer);
      fixture.stop();
      await pending.catch(() => undefined);
    }
  };
  process.on('unhandledRejection', onUnhandled);
  try {
    global.fetch = async (url, options) => {
      assert(
        targets.some((target) => new URL(url).pathname === target.upstream),
      );
      assert.equal(options.method, 'POST');
      calls.push({
        path: new URL(url).pathname,
        body: JSON.parse(options.body),
      });
      return Response.json({ synthetic: true });
    };
    for (const target of targets) {
      for (const mode of ['deadline', 'caller', 'already-cancelled']) {
        await check(target.name + ' ' + mode, async () => {
          let deadline;
          let budget;
          AbortSignal.timeout = (milliseconds) => {
            budget = milliseconds;
            deadline = new AbortController();
            return deadline.signal;
          };
          const caller = new AbortController();
          if (mode === 'already-cancelled') caller.abort();
          const fixture = input(target, '{', {
            pending: true,
            signal: caller.signal,
            onPull: () =>
              mode === 'deadline' ? deadline?.abort() : caller.abort(),
          });
          const before = calls.length;
          const response = await bounded(target.call(fixture.request), fixture);
          assert.equal(response.status, 400);
          assert.equal(budget, target.budget);
          assert.equal(fixture.cancellations(), 1);
          assert.equal(
            calls.length,
            before,
            'Rejected input must never reach upstream.',
          );
        });
      }
      AbortSignal.timeout = previousTimeout;
      for (const [name, value, options] of [
        [
          'oversize streamed input',
          'x'.repeat(target.limit + 1),
          { pending: true },
        ],
        [
          'oversize declared input',
          target.valid,
          { headers: { 'content-length': String(target.limit + 1) } },
        ],
        [
          'malformed declared input',
          target.valid,
          { headers: { 'content-length': 'invalid' } },
        ],
        ['invalid UTF-8', new Uint8Array([0xc3]), {}],
      ]) {
        await check(target.name + ' ' + name, async () => {
          const fixture = input(target, value, options);
          const before = calls.length;
          const response = await bounded(target.call(fixture.request), fixture);
          assert.equal(response.status, 400);
          assert.equal(
            calls.length,
            before,
            'Invalid input cannot cause an action.',
          );
          if (options.pending) assert.equal(fixture.cancellations(), 1);
        });
      }
      await check(target.name + ' exact byte limit', async () => {
        const bytes = Buffer.byteLength(target.valid);
        const fixture = input(
          target,
          target.valid + ' '.repeat(target.limit - bytes),
        );
        const before = calls.length;
        assert.equal((await target.call(fixture.request)).status, 200);
        assert.equal(calls.length, before + 1);
        assert.deepEqual(calls.at(-1).body, JSON.parse(target.valid));
      });
    }
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(unhandled, []);
    assert.deepEqual(failures, []);
    console.log(
      'Agent/order bodies: 48 cancellation, limit, encoding and valid-request cases passed; fake fetch only.',
    );
  } finally {
    global.fetch = previousFetch;
    AbortSignal.timeout = previousTimeout;
    process.removeListener('unhandledRejection', onUnhandled);
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
