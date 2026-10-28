const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const Module = require('node:module');
const ts = require('typescript');

const componentPath = path.resolve(
  __dirname,
  '../apps/web/features/team/components/daily-brief-conversation/DailyBriefConversation.tsx',
);

function loadComponent(source) {
  const originalLoad = Module._load;
  const react = {
    Component: class {
      constructor(props) {
        this.props = props;
        this.state = {};
      }
      setState(update, callback) {
        this.state = {
          ...this.state,
          ...(typeof update === 'function' ? update(this.state) : update),
        };
        callback?.();
      }
    },
    createRef: () => ({ current: null }),
    createElement: (type, props, ...children) => ({
      type,
      props: { ...props, children },
    }),
  };
  global.React = react;
  Module._load = (request, parent, isMain) => {
    if (request === 'react') return react;
    if (request === '@mui/material')
      return new Proxy({}, { get: (_, key) => key });
    if (request.startsWith('@mui/icons-material/')) return 'Icon';
    if (request.endsWith('/TeamRole'))
      return {
        TeamRole: { Ceo: 'ceo', Analyst: 'analyst', Reviewer: 'reviewer' },
      };
    if (request.endsWith('/TeamPresentation'))
      return {
        TeamPresentation: {
          roleName: (role) => role,
          members: [],
          legacyMembers: [],
          statusStyle: () => ({}),
        },
      };
    if (request.endsWith('/BriefMessage')) return 'BriefMessage';
    if (request.endsWith('/ReportTable')) return 'ReportTable';
    return originalLoad.call(Module, request, parent, isMain);
  };
  try {
    const compiled = ts.transpileModule(source, {
      compilerOptions: {
        jsx: ts.JsxEmit.React,
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
      },
      fileName: componentPath,
    }).outputText;
    const mod = new Module(componentPath, module);
    mod.filename = componentPath;
    mod.paths = Module._nodeModulePaths(path.dirname(componentPath));
    mod._compile(compiled, componentPath);
    return mod.exports.default;
  } finally {
    Module._load = originalLoad;
  }
}

function brief(runs) {
  return {
    date: '2026-10-23',
    runs: runs.map((run) => ({
      id: run.id,
      createdAt: run.createdAt,
      updatedAt: run.createdAt,
      status: run.status,
      objective: run.id,
      steps: [
        { role: 'analyst', status: run.status, output: `output-${run.id}` },
      ],
    })),
  };
}

function props(briefValue, archive = true) {
  return {
    active: true,
    archive,
    preservePosition: true,
    brief: briefValue,
    error: null,
    date: briefValue.date,
    loading: false,
  };
}

function find(node, predicate, result = []) {
  if (Array.isArray(node)) {
    for (const item of node) find(item, predicate, result);
    return result;
  }
  if (!node || typeof node !== 'object') return result;
  if (predicate(node)) result.push(node);
  for (const child of node.props?.children ?? [])
    find(child, predicate, result);
  return result;
}

