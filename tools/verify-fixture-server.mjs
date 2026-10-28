import assert from 'node:assert/strict';

const url = process.env.TEST_URL;
assert.match(url ?? '', /^http:\/\/127\.0\.0\.1:(3108|3112)$/);
const health = await fetch(url + '/__fixture_health');
assert.equal(health.status, 200);
assert.equal(await health.text(), 'Quantara fixture frontend');

for (const [path, method, expected] of [
  ['/api/trading', 'GET', 404],
  ['/api/crypto/portfolio', 'GET', 404],
  ['/api/trading/activate', 'POST', 405],
  ['/crypto', 'POST', 405],
  ['/vendor/%252e%252e/api/trading', 'GET', 404],
  ['/vendor/%2e%2e%2fapi/trading', 'GET', 404],
  ['/vendor/%5c..%5capi/trading', 'GET', 404],
  ['/fonts/%252e%252e/api/trading', 'GET', 404],
  ['/landscapes/%5c..%5capi/trading', 'GET', 404],
  ['/vendor/%zz', 'GET', 400],
  ['/unknown', 'GET', 404],
]) {
  const response = await fetch(url + path, { method });
  assert.equal(response.status, expected, method + ' ' + path);
}
for (const [path, contentType] of [
  ['/fonts/nunito-variable.ttf', /font|octet-stream/],
  ['/landscapes/dusk-valley.jpg', /image\/jpeg/],
]) {
  const response = await fetch(url + path);
  assert.equal(response.status, 200, path);
  assert.match(response.headers.get('content-type') ?? '', contentType, path);
  assert((await response.arrayBuffer()).byteLength > 0, path);
}
console.log(
  'Fixture server: Dusk photo/font served; API, mutations, malformed paths and traversal blocked.',
);
