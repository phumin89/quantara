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
console.log(
  'DI: class Context consumer, isolated overrides, required provider and scoped/stable instances passed.',
);
