const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) =>
    module._compile(
      ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
        compilerOptions: {
          module: ts.ModuleKind.CommonJS,
          target: ts.ScriptTarget.ES2022,
          jsx: ts.JsxEmit.ReactJSX,
          esModuleInterop: true,
        },
      }).outputText,
      filename,
    );
}
const CryptoPage =
  require('../apps/web/features/crypto/components/crypto-page/CryptoPage.tsx').default;
const globals = { window: globalThis.window, document: globalThis.document };
const timers = new Map();
let nextTimer = 0;

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

function page() {
  const requests = [];
  const component = new CryptoPage({});
  component.context = {
    history: {
      get: (_symbol, signal) => {
        const pending = deferred();
        requests.push({ ...pending, signal });
        return pending.promise;
      },
    },
  };
  component.mounted = true;
  component.generation = 1;
  component.setState = (update) => {
    const next =
      typeof update === 'function' ? update(component.state) : update;
    if (next) component.state = { ...component.state, ...next };
  };
  return { component, requests };
}

async function verify() {
  globalThis.window = {
    setTimeout: (callback, delay) => {
      const id = ++nextTimer;
      timers.set(id, { callback, delay });
      return id;
    },
    clearTimeout: (id) => timers.delete(id),
  };
  globalThis.document = { removeEventListener: () => {} };
  try {
    for (const symbol of ['constructor', 'toString', 'hasOwnProperty']) {
      const inherited = page();
      const inheritedRead = inherited.component.openHistory(symbol, false);
      assert.equal(
        inherited.requests.length,
        1,
        'Inherited object members are not cached history.',
      );
      inherited.requests[0].resolve({
        trades: [{ transactionId: 'own-history' }],
      });
      await inheritedRead;
      assert.equal(
        Object.hasOwn(inherited.component.state.history, symbol),
        true,
      );
      assert.equal(
        inherited.component.state.history[symbol][0].transactionId,
        'own-history',
      );
      assert.equal(timers.size, 0);
    }
    const cached = page();
    cached.component.state.history = { constructor: [] };
    await cached.component.openHistory('constructor', false);
    assert.equal(
      cached.requests.length,
      0,
      'Own empty history remains cached.',
    );
    assert.equal(cached.component.state.expanded, 'constructor');

    const timed = page();
    const pending = timed.component.openHistory('BTC', false);
    const [timerId, timer] = [...timers][0];
    assert.equal(timer.delay, 8000);
    timers.delete(timerId);
    timer.callback();
    assert.equal(timed.requests[0].signal.aborted, true);
    timed.requests[0].resolve({ trades: [{ transactionId: 'late' }] });
    await pending;
    assert.equal(
      timed.component.state.history.BTC,
      undefined,
      'Expired results never enter the cache.',
    );
    assert.equal(timed.component.state.historyLoading.BTC, false);
    assert.equal(
      timed.component.state.historyErrors.BTC,
      'History request timed out. Try again.',
      'Ignored abort still ends in an explicit timeout.',
    );
    assert.equal(timers.size, 0);

    const replaced = page();
    const first = replaced.component.openHistory('BTC', false);
    const second = replaced.component.openHistory('BTC', true);
    assert.equal(replaced.requests[0].signal.aborted, true);
    replaced.requests[0].reject(new Error('Old failure'));
    await first;
    assert.equal(
      replaced.component.state.historyLoading.BTC,
      true,
      'Old finalizer cannot stop the newer spinner.',
    );
    assert.equal(replaced.component.state.historyErrors.BTC, undefined);
    const trades = [{ transactionId: 'current' }];
    replaced.requests[1].resolve({ trades });
    await second;
    assert.deepEqual(replaced.component.state.history.BTC, trades);
    assert.equal(replaced.component.state.historyLoading.BTC, false);
    assert.equal(timers.size, 0);

    const cleared = page();
    const clearing = cleared.component.openHistory('BTC', false);
    cleared.component.clearHistory();
    cleared.requests[0].resolve({ trades: [{ transactionId: 'stale' }] });
    await clearing;
    assert.deepEqual(cleared.component.state.history, {});
    assert.deepEqual(cleared.component.state.historyErrors, {});
    assert.deepEqual(cleared.component.state.historyLoading, {});
    assert.equal(cleared.component.state.expanded, null);
    assert.equal(timers.size, 0);

    const unmounted = page();
    const leaving = unmounted.component.openHistory('BTC', false);
    unmounted.component.componentWillUnmount();
    const state = unmounted.component.state;
    unmounted.requests[0].resolve({ trades: [{ transactionId: 'unmounted' }] });
    await leaving;
    assert.equal(
      unmounted.component.state,
      state,
      'Unmounted completion cannot update state.',
    );
    assert.equal(timers.size, 0);
    console.log(
      'Crypto history state: ignored abort, replacement, invalidation, unmount and timer cleanup passed.',
    );
  } finally {
    for (const [name, value] of Object.entries(globals)) {
      if (value === undefined) delete globalThis[name];
      else globalThis[name] = value;
    }
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
