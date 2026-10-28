// Deterministic component-level regression for Dashboard financial provenance.
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const assert = require('node:assert/strict');
const ts = require('typescript');

const root = path.resolve(__dirname, '..');
const file = path.join(
  root,
  'apps/web/features/dashboard/components/dashboard-screen/DashboardScreen.tsx',
);
const date = '2026-10-20';
const originalLoad = Module._load;
let Screen;
Module._load = function (request, parent, isMain) {
  if (parent?.filename === file) {
    if (request === 'react')
      return {
        Component: class {
          constructor(props) {
            this.props = props;
          }
          setState(update, callback) {
            this.state = {
              ...this.state,
              ...(typeof update === 'function' ? update(this.state) : update),
            };
            callback?.();
          }
        },
      };
    if (request.endsWith('/DashboardServiceContext'))
      return {
        DashboardServiceContext: { value: {}, require: (context) => context },
      };
    return {};
  }
  return originalLoad.call(this, request, parent, isMain);
};
try {
  const mod = new Module(file, module);
  mod.filename = file;
  mod.paths = Module._nodeModulePaths(path.dirname(file));
  mod._compile(
    ts.transpileModule(fs.readFileSync(file, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
        jsx: ts.JsxEmit.React,
      },
    }).outputText,
    file,
  );
  Screen = mod.exports.default;
} finally {
  Module._load = originalLoad;
}

function makeScreen(accountKey) {
  const screen = new Screen({
    activity: { accountKey, scopeRevision: 0, date },
  });
  screen.mounted = true;
  return screen;
}

function response(kind, accountKey) {
  if (kind === 'trading')
    return {
      accountKey,
      mode: 'paper',
      reports: [],
      paper: {},
      live: {},
      autonomy: null,
    };
  return {
    accountKey,
    capital: { accountKey, observedAt: '2026-10-20T03:00:00Z' },
    performance: null,
  };
}

function service(kind, accountKey) {
  return {
    get: async () => {
      if (accountKey === null) throw new Error('Synthetic read failed.');
      return response(kind, accountKey);
    },
  };
}

function configure(screen, tradingKey, riskKey) {
  screen.context = {
    trading: service('trading', tradingKey),
    risk: service('risk', riskKey),
  };
}

function result(name, screen) {
  return {
    name,
    teamAccount: screen.props.activity.accountKey,
    tradingAccount: screen.state.trading?.accountKey ?? null,
    riskAccount: screen.state.risk?.accountKey ?? null,
    tradingError: screen.state.tradingError,
    riskError: screen.state.riskError,
  };
}

async function run() {
  const results = [];
  const noTeam = makeScreen(null);
  configure(noTeam, 'A', 'A');
  await noTeam.load();
  results.push(result('No research run; matching A/A finance', noTeam));

  const staleTeam = makeScreen('A');
  configure(staleTeam, 'B', 'B');
  await staleTeam.load();
  results.push(result('Research still A; matching B/B finance', staleTeam));

  const mixed = makeScreen(null);
  configure(mixed, 'A', 'B');
  await mixed.load();
  results.push(result('Conflicting A/B finance', mixed));

  const partial = makeScreen('A');
  configure(partial, 'A', 'A');
  await partial.load();
  configure(partial, 'A', null);
  await partial.load();
  results.push(
    result('Same-account partial refresh after accepted A/A', partial),
  );
  assert.equal(partial.state.trading?.accountKey, 'A');
  assert.equal(partial.state.risk?.accountKey, 'A');
  assert.equal(partial.state.riskError, 'Capital evidence unavailable');
  configure(partial, 'B', null);
  await partial.load();
  results.push(result('New B plus risk failure must not retain A', partial));

  const pending = makeScreen('A');
  let resolveTrading, resolveRisk;
  pending.context = {
    trading: {
      get: () =>
        new Promise((resolve) => {
          resolveTrading = resolve;
        }),
    },
    risk: {
      get: () =>
        new Promise((resolve) => {
          resolveRisk = resolve;
        }),
    },
  };
  const oldLoad = pending.load();
  const previous = pending.props;
  pending.props = { activity: { accountKey: 'B', scopeRevision: 1, date } };
  configure(pending, 'B', 'B');
  pending.componentDidUpdate(previous);
  await new Promise(setImmediate);
  resolveTrading(response('trading', 'A'));
  resolveRisk(response('risk', 'A'));
  await oldLoad;
  results.push(
    result('Late A results after observed scope switch to B', pending),
  );

  const validThenBothFail = makeScreen('A');
  configure(validThenBothFail, 'A', 'A');
  await validThenBothFail.load();
  configure(validThenBothFail, null, null);
  await validThenBothFail.load();
  results.push(result('Both sources fail after valid pair', validThenBothFail));

  const currentMismatch = makeScreen('A');
  configure(currentMismatch, 'A', 'A');
  await currentMismatch.load();
  configure(currentMismatch, 'A', 'B');
  await currentMismatch.load();
  results.push(
    result('Current mismatch clears retained pair', currentMismatch),
  );

  for (const row of results) console.log(JSON.stringify(row));
  assert.equal(noTeam.state.trading?.accountKey, 'A');
  assert.equal(noTeam.state.risk?.accountKey, 'A');
  assert.equal(staleTeam.state.trading?.accountKey, 'B');
  assert.equal(staleTeam.state.risk?.accountKey, 'B');
  assert.equal(mixed.state.trading, null);
  assert.equal(mixed.state.risk, null);
  assert.equal(partial.state.trading, null);
  assert.equal(partial.state.risk, null);
  assert.equal(pending.state.trading?.accountKey, 'B');
  assert.equal(pending.state.risk?.accountKey, 'B');
  assert.equal(validThenBothFail.state.trading?.accountKey, 'A');
  assert.equal(validThenBothFail.state.risk?.accountKey, 'A');
  assert.equal(currentMismatch.state.trading, null);
  assert.equal(currentMismatch.state.risk, null);
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
