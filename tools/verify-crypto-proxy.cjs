const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
require.extensions['.ts'] = (module, filename) =>
  module._compile(
    ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
      },
    }).outputText,
    filename,
  );
const { CryptoProxy } = require('../apps/web/shared/api/CryptoProxy.ts');

async function verify() {
  const previous = global.fetch;
  const failures = [];
  let calls = 0;
  try {
    for (const method of ['GET', 'POST']) {
      for (const mode of ['transport', 'body']) {
        global.fetch = async (_url, options) => {
          calls++;
          assert.equal(options.method, method);
          if (mode === 'transport')
            throw new Error('Synthetic secret=never-display');
          return new Response(
            new ReadableStream({
              start(controller) {
                controller.error(new Error('Synthetic secret=never-display'));
              },
            }),
          );
        };
        const before = calls;
        const response = await CryptoProxy.request(
          method === 'POST' ? 'sync' : 'portfolio',
          method,
        );
        const body = await response.json();
        assert.equal(response.status, 503);
        assert.equal(
          calls,
          before + 1,
          'No failure automatically repeats a request.',
        );
        assert(!body.error.includes('never-display'));
        const expected =
          method === 'POST'
            ? 'The import request may have started. Refresh status before trying again.'
            : 'The portfolio service is unavailable. Refresh to try again.';
        if (body.error !== expected)
          failures.push(method + ' ' + mode + ': ' + body.error);
      }
    }
    for (const method of ['GET', 'POST']) {
      const payload = '{"synthetic":true}';
      global.fetch = async () =>
        new Response(payload, { status: method === 'POST' ? 202 : 200 });
      const response = await CryptoProxy.request('portfolio', method);
      assert.equal(response.status, method === 'POST' ? 202 : 200);
      assert.equal(await response.text(), payload);
      assert.equal(response.headers.get('cache-control'), 'no-store');
    }
    assert.deepEqual(failures, []);
    console.log(
      'Crypto proxy: GET recovery, uncertain import responses, body failures, redaction, no retry and success forwarding passed; fake fetch only.',
    );
  } finally {
    global.fetch = previous;
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
