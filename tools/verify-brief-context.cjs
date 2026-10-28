// Deterministic component regression for historical Daily Brief account context.
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const assert = require('node:assert/strict');
const ts = require('typescript');

const root = path.resolve(__dirname, '..');
const file = path.join(
  root,
  'apps/web/features/team/components/daily-brief-page/DailyBriefPage.tsx',
);
const snapshotFile = path.join(
  root,
  'apps/web/features/team/presentation/TeamActivitySnapshot.ts',
);
const snapshot = new Module(snapshotFile, module);
snapshot.filename = snapshotFile;
snapshot._compile(
  ts.transpileModule(fs.readFileSync(snapshotFile, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  }).outputText,
  snapshotFile,
);
const originalLoad = Module._load;
const savedReact = global.React;
global.React = {
  createElement: (type, props, ...children) => ({
    type,
    props: { ...props, children },
  }),
};
let Page;
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
    if (request === '@mui/material')
      return new Proxy({}, { get: (_, key) => key });
    if (request.endsWith('/TeamActivityContext'))
      return {
        TeamActivityContext: { value: {}, require: (context) => context },
      };
    if (request.endsWith('/TeamActivitySnapshot')) return snapshot.exports;
    if (request.endsWith('/DailyBriefConversation'))
      return { default: 'DailyBriefConversation' };
    if (request.endsWith('/DateOnlyPicker'))
      return { DateOnlyPicker: 'DateOnlyPicker' };
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
  Page = mod.exports.default;
} finally {
  Module._load = originalLoad;
}

const report = (accountKey) => ({
  date: '2026-10-20',
  runs: [{ id: 'r1', accountKey, status: 'completed' }],
});
const activity = (state, refresh) => ({
  date: '2026-10-25',
  scopeRevision: state.scopeRevision,
  fetchedAt: 1,
  overview: state.overview,
  brief: null,
  loading: false,
  error: state.error,
  refresh,
  service: {
    brief: async () => state.brief ?? report('A'),
    news: async () => null,
  },
});
function find(node, type = 'DailyBriefConversation') {
  if (Array.isArray(node)) {
    for (const item of node) {
      const found = find(item, type);
      if (found) return found;
    }
    return null;
  }
  if (node?.type === type) return node.props;
  for (const child of node?.props?.children ?? []) {
    const found = find(child, type);
    if (found) return found;
  }
  return null;
}
async function make(state, refresh) {
  const page = new Page({});
  page.mounted = true;
  page.state.date = '2026-10-20';
  page.context = activity(state, async () => {
    await refresh();
    page.context.overview = state.overview;
    page.context.error = state.error;
  });
  await page.load();
  return { page, view: find(page.render()) };
}

function browserWindow(url) {
  const events = new EventTarget();
  const listeners = new Map();
  const browser = {
    location: new URL(url),
    history: {
      state: { __NA: true, tree: ['daily-brief'], preserved: 'context' },
      replaceState(state, _unused, nextUrl) {
        this.state = state;
        browser.location = new URL(nextUrl, browser.location.href);
      },
    },
    addEventListener(type, listener, options) {
      if (!listeners.has(type)) listeners.set(type, new Set());
      listeners.get(type).add(listener);
      events.addEventListener(type, listener, options);
    },
    removeEventListener(type, listener, options) {
      listeners.get(type)?.delete(listener);
      events.removeEventListener(type, listener, options);
    },
    dispatchEvent(event) {
      return events.dispatchEvent(event);
    },
    navigate(nextUrl, type) {
      this.location = new URL(nextUrl, this.location.href);
      events.dispatchEvent(new Event(type));
    },
    listenerCount() {
      return [...listeners.values()].reduce(
        (count, entries) => count + entries.size,
        0,
      );
    },
  };
  return browser;
}

