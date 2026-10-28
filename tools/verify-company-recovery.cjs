// Executes actual transpiled component methods with in-process synthetic services.
// No browser, server, network, account or financial write is used.
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');
const assert = require('node:assert/strict');
const ts = require('typescript');
const root = path.resolve(__dirname, '..');
const originals = Module._load;
const sources = new Set();
const context = { value: {}, require: (value) => value };
const Component = class {
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
};
const today = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Bangkok',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
}).format(new Date());
function load(relative) {
  const file = path.join(root, relative);
  sources.add(file);
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
  return mod.exports;
}
let snapshot;
Module._load = function (request, parent, isMain) {
  if (sources.has(parent?.filename)) {
    if (request === 'react') return { Component };
    if (request.endsWith('/TeamServiceContext'))
      return { TeamServiceContext: context };
    if (request.endsWith('/TeamActivityContext'))
      return { TeamActivityContext: context };
    if (request.endsWith('/TeamActivitySnapshot')) return snapshot;
    if (request.endsWith('/CompanyLoopStatus'))
      return { CompanyLoopStatus: { Error: 'error' } };
    return {};
  }
  return originals.call(this, request, parent, isMain);
};
let Provider, Page;
try {
  snapshot = load(
    'apps/web/features/team/presentation/TeamActivitySnapshot.ts',
  );
  Provider = load(
    'apps/web/features/team/components/team-activity-provider/TeamActivityProvider.tsx',
  ).TeamActivityProvider;
  Page = load(
    'apps/web/features/team/components/daily-brief-page/DailyBriefPage.tsx',
  ).default;
} finally {
  Module._load = originals;
}
global.document = { hidden: false, removeEventListener: () => {} };
const flush = () => new Promise(setImmediate);
const overview = (enabled) => ({
  latestRun: null,
  company: { enabled, status: enabled ? 'waiting' : 'stopped' },
});
function setup({ historical = false, briefFails = false } = {}) {
  const counters = { get: 0, brief: 0, command: 0 };
  let serverEnabled = true;
  const service = {
    get: async () => {
      counters.get++;
      return overview(serverEnabled);
    },
    brief: async (date) => {
      counters.brief++;
      if (briefFails) throw new Error('Synthetic brief failure');
      return { date, runs: [] };
    },
    news: async () => ({}),
    setCompanyEnabled: async (enabled) => {
      counters.command++;
      serverEnabled = enabled;
      return overview(enabled).company;
    },
  };
  const provider = new Provider({ children: null });
  provider.mounted = true;
  provider.context = service;
  provider.state = {
    ...provider.state,
    date: today,
    overview: overview(true),
    brief: { date: today, runs: [] },
    loading: false,
    fetchedAt: 1,
  };
  const page = new Page({});
  page.mounted = true;
  page.state = { ...page.state, date: historical ? '2020-01-01' : today };
  Object.defineProperty(page, 'context', {
    configurable: true,
    get: () => ({ ...provider.state, service, refresh: provider.refresh }),
  });
  return {
    provider,
    page,
    service,
    counters,
    serverEnabled: () => serverEnabled,
  };
}
async function run() {
  const results = [];
  const race = setup();
  let resolveOld;
  race.service.get = () => {
    race.counters.get++;
    if (race.counters.get === 1)
      return new Promise((resolve) => {
        resolveOld = resolve;
      });
    return Promise.resolve(overview(false));
  };
  const oldRefresh = race.provider.refresh();
  await race.page.controlCompany();
  resolveOld(overview(true));
  await oldRefresh;
  await flush();
  assert.equal(race.serverEnabled(), false);
  assert.equal(race.provider.state.overview.company.enabled, false);
  assert.equal(race.provider.state.error, null);
  assert.equal(race.counters.get, 2);
  results.push({
    scenario: 'Pre-command GET settles after successful pause',
    serverEnabled: race.serverEnabled(),
    displayedEnabled: race.provider.state.overview.company.enabled,
    error: race.provider.state.error,
    getCalls: race.counters.get,
    commandCalls: race.counters.command,
    fetchedAtAdvanced: race.provider.state.fetchedAt > 1,
  });

  const rejectedLate = setup();
  let rejectOld;
  rejectedLate.service.get = () => {
    rejectedLate.counters.get++;
    if (rejectedLate.counters.get === 1)
      return new Promise((resolve, reject) => {
        rejectOld = reject;
      });
    return Promise.resolve(overview(false));
  };
  const rejectedRefresh = rejectedLate.provider.refresh();
  await rejectedLate.page.controlCompany();
  rejectOld(new Error('old request failed'));
  await rejectedRefresh;
  await flush();
  assert.equal(rejectedLate.provider.state.overview.company.enabled, false);
  assert.equal(rejectedLate.provider.state.error, null);

  const commandFailure = setup();
  commandFailure.service.setCompanyEnabled = async () => {
    commandFailure.counters.command++;
    throw new Error('Synthetic command failure');
  };
  await commandFailure.page.controlCompany();
  await flush();
  assert.equal(commandFailure.counters.command, 1);
  assert.equal(commandFailure.page.state.error, 'Synthetic command failure');

  const pending = setup();
  let resolvePending;
  pending.service.get = () =>
    new Promise((resolve) => {
      resolvePending = resolve;
    });
  const pendingCommand = pending.page.controlCompany();
  await flush();
  assert.equal(pending.page.state.controlling, true);
  resolvePending(overview(false));
  await pendingCommand;
  await flush();
  assert.equal(pending.page.state.controlling, false);

  const timeout = setup();
  timeout.service.get = async () => overview(false);
  timeout.service.brief = (_date, signal) =>
    new Promise((_resolve, reject) => {
      signal.addEventListener('abort', () => reject(new Error('aborted')));
    });
  const timeoutCommand = timeout.page.controlCompany();
  await flush();
  timeout.provider.controller?.abort();
  await timeoutCommand;
  assert.equal(timeout.provider.state.overview.company.enabled, true);
  assert.equal(timeout.provider.state.fetchedAt, 1);
  assert.match(timeout.provider.state.error, /timed out/);

  const unmounted = setup();
  let resolveUnmounted;
  unmounted.service.get = () =>
    new Promise((resolve) => {
      resolveUnmounted = resolve;
    });
  const unmountedRefresh = unmounted.provider.refresh();
  unmounted.provider.componentWillUnmount();
  resolveUnmounted(overview(false));
  await unmountedRefresh;
  assert.equal(unmounted.provider.state.overview.company.enabled, true);

  const historical = setup({ historical: true });
  // Give history a known account so load() does not refresh solely to verify it.
  historical.provider.state.overview.latestRun = {
    id: 'run',
    accountKey: 'A',
    createdAt: '2026-10-25T00:00:00Z',
    updatedAt: '2026-10-25T00:00:00Z',
  };
  historical.provider.state.accountKey = 'A';
  await historical.page.controlCompany();
  await flush();
  assert.equal(historical.serverEnabled(), false);
  assert.equal(historical.provider.state.overview.company.enabled, false);
  assert.equal(historical.counters.get, 1);
  results.push({
    scenario: 'Pause from historical date',
    serverEnabled: historical.serverEnabled(),
    displayedEnabled: historical.provider.state.overview.company.enabled,
    getCalls: historical.counters.get,
    briefCalls: historical.counters.brief,
  });

  const partial = setup({ briefFails: true });
  const previousBrief = partial.provider.state.brief;
  await partial.page.controlCompany();
  await flush();
  await partial.provider.refresh();
  assert.equal(partial.serverEnabled(), false);
  assert.equal(partial.provider.state.overview.company.enabled, false);
  assert.equal(partial.counters.get, 2);
  assert.equal(partial.provider.state.fetchedAt, 1);
  assert.equal(partial.provider.state.brief, previousBrief);
  assert.equal(partial.provider.state.error, 'Synthetic brief failure');
  results.push({
    scenario: 'Fresh company overview with repeated independent brief failure',
    serverEnabled: partial.serverEnabled(),
    displayedEnabled: partial.provider.state.overview.company.enabled,
    error: partial.provider.state.error,
    getCalls: partial.counters.get,
  });
  const output = {
    note: 'Actual transpiled component methods with synchronous setState shim; verifies state transitions, not browser scheduling or layout.',
    results,
  };
  console.log(JSON.stringify(output, null, 2));
}
run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
