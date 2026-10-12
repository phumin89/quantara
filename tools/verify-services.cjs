const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');

require.extensions['.ts'] = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  });
  module._compile(output.outputText, filename);
};
const {
  RequestService,
} = require('../apps/web/shared/services/RequestService.ts');
const {
  PortfolioService,
} = require('../apps/web/features/crypto/services/PortfolioService.ts');
const {
  HistoryService,
} = require('../apps/web/features/crypto/services/HistoryService.ts');

async function verify() {
  const calls = [];
  let response = new Response(
    JSON.stringify({ trades: [], coverage: 'Fixture' }),
  );
  const requests = new RequestService('/api/', async function (url, options) {
    assert.equal(
      this,
      globalThis,
      'Browser fetch must keep its global receiver',
    );
    calls.push({ url, options });
    if (options.signal?.aborted)
      throw new DOMException('Aborted', 'AbortError');
    return response;
  });
  const history = new HistoryService(requests);
  const portfolio = new PortfolioService(requests);
  const controller = new AbortController();
  assert.deepEqual(await history.get('A&B', controller.signal), {
    trades: [],
    coverage: 'Fixture',
  });
  assert.equal(calls[0].url, '/api/crypto/history?coin=A%26B');
  assert.equal(calls[0].options.signal, controller.signal);
  assert.equal(calls[0].options.cache, 'no-store');
  assert.equal(calls[0].options.method, 'GET');
  response = new Response('', { status: 202 });
  await portfolio.sync(controller.signal);
  assert.equal(calls[1].url, '/api/crypto/sync');
  assert.equal(calls[1].options.method, 'POST');
  response = new Response(JSON.stringify({ error: 'Fixture failure' }), {
    status: 503,
  });
  await assert.rejects(history.get('BTC'), /Fixture failure/);
  response = new Response('{}', { status: 503 });
  await assert.rejects(portfolio.sync(), /Unable to sync Bitkub data/);
  response = new Response('{broken');
  await assert.rejects(history.get('BTC'), SyntaxError);
  response = new Response(JSON.stringify({ trades: 'invalid' }));
  await assert.rejects(history.get('BTC'), /History response is invalid/);
  response = new Response('{}');
  await assert.rejects(portfolio.get(), /Portfolio response is invalid/);
  assert.equal(calls.at(-1).url, '/api/crypto/portfolio');
  controller.abort();
  await assert.rejects(history.get('BTC', controller.signal), {
    name: 'AbortError',
  });
  const targets = [];
  const alternate = new RequestService(
    'https://service.invalid/api/',
    async (url) => {
      targets.push(url);
      return new Response('{}');
    },
  );
  await alternate.get('health?check=1', 'Fixture');
  await alternate.get('/health?check=2', 'Fixture');
  assert.deepEqual(targets, [
    'https://service.invalid/api/health?check=1',
    'https://service.invalid/api/health?check=2',
  ]);
  console.log(
    'Shared RequestService: transport, errors, cancellation and feature validation passed.',
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
