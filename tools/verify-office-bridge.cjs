const assert = require('node:assert/strict');
const vm = require('node:vm');
const { build } = require('esbuild');

async function verify() {
  const result = await build({
    entryPoints: ['vendor/pixel-agents/integration/transport.ts'],
    bundle: true,
    format: 'cjs',
    platform: 'browser',
    write: false,
    plugins: [
      {
        name: 'fixture-assets',
        setup(builder) {
          builder.onResolve({ filter: /^virtual:/ }, () => ({
            path: 'assets',
            namespace: 'fixture',
          }));
          builder.onLoad({ filter: /.*/, namespace: 'fixture' }, () => ({
            contents: 'export default {};',
          }));
        },
      },
    ],
  });
  const messages = [],
    listeners = new Map(),
    delivered = [];
  const parent = {
    postMessage: (value, origin) => messages.push({ value, origin }),
  };
  const module = { exports: {} };
  vm.runInNewContext(result.outputFiles[0].text, {
    module,
    exports: module.exports,
    parent,
    location: { origin: 'http://fixture.local' },
    ResizeObserver: class {
      observe() {}
    },
    matchMedia: () => ({ matches: false, addEventListener() {} }),
    document: { hidden: false, addEventListener() {} },
    window: {
      addEventListener: (type, listener) => listeners.set(type, listener),
    },
  });
  const { transport, officeRoleLabel, officeMotionEnabled } = module.exports;
  transport.onMessage((message) => delivered.push(message));
  const send = (data, source = parent, origin = 'http://fixture.local') =>
    listeners.get('message')({ data, source, origin });
  const snapshot = {
    source: 'quantara-home',
    type: 'snapshot',
    selectedId: 4,
    paused: false,
    agents: Array.from({ length: 8 }, (_, index) => ({
      id: index + 1,
      label: `Role ${index + 1}`,
      status: index === 3 ? 'active' : 'unknown',
      accountKey: 'must-not-forward',
    })),
  };
  send(snapshot, {}, 'http://fixture.local');
  assert.equal(delivered.length, 0);
  send(snapshot, parent, 'http://untrusted.local');
  assert.equal(delivered.length, 0);
  for (const agents of [
    [...snapshot.agents.slice(1), null],
    snapshot.agents.slice(1),
    snapshot.agents.map((agent) => ({ ...agent, id: 1 })),
    snapshot.agents.map((agent) => ({ ...agent, status: 'working' })),
  ]) {
    send({ ...snapshot, agents });
    assert.equal(delivered.length, 0);
  }
  send(snapshot);
  assert.equal(
    delivered.filter((message) => message.type === 'agentStatus').length,
    8,
  );
  assert.equal(officeRoleLabel(4), 'Role 4: active');
  assert.equal(officeMotionEnabled(), true);
  assert(!JSON.stringify(delivered).includes('must-not-forward'));
  for (const type of [
    'launchAgent',
    'closeAgent',
    'saveLayout',
    'saveAgentSeats',
    'setHooksEnabled',
    'installHooks',
    'openFile',
    'terminalInput',
  ])
    transport.send({ type, id: 1 });
  assert.equal(
    messages.length,
    0,
    'Host/terminal/filesystem writes never leave the bridge.',
  );
  transport.send({ type: 'focusAgent', id: 999 });
  assert.equal(messages.length, 0);
  transport.send({ type: 'focusAgent', id: 4 });
  assert.equal(messages[0].value.type, 'select');
  send({ source: 'quantara-home', type: 'request-ready' });
  assert.equal(
    messages[1].value.type,
    'ready',
    'SSR handshake can replay readiness.',
  );
  send({ ...snapshot, paused: true });
  assert.equal(officeMotionEnabled(), false);
  console.log(
    'Office bridge: exact roles/statuses, origin/source/input checks, no private records, forbidden actions dropped, selection/readiness and motion gates passed.',
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
