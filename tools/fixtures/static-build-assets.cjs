const assert = require('node:assert/strict');
const path = require('node:path');

module.exports = async function staticBuildAsset(route, address) {
  const request = route.request();
  assert.equal(request.method(), 'GET', 'Built assets are read-only.');
  assert(
    !address.pathname.startsWith('/api/'),
    'API requests need an explicit fixture.',
  );
  const pathname = decodeURIComponent(address.pathname);
  let root;
  let relative;
  if (
    ['/', '/crypto', '/daily-brief', '/dashboard', '/team'].includes(pathname)
  ) {
    root = path.resolve('apps/web/.next/server/app');
    relative =
      (pathname === '/' ? 'index' : pathname.slice(1)) +
      (request.headers().rsc === '1' ? '.rsc' : '.html');
  } else if (pathname.startsWith('/_next/static/')) {
    root = path.resolve('apps/web/.next/static');
    relative = pathname.slice('/_next/static/'.length);
  } else {
    root = path.resolve('apps/web/public');
    relative = pathname.slice(1);
  }
  const file = path.resolve(root, relative);
  const withinRoot = path.relative(root, file);
  assert(
    withinRoot && !withinRoot.startsWith('..') && !path.isAbsolute(withinRoot),
  );
  await route.fulfill({
    path: file,
    ...(relative.endsWith('.rsc') ? { contentType: 'text/x-component' } : {}),
  });
};