async function runNavigation() {
  const browser = browserWindow(
    'http://127.0.0.1:3000/daily-brief?date=2026-10-20&view=team-chat&filter=open#run-00000000-0000-4000-8000-000000000001',
  );
  global.window = browser;
  const historyState = structuredClone(browser.history.state);
  const dates = [];
  const page = new Page({});
  page.context = activity(
    {
      scopeRevision: 0,
      overview: { latestRun: { accountKey: 'A' } },
      error: null,
    },
    async () => {},
  );
  page.context.service.brief = async (date) => {
    dates.push(date);
    return { ...report('A'), date };
  };
  const settle = () => new Promise((resolve) => setImmediate(resolve));
  page.componentDidMount();
  await settle();
  assert.equal(
    page.state.date,
    '2026-10-20',
    'initial query selects its report date',
  );
  assert.equal(dates.at(-1), '2026-10-20');

  find(page.render(), 'DateOnlyPicker').onChange('2026-10-21');
  await settle();
  assert.equal(
    browser.location.searchParams.get('date'),
    '2026-10-21',
    'date picker updates the reload/share URL',
  );
  assert.equal(
    browser.location.hash,
    '',
    'changing report date clears its old report hash',
  );
  assert.equal(browser.location.searchParams.get('view'), 'team-chat');
  assert.equal(
    browser.location.searchParams.get('filter'),
    'open',
    'date selection preserves unrelated query',
  );
  assert.deepEqual(
    browser.history.state,
    historyState,
    'date selection preserves router history state',
  );
  assert.equal(dates.at(-1), '2026-10-21');

  browser.navigate('?date=2026-10-20&view=team-chat&filter=open', 'popstate');
  await settle();
  assert.equal(
    page.state.date,
    '2026-10-20',
    'history date changes reload the selected day',
  );
  assert.equal(dates.at(-1), '2026-10-20');
  browser.navigate(
    '?date=2026-10-22&view=team-chat&filter=open',
    'quantara:route-change',
  );
  await settle();
  assert.equal(
    page.state.date,
    '2026-10-22',
    'client route changes reload the selected day',
  );
  assert.equal(dates.at(-1), '2026-10-22');
  browser.navigate('?date=2026-02-30', 'popstate');
  await settle();
  assert.equal(
    page.state.date,
    snapshot.exports.TeamActivitySnapshot.today(),
    'invalid navigated dates use the validated fallback',
  );

  page.componentWillUnmount();
  const reads = dates.length;
  const selectedDate = page.state.date;
  assert.equal(
    browser.listenerCount(),
    0,
    'page unmount removes location listeners',
  );
  browser.navigate('?date=2026-10-20', 'popstate');
  await settle();
  assert.equal(
    dates.length,
    reads,
    'unmounted pages do not load location changes',
  );
  assert.equal(page.state.date, selectedDate);
}

const savedWindow = global.window;
(async () => {
  const failed = await make(
    { scopeRevision: 0, overview: null, error: 'Status unavailable.' },
    async () => {},
  );
  assert.match(failed.page.state.error, /verified/i);
  assert.equal(failed.view.brief, null);

  const recoveredState = {
    scopeRevision: 0,
    overview: null,
    error: 'Status unavailable.',
  };
  const recovered = await make(recoveredState, async () => {
    recoveredState.overview = {
      accountKey: 'A',
      latestRun: { accountKey: 'A' },
    };
    recoveredState.error = null;
  });
  assert.equal(recovered.view.brief.runs.length, 1);

  const noLatest = await make(
    {
      scopeRevision: 0,
      overview: { accountKey: 'A', latestRun: null },
      error: null,
    },
    async () => {},
  );
  assert.match(noLatest.page.state.error, /verified/i);
  assert.equal(noLatest.view.brief, null);

  const staleState = {
    scopeRevision: 0,
    overview: { accountKey: 'A', latestRun: null },
    error: null,
  };
  const refreshed = await make(staleState, async () => {
    staleState.overview = { accountKey: 'A', latestRun: { accountKey: 'A' } };
  });
  assert.equal(
    refreshed.view.brief.runs.length,
    1,
    'Retry refreshes a missing latest run before checking archived reports.',
  );

  const empty = await make(
    {
      scopeRevision: 0,
      overview: { accountKey: 'A', latestRun: null },
      error: null,
      brief: { date: '2026-10-20', runs: [] },
    },
    async () => {},
  );
  assert.deepEqual(empty.view.brief.runs, []);

  const lateState = {
    scopeRevision: 0,
    overview: null,
    error: 'Status unavailable.',
  };
  let resolve;
  const late = new Page({});
  late.mounted = true;
  late.state.date = '2026-10-20';
  late.context = activity(
    lateState,
    () =>
      new Promise((done) => {
        resolve = done;
      }),
  );
  const old = late.load();
  const previous = late.context;
  late.context = activity(
    {
      scopeRevision: 1,
      overview: { accountKey: 'B', latestRun: { accountKey: 'B' } },
      error: null,
    },
    async () => {},
  );
  late.componentDidUpdate(previous);
  resolve();
  await old;
  assert.notEqual(late.state.brief?.runs[0]?.accountKey, 'A');
  await runNavigation();
  console.log(
    'brief context regression passed: failed refresh, recovery, missing latestRun, confirmed empty archive, late request guard, date URL/history navigation and listener cleanup',
  );
})()
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  })
  .finally(() => {
    global.React = savedReact;
    global.window = savedWindow;
  });
