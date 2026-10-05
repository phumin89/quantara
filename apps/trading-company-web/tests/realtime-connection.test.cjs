const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const ts = require("typescript");

const source = fs.readFileSync(require.resolve("../app/realtimeConnection.ts"), "utf8");
const exportsObject = {};
new Function("exports", "module", ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2019 },
}).outputText)(exportsObject, { exports: exportsObject });
const { startRealtimeConnection } = exportsObject;

function fakeHub() {
  const handlers = {};
  return {
    state: "Disconnected", starts: [], stopCalls: 0, handlers,
    onclose(fn) { handlers.close = fn; },
    onreconnecting(fn) { handlers.reconnecting = fn; },
    onreconnected(fn) { handlers.reconnected = fn; },
    start() { const promise = this.starts.shift(); this.state = "Connecting"; return promise.finally(() => { this.state = "Disconnected"; }); },
    stop() { this.stopCalls++; this.state = "Disconnected"; return Promise.resolve(); },
  };
}

test("disposal during negotiation stops after start settles", async () => {
  const hub = fakeHub(); let resolve;
  hub.starts.push(new Promise(r => { resolve = r; }));
  const states = []; const dispose = startRealtimeConnection(hub, s => states.push(s));
  dispose(); assert.equal(hub.stopCalls, 0);
  resolve(); await new Promise(setImmediate);
  assert.equal(hub.stopCalls, 1); assert.deepEqual(states, []);
});

test("initial rejection retries then connects without wall-clock waiting", async () => {
  const hub = fakeHub(); const states = []; const oldSetTimeout = global.setTimeout;
  let retry; global.setTimeout = fn => { retry = fn; return 1; };
  try {
    hub.starts.push(Promise.reject(new Error("offline")), Promise.resolve());
    const dispose = startRealtimeConnection(hub, s => states.push(s));
    await new Promise(setImmediate); retry(); await new Promise(setImmediate);
    assert.deepEqual(states, ["offline", "connected"]); dispose();
  } finally { global.setTimeout = oldSetTimeout; }
});

test("onclose schedules retry after automatic reconnect is exhausted", async () => {
  const hub = fakeHub(); let retry; const oldSetTimeout = global.setTimeout;
  global.setTimeout = fn => { retry = fn; return 1; };
  try {
    hub.starts.push(Promise.resolve(), Promise.resolve());
    const states = []; const dispose = startRealtimeConnection(hub, s => states.push(s));
    await new Promise(setImmediate); hub.handlers.close(); assert.equal(typeof retry, "function");
    retry(); await new Promise(setImmediate); assert.deepEqual(states, ["connected", "offline", "connected"]); dispose();
  } finally { global.setTimeout = oldSetTimeout; }
});

test("callbacks after disposal are ignored and stop rejection is handled", async () => {
  const hub = fakeHub(); let resolve; hub.starts.push(new Promise(r => { resolve = r; }));
  hub.stop = () => { hub.stopCalls++; return Promise.reject(new Error("stop failed")); };
  const states = []; const dispose = startRealtimeConnection(hub, s => states.push(s));
  dispose(); hub.handlers.reconnecting(); hub.handlers.reconnected(); hub.handlers.close(); resolve();
  await new Promise(setImmediate);
  assert.deepEqual(states, []); assert.equal(hub.stopCalls, 1);
});
