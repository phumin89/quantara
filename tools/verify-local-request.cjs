const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');

const filename = 'apps/web/shared/api/LocalRequestPolicy.ts';
const compiled = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
  },
}).outputText;
const loaded = { exports: {} };
new Function('module', 'exports', compiled)(loaded, loaded.exports);
const { LocalRequestPolicy } = loaded.exports;
const original = process.env.QUANTARA_CONTAINER_RUNTIME;
const request = (hostname, host, origin, site) => ({
  nextUrl: new URL(`http://${hostname}:3000/api/team`),
  headers: new Headers({
    ...(host === null ? {} : { host }),
    ...(origin ? { origin } : {}),
    ...(site ? { 'sec-fetch-site': site } : {}),
  }),
});

try {
  delete process.env.QUANTARA_CONTAINER_RUNTIME;
  assert(LocalRequestPolicy.allows(request('127.0.0.1', '127.0.0.1:3000')));
  assert(!LocalRequestPolicy.allows(request('0.0.0.0', '127.0.0.1:3000')));
  process.env.QUANTARA_CONTAINER_RUNTIME = 'true';
  assert(LocalRequestPolicy.allows(request('0.0.0.0', '127.0.0.1:3000')));
  assert(
    LocalRequestPolicy.allows(
      request('0.0.0.0', 'localhost:3000', 'http://localhost:3000'),
    ),
  );
  for (const denied of [
    request('0.0.0.0', null),
    request('0.0.0.0', 'evil.test:3000'),
    request('evil.test', '127.0.0.1:3000'),
    request('0.0.0.0', '127.0.0.1:99999'),
    request('0.0.0.0', '127.0.0.1:3000', 'https://evil.test'),
    request('0.0.0.0', '127.0.0.1:3000', 'http://127.0.0.1:3001'),
    request('0.0.0.0', '127.0.0.1:3000', null, 'cross-site'),
  ]) {
    assert(!LocalRequestPolicy.allows(denied));
  }
  console.log(
    'Local request policy: native/container ingress and hostile origins/hosts passed.',
  );
} finally {
  if (original === undefined) delete process.env.QUANTARA_CONTAINER_RUNTIME;
  else process.env.QUANTARA_CONTAINER_RUNTIME = original;
}
