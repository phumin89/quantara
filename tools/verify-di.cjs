const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToString } = require('react-dom/server');

const load = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
      jsx: ts.JsxEmit.ReactJSX,
    },
  });
  module._compile(output.outputText, filename);
};
require.extensions['.ts'] = load;
require.extensions['.tsx'] = load;
const {
  default: ServiceProvider,
} = require('../apps/web/app/providers/service-provider/ServiceProvider.tsx');
const {
  CryptoServiceContext,
} = require('../apps/web/features/crypto/context/CryptoServiceContext.ts');
const {
  ServiceContainer,
} = require('../apps/web/shared/di/ServiceContainer.ts');
const {
  ServiceBindings,
} = require('../apps/web/app/providers/service-provider/ServiceBindings.ts');
const {
  CryptoServices,
} = require('../apps/web/app/providers/service-provider/CryptoServices.ts');
const {
  ServiceKey,
} = require('../apps/web/app/providers/service-provider/ServiceKey.ts');

const seen = [];
class Consumer extends React.Component {
  static contextType = CryptoServiceContext.value;
  render() {
    seen.push(CryptoServiceContext.require(this.context));
    return React.createElement('span', null, 'Injected services');
  }
}
const services = {
  history: {
    async get() {
      return { trades: [] };
    },
  },
  portfolio: {
    async get() {
      throw new Error('Unused fixture');
    },
    async sync() {},
  },
};
const fetchDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'fetch');
try {
  // Overrides must not instantiate a transport or reach the network.
  Object.defineProperty(globalThis, 'fetch', {
    configurable: true,
    get() {
      throw new Error('Unexpected concrete transport creation');
    },
  });
  const output = renderToString(
    React.createElement(
      ServiceProvider,
      { services },
      React.createElement(Consumer),
    ),
  );
  assert.match(output, /Injected services/);
  assert.equal(seen.at(-1), services);
  // Mounting the real provider and passing its Context must not create a transport.
  renderToString(
    React.createElement(ServiceProvider, null, React.createElement(Consumer)),
  );
  assert.throws(
    () => renderToString(React.createElement(Consumer)),
    /Crypto services provider is required/,
  );
} finally {
  Object.defineProperty(globalThis, 'fetch', fetchDescriptor);
}
// Dependency identity stays stable through provider renders, and is scoped per provider.
const first = new ServiceProvider({ children: null });
const second = new ServiceProvider({ children: null });
assert.equal(first.render().props.value, first.render().props.value);
assert.notEqual(first.render().props.value, second.render().props.value);
const counts = { request: 0, portfolio: 0, history: 0, team: 0 };
const tracked = Object.fromEntries(
  Object.entries(ServiceBindings.factories).map(([key, factory]) => [
    key,
    (container) => {
      counts[key]++;
      return factory(container);
    },
  ]),
);
const container = new ServiceContainer(tracked);
const lazyServices = new CryptoServices(container);
assert.deepEqual(counts, { request: 0, portfolio: 0, history: 0, team: 0 });
assert.equal(lazyServices.portfolio, lazyServices.portfolio);
assert.deepEqual(counts, { request: 1, portfolio: 1, history: 0, team: 0 });
assert.equal(lazyServices.history, lazyServices.history);
assert.deepEqual(counts, { request: 1, portfolio: 1, history: 1, team: 0 });
assert.equal(
  container.resolve(ServiceKey.Request),
  container.resolve(ServiceKey.Request),
);
const other = new ServiceContainer(tracked);
assert.notEqual(other.resolve(ServiceKey.Portfolio), lazyServices.portfolio);
assert.deepEqual(counts, { request: 2, portfolio: 2, history: 1, team: 0 });
assert.equal(
  container.resolve(ServiceKey.Team),
  container.resolve(ServiceKey.Team),
);
assert.deepEqual(counts, { request: 2, portfolio: 2, history: 1, team: 1 });
assert.throws(() => container.resolve('missing'), /not registered/);
const circular = new ServiceContainer({
  first: (scope) => scope.resolve('second'),
  second: (scope) => scope.resolve('first'),
});
assert.throws(() => circular.resolve('first'), /Circular service dependency/);
assert.throws(() => circular.resolve('second'), /Circular service dependency/);
let attempts = 0;
const retry = new ServiceContainer({
  unstable: () => {
    attempts++;
    if (attempts === 1) throw new Error('Fixture constructor failed');
    return {};
  },
});
assert.throws(() => retry.resolve('unstable'), /Fixture constructor failed/);
assert.equal(retry.resolve('unstable'), retry.resolve('unstable'));
assert.equal(attempts, 2);
assert.throws(
  () => new ServiceContainer({ empty: () => undefined }).resolve('empty'),
  /returned no instance/,
);
const unusualKeys = new ServiceContainer({
  toString: () => ({}),
  ['__proto__']: () => ({}),
});
assert.equal(unusualKeys.resolve('toString'), unusualKeys.resolve('toString'));
assert.equal(
  unusualKeys.resolve('__proto__'),
  unusualKeys.resolve('__proto__'),
);
console.log(
  'Lazy DI: zero creation on mount, only used dependencies, cached scope, overrides, cycles and failed-constructor recovery passed.',
);
