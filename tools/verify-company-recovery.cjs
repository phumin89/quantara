// Executes actual transpiled component methods with in-process synthetic services.
// No browser, server, network, account or financial write is used.
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');
const assert = require('node:assert/strict');
const { mock } = require('node:test');
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
const overview = (enabled, accountKey = null) => ({
  latestRun: accountKey
    ? {
        id: `run-${accountKey}`,
        accountKey,
        createdAt: '2026-10-25T00:00:00Z',
        updatedAt: '2026-10-25T00:00:00Z',
      }
    : null,
  company: { enabled, status: enabled ? 'waiting' : 'stopped' },
});
function setup({
  historical = false,
  briefFails = false,
  accountKey = null,
  enabled = true,
} = {}) {
  const counters = { get: 0, brief: 0, command: 0 };
  let serverEnabled = enabled;
  const previousBrief = {
    date: today,
    runs: accountKey
      ? [{ id: `run-${accountKey}`, accountKey, status: 'completed' }]
      : [],
  };
  const service = {
    get: async () => {
      counters.get++;
      return overview(serverEnabled, accountKey);
    },
    brief: async (date) => {
      counters.brief++;
      if (briefFails) throw new Error('Synthetic brief failure');
      return { ...previousBrief, date };
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
    overview: overview(enabled, accountKey),
    brief: previousBrief,
    loading: false,
    fetchedAt: 1,
    accountKey,
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
  let resolveOldBrief;
  race.service.get = () => {
    race.counters.get++;
    if (race.counters.get === 1)
      return new Promise((resolve) => {
        resolveOld = resolve;
      });
    return Promise.resolve(overview(false));
  };
  race.service.brief = async (date) => {
    race.counters.brief++;
    if (race.counters.brief === 1)
      return new Promise((resolve) => {
        resolveOldBrief = resolve;
      });
    return { date, runs: [] };
  };
  const oldRefresh = race.provider.refresh();
  await race.page.controlCompany();
  const currentState = race.provider.state;
  resolveOld(overview(true));
  resolveOldBrief({ date: today, runs: [{ id: 'old', accountKey: 'old' }] });
  await oldRefresh;
  await flush();
  assert.equal(race.provider.state, currentState);
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
  const recoveredState = rejectedLate.provider.state;
  rejectOld(new Error('old request failed'));
  await rejectedRefresh;
  await flush();
  assert.equal(rejectedLate.provider.state, recoveredState);
  assert.equal(rejectedLate.provider.state.overview.company.enabled, false);
  assert.equal(rejectedLate.provider.state.error, null);
  results.push({
    scenario: 'Late failed pre-command read cannot overwrite recovered state',
    displayedEnabled: rejectedLate.provider.state.overview.company.enabled,
    error: rejectedLate.provider.state.error,
  });

  const commandFailure = setup();
  commandFailure.service.setCompanyEnabled = async () => {
    commandFailure.counters.command++;
    throw new Error('Synthetic command failure');
  };
  await commandFailure.page.controlCompany();
  await flush();
  assert.equal(commandFailure.counters.get, 1);
  assert.equal(commandFailure.counters.brief, 1);
  assert.equal(commandFailure.page.state.controlling, false);
  assert.equal(commandFailure.provider.state.overview.company.enabled, true);
  await commandFailure.provider.refresh();
  assert.equal(commandFailure.counters.get, 2);
  assert.equal(commandFailure.counters.command, 1);
  assert.equal(commandFailure.page.state.error, 'Synthetic command failure');
  results.push({
    scenario: 'Failed command refreshes reads without resubmission',
    getCalls: commandFailure.counters.get,
    commandCalls: commandFailure.counters.command,
    error: commandFailure.page.state.error,
  });

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

  for (const enabled of [true, false]) {
    mock.timers.enable({ apis: ['setTimeout'] });
    try {
      const timeout = setup({ accountKey: 'A', enabled });
      const timeoutBrief = timeout.provider.state.brief;
      assert.equal(timeoutBrief.runs[0].accountKey, 'A');
      timeout.service.brief = (_date, signal) => {
        timeout.counters.brief++;
        return new Promise((_resolve, reject) => {
          signal.addEventListener('abort', () => reject(new Error('aborted')));
        });
      };
      const timeoutCommand = timeout.page.controlCompany();
      await flush();
      assert.ok(timeout.provider.controller);
      const controller = timeout.provider.controller;
      mock.timers.tick(7999);
      await flush();
      assert.equal(controller.signal.aborted, false);
      assert.equal(timeout.page.state.controlling, true);
      assert.equal(timeout.page.state.loading, true);
      mock.timers.tick(1);
      assert.equal(controller.signal.aborted, true);
      await timeoutCommand;
      // A verified overview survives a brief timeout; the brief stays stale.
      assert.equal(timeout.serverEnabled(), !enabled);
      assert.equal(timeout.provider.state.overview.company.enabled, !enabled);
      assert.equal(timeout.provider.state.brief, timeoutBrief);
      assert.equal(timeout.provider.state.fetchedAt, 1);
      assert.match(timeout.provider.state.error, /timed out/);
      assert.equal(timeout.provider.state.loading, false);
      assert.equal(timeout.page.state.loading, false);
      assert.equal(timeout.page.state.controlling, false);
      assert.equal(timeout.page.state.error, null);
      assert.equal(timeout.counters.command, 1);
      assert.equal(timeout.counters.get, 1);
      assert.equal(timeout.counters.brief, 1);
      results.push({
        scenario: `Successful ${enabled ? 'pause' : 'resume'} and 8-second brief timeout`,
        displayedEnabled: timeout.provider.state.overview.company.enabled,
        briefRetained: timeout.provider.state.brief === timeoutBrief,
        fetchedAt: timeout.provider.state.fetchedAt,
        controlling: timeout.page.state.controlling,
        commandCalls: timeout.counters.command,
        error: timeout.provider.state.error,
      });
    } finally {
      mock.timers.reset();
    }
  }

  const overviewFailure = setup({ accountKey: 'A' });
  const verifiedState = overviewFailure.provider.state;
  overviewFailure.service.get = async () => {
    overviewFailure.counters.get++;
    throw new Error('Synthetic overview failure');
  };
  overviewFailure.service.brief = async (date) => {
    overviewFailure.counters.brief++;
    return { date, runs: [{ id: 'unverified', accountKey: 'B' }] };
  };
  await overviewFailure.page.controlCompany();
  assert.equal(overviewFailure.serverEnabled(), false);
  assert.equal(overviewFailure.counters.brief, 1);
  assert.equal(overviewFailure.provider.state.overview, verifiedState.overview);
  assert.equal(overviewFailure.provider.state.brief, verifiedState.brief);
  assert.equal(overviewFailure.provider.state.fetchedAt, 1);
  assert.equal(overviewFailure.provider.state.accountKey, 'A');
  assert.equal(overviewFailure.provider.state.scopeRevision, 0);
  assert.equal(
    overviewFailure.provider.state.error,
    'Synthetic overview failure',
  );
  assert.equal(overviewFailure.page.state.controlling, false);
  assert.equal(overviewFailure.counters.command, 1);
  results.push({
    scenario: 'Failed overview cannot trust a successful brief',
    displayedEnabled: overviewFailure.provider.state.overview.company.enabled,
    retainedAccount: overviewFailure.provider.state.accountKey,
    fetchedAt: overviewFailure.provider.state.fetchedAt,
    error: overviewFailure.provider.state.error,
  });

  for (const outcome of ['fulfilled', 'rejected']) {
    const unmounted = setup({ accountKey: 'A' });
    let resolveUnmounted;
    let rejectUnmounted;
    unmounted.service.get = () =>
      new Promise((resolve, reject) => {
        resolveUnmounted = resolve;
        rejectUnmounted = reject;
      });
    const unmountedRefresh = unmounted.provider.refresh();
    unmounted.provider.componentWillUnmount();
    const unmountedState = unmounted.provider.state;
    if (outcome === 'fulfilled') {
      resolveUnmounted(overview(false, 'B'));
    } else {
      rejectUnmounted(new Error('Late unmounted failure'));
    }
    await unmountedRefresh;
    assert.equal(unmounted.provider.state, unmountedState);
    assert.equal(unmounted.provider.controller, null);
    assert.equal(unmounted.provider.pending, null);
    results.push({
      scenario: `Unmount ignores ${outcome} read completion`,
      retainedAccount: unmounted.provider.state.accountKey,
      fetchedAt: unmounted.provider.state.fetchedAt,
    });
  }

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

  for (const enabled of [true, false]) {
    const partial = setup({ briefFails: true, accountKey: 'A', enabled });
    const previousBrief = partial.provider.state.brief;
    await partial.page.controlCompany();
    await flush();
    await partial.provider.refresh();
    assert.equal(partial.serverEnabled(), !enabled);
    assert.equal(partial.provider.state.overview.company.enabled, !enabled);
    assert.equal(partial.counters.get, 2);
    assert.equal(partial.provider.state.fetchedAt, 1);
    assert.equal(partial.provider.state.brief, previousBrief);
    assert.equal(partial.provider.state.error, 'Synthetic brief failure');
    assert.equal(partial.provider.state.loading, false);
    assert.equal(partial.page.state.loading, false);
    assert.equal(partial.page.state.controlling, false);
    assert.equal(partial.counters.command, 1);
    results.push({
      scenario: `Verified ${enabled ? 'pause' : 'resume'} with repeated brief failure`,
      serverEnabled: partial.serverEnabled(),
      displayedEnabled: partial.provider.state.overview.company.enabled,
      error: partial.provider.state.error,
      getCalls: partial.counters.get,
      commandCalls: partial.counters.command,
    });
  }

  const changedAccount = setup({ briefFails: true, accountKey: 'A' });
  assert.equal(changedAccount.provider.state.brief.runs[0].accountKey, 'A');
  changedAccount.service.get = async () => {
    changedAccount.counters.get++;
    return overview(false, 'B');
  };
  await changedAccount.page.controlCompany();
  assert.equal(changedAccount.provider.state.overview.company.enabled, false);
  assert.equal(changedAccount.provider.state.accountKey, 'B');
  assert.equal(changedAccount.provider.state.scopeRevision, 1);
  assert.equal(changedAccount.provider.state.brief, null);
  assert.equal(changedAccount.provider.state.fetchedAt, null);
  assert.equal(changedAccount.provider.state.error, 'Synthetic brief failure');
  assert.equal(changedAccount.page.state.controlling, false);
  assert.equal(changedAccount.counters.command, 1);
  results.push({
    scenario: 'Account change clears the old brief despite a new brief failure',
    accountKey: changedAccount.provider.state.accountKey,
    scopeRevision: changedAccount.provider.state.scopeRevision,
    brief: changedAccount.provider.state.brief,
    fetchedAt: changedAccount.provider.state.fetchedAt,
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
