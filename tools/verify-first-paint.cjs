const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');

async function verify() {
  const origin = 'http://127.0.0.1:3108';
  const output =
    process.env.FIRST_PAINT_CAPTURE_DIR || '.impeccable/review/first-paint';
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const width of [320, 390, 768, 1024, 1440]) {
      const context = await browser.newContext({
        viewport: { width, height: 900 },
        javaScriptEnabled: false,
        serviceWorkers: 'block',
      });
      const rejected = [];
      await context.route('**/*', async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        if (
          url.origin !== origin ||
          request.method() !== 'GET' ||
          url.pathname.startsWith('/api/')
        ) {
          rejected.push(`${request.method()} ${url.pathname}`);
          return route.abort();
        }
        let root;
        let relative;
        if (
          ['/', '/dashboard', '/daily-brief', '/crypto'].includes(url.pathname)
        ) {
          root = path.resolve('apps/web/.next/server/app');
          relative =
            (url.pathname === '/' ? 'index' : url.pathname.slice(1)) + '.html';
        } else if (url.pathname.startsWith('/_next/static/')) {
          root = path.resolve('apps/web/.next/static');
          relative = decodeURIComponent(
            url.pathname.slice('/_next/static/'.length),
          );
        } else {
          root = path.resolve('apps/web/public');
          relative = decodeURIComponent(url.pathname.slice(1));
        }
        const file = path.resolve(root, relative);
        assert(
          file.startsWith(root + path.sep),
          'Static files stay inside their build directory.',
        );
        return route.fulfill({ path: file });
      });
      const page = await context.newPage();
      for (const route of ['/', '/dashboard', '/daily-brief', '/crypto']) {
        await page.goto(origin + route);
        const header = page.getByRole('banner');
        await header.waitFor();
        const metrics = await header.evaluate((element) => ({
          headerWidth: element.clientWidth,
          headerScrollWidth: element.scrollWidth,
          pageWidth: document.documentElement.scrollWidth,
          viewportWidth: innerWidth,
        }));
        await page.screenshot({
          path: path.join(
            output,
            `${width}-${route === '/' ? 'home' : route.slice(1)}.png`,
          ),
        });
        results.push({ width, route, ...metrics });
        await fs.writeFile(
          path.join(output, 'results.json'),
          JSON.stringify(results, null, 2),
        );
        assert(
          metrics.headerScrollWidth <= metrics.headerWidth + 1,
          `${width} ${route}: header fits before hydration.`,
        );
        assert(
          metrics.pageWidth <= metrics.viewportWidth + 1,
          `${width} ${route}: no first-paint page overflow.`,
        );
        assert.equal(
          await page
            .getByRole('button', { name: 'Open menu', exact: true })
            .isVisible(),
          width < 1024,
        );
        assert.equal(
          await page
            .getByRole('navigation', { name: 'Main navigation', exact: true })
            .isVisible(),
          width >= 1024,
        );
      }
      assert.deepEqual(
        rejected,
        [],
        'Prerender checks make no API, write or external requests.',
      );
      await context.close();
    }
  } finally {
    await browser.close();
  }
  console.log(
    `First paint: ${results.length} route/viewport cases fit before hydration; static files only.`,
  );
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