function browserWindow(url) {
  const events = new EventTarget();
  const listeners = new Map();
  const browser = {
    location: new URL(url),
    history: {
      state: { __NA: true, tree: ['daily-brief'], preserved: 'selection' },
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

function runBehavior(Component) {
  global.window = browserWindow(
    'http://127.0.0.1:3000/daily-brief?date=2026-10-23',
  );
  global.document = { getElementById: () => null };
  const oldRun = {
    id: '00000000-0000-4000-8000-000000000001',
    createdAt: '2026-10-23T01:00:00Z',
    status: 'failed',
  };
  const latestRun = {
    id: '00000000-0000-4000-8000-000000000002',
    createdAt: '2026-10-23T02:00:00Z',
    status: 'completed',
  };
  const instance = new Component(props(brief([latestRun, oldRun])));
  const viewport = { scrollTop: 0, scrollHeight: 2000, clientHeight: 500 };
  instance.viewport.current = viewport;
  instance.componentDidMount();
  let tree = instance.render();
  const select = find(tree, (node) => node.type === 'select')[0];
  assert.equal(
    select?.props.value,
    latestRun.id,
    'default is latest even when input is unsorted',
  );
  assert.deepEqual(
    find(tree, (node) => node.props?.id?.startsWith('run-')).map(
      (node) => node.props.id,
    ),
    ['run-' + latestRun.id],
  );
  assert.equal(
    find(tree, (node) => node.type === 'option').length,
    2,
    'all retained reports selectable',
  );
  viewport.scrollTop = 400;
  select.props.onChange({ target: { value: oldRun.id } });
  assert.equal(
    viewport.scrollTop,
    0,
    'selecting a report starts at its beginning',
  );
  viewport.scrollTop = 250;
  instance.props = props(
    brief([
      { ...oldRun },
      { ...latestRun, createdAt: '2026-10-23T03:00:00Z' },
      {
        id: '00000000-0000-4000-8000-000000000003',
        createdAt: '2026-10-23T04:00:00Z',
        status: 'completed',
      },
    ]),
  );
  instance.componentDidUpdate(props(brief([oldRun, latestRun])));
  tree = instance.render();
  assert.equal(
    find(tree, (node) => node.type === 'select')[0].props.value,
    oldRun.id,
    'polling preserves selection',
  );
  assert.equal(viewport.scrollTop, 250, 'polling preserves reading position');
  assert.deepEqual(
    find(tree, (node) => node.props?.id?.startsWith('run-')).map(
      (node) => node.props.id,
    ),
    ['run-' + oldRun.id],
  );
  global.window.location.hash = '#run-' + oldRun.id;
  const linked = new Component(props(brief([latestRun, oldRun])));
  linked.componentDidMount();
  assert.equal(
    linked.state.selectedRunId,
    oldRun.id,
    'deep link selects older report',
  );
  global.window.location.hash = '';
  const reset = new Component(props(brief([latestRun, oldRun])));
  reset.componentDidMount();
  assert.equal(
    reset.state.selectedRunId,
    latestRun.id,
    'new date/account keyed instance resets selection',
  );
  const embedded = new Component({
    ...props(brief([latestRun, oldRun]), false),
    embedded: true,
  });
  embedded.componentDidMount();
  const embeddedTree = embedded.render();
  assert.equal(find(embeddedTree, (node) => node.type === 'select').length, 0);
  assert.deepEqual(
    find(embeddedTree, (node) => node.props?.id?.startsWith('run-')).map(
      (node) => node.props.id,
    ),
    ['run-' + oldRun.id, 'run-' + latestRun.id],
    'sidebar retains chronological transcript',
  );
  const reportCount = (tree) =>
    find(
      tree,
      (node) =>
        Array.isArray(node.props?.children) &&
        /\b\d+ reports?\b/.test(node.props.children.join('')),
    ).map((node) => node.props.children.join('').match(/\d+ reports?/)?.[0]);
  for (const loading of [true, false]) {
    const unavailable = new Component({
      ...props(brief([])),
      brief: null,
      loading,
      error: loading ? null : 'Account context unavailable',
    });
    assert.equal(
      reportCount(unavailable.render()).length,
      0,
      'Unknown or loading report counts must not display zero.',
    );
  }
  const empty = new Component(props(brief([])));
  assert.deepEqual(
    reportCount(empty.render()),
    ['0 reports'],
    'A verified empty archive still shows its count.',
  );
  assert.deepEqual(
    reportCount(new Component(props(brief([latestRun]))).render()),
    ['1 report'],
    'A single report shows its count.',
  );
  assert.deepEqual(
    reportCount(new Component(props(brief([latestRun, oldRun]))).render()),
    ['2 reports'],
    'Two reports show their count.',
  );
  for (const mounted of [instance, linked, reset, embedded])
    mounted.componentWillUnmount?.();
  return true;
}

function runNavigation(Component) {
  const browser = browserWindow(
    'http://127.0.0.1:3000/daily-brief?date=2026-10-23&view=team-chat&filter=open',
  );
  global.window = browser;
  const historyState = structuredClone(browser.history.state);
  const oldRun = {
    id: '00000000-0000-4000-8000-000000000001',
    createdAt: '2026-10-23T01:00:00Z',
    status: 'failed',
  };
  const latestRun = {
    id: '00000000-0000-4000-8000-000000000002',
    createdAt: '2026-10-23T02:00:00Z',
    status: 'completed',
  };
  const reports = brief([latestRun, oldRun]);
  const instance = new Component(props(reports));
  const viewport = { scrollTop: 0, scrollHeight: 2000, clientHeight: 500 };
  instance.viewport.current = viewport;
  instance.componentDidMount();

  browser.navigate('#run-' + oldRun.id, 'hashchange');
  assert.equal(
    instance.state.selectedRunId,
    oldRun.id,
    'same-page hash navigation selects its report',
  );
  assert.deepEqual(
    find(instance.render(), (node) => node.props?.id?.startsWith('run-')).map(
      (node) => node.props.id,
    ),
    ['run-' + oldRun.id],
    'same-page deep link renders the requested report',
  );

  const select = find(instance.render(), (node) => node.type === 'select')[0];
  select.props.onChange({ target: { value: latestRun.id } });
  assert.equal(
    browser.location.hash,
    '#run-' + latestRun.id,
    'dropdown selection updates the shareable hash',
  );
  assert.equal(
    browser.location.searchParams.get('filter'),
    'open',
    'report selection preserves unrelated query',
  );
  assert.equal(
    browser.location.searchParams.get('date'),
    reports.date,
    'report selection preserves its date',
  );
  assert.deepEqual(
    browser.history.state,
    historyState,
    'report selection preserves router history state',
  );

  viewport.scrollTop = 250;
  browser.navigate(browser.location.href, 'quantara:route-change');
  const previousProps = instance.props;
  instance.props = props(brief([{ ...latestRun }, { ...oldRun }]));
  instance.componentDidUpdate(previousProps);
  assert.equal(
    instance.state.selectedRunId,
    latestRun.id,
    'same-hash route events and polling preserve dropdown choice',
  );
  assert.equal(
    viewport.scrollTop,
    250,
    'same-hash polling preserves the reading position',
  );

  browser.navigate('#run-' + oldRun.id, 'popstate');
  assert.equal(
    instance.state.selectedRunId,
    oldRun.id,
    'history navigation changes the selected report',
  );
  browser.navigate('#run-' + latestRun.id, 'quantara:route-change');
  assert.equal(
    instance.state.selectedRunId,
    latestRun.id,
    'client route changes select their report',
  );
  find(instance.render(), (node) => node.type === 'select')[0].props.onChange({
    target: { value: oldRun.id },
  });
  const latestButton = find(
    instance.render(),
    (node) =>
      node.type === 'Button' &&
      node.props.children.join('') === 'Latest report',
  )[0];
  assert.ok(latestButton, 'older selections offer Latest report');
  latestButton.props.onClick();
  assert.equal(instance.state.selectedRunId, latestRun.id);
  assert.equal(
    browser.location.hash,
    '#run-' + latestRun.id,
    'Latest report updates its hash',
  );
  assert.equal(browser.location.searchParams.get('filter'), 'open');
  assert.deepEqual(browser.history.state, historyState);

  instance.componentWillUnmount();
  assert.equal(
    browser.listenerCount(),
    0,
    'archive unmount removes location listeners',
  );
  browser.navigate('#run-' + oldRun.id, 'hashchange');
  assert.equal(
    instance.state.selectedRunId,
    latestRun.id,
    'unmounted archives ignore location events',
  );

  browser.navigate('#run-' + latestRun.id, 'hashchange');
  const pending = new Component({
    ...props(brief([])),
    brief: null,
    loading: true,
  });
  pending.componentDidMount();
  browser.navigate('#run-' + oldRun.id, 'hashchange');
  const pendingProps = pending.props;
  pending.props = props(reports);
  pending.componentDidUpdate(pendingProps);
  assert.equal(
    pending.state.selectedRunId,
    oldRun.id,
    'linked report remains pending until its brief is available',
  );
  pending.componentWillUnmount();
  assert.equal(
    browser.listenerCount(),
    0,
    'pending archive also removes listeners',
  );
}

const savedWindow = global.window;
const savedDocument = global.document;
const savedReact = global.React;
const current = loadComponent(fs.readFileSync(componentPath, 'utf8'));
try {
  runBehavior(current);
  runNavigation(current);
  console.log(
    'Brief selection: latest, same-page links, history/query preservation, pending links, listener cleanup, retained history, polling position, keyed reset and embedded transcript passed.',
  );
} finally {
  global.window = savedWindow;
  global.document = savedDocument;
  global.React = savedReact;
}
