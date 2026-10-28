const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

const before = process.argv.includes('--capture-before');
const compare = process.argv.includes('--compare-before');
assert(
  process.argv
    .slice(2)
    .every((value) => ['--capture-before', '--compare-before'].includes(value)),
);
assert(!(before && compare), 'Capture and comparison are separate phases.');
const origin = 'http://127.0.0.1:3107';
const directory = path.resolve(
  process.env.DESIGN_CAPTURE_DIR ?? 'work/hallmark-playful-review-20261027',
  before ? 'before' : 'after',
);
const routes = [
  ['home', '/', 'Home'],
  ['dashboard', '/dashboard', 'Dashboard'],
  ['crypto', '/crypto', 'Crypto'],
  ['brief', '/daily-brief', 'Daily Brief'],
];

async function verify() {
  await fs.mkdir(directory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const width of [1440, 768, 390, 320]) {
      for (const [name, routePath, heading] of routes) {
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          serviceWorkers: 'block',
          reducedMotion: 'reduce',
        });
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        const violations = [],
          writes = [],
          errors = [];
        page.on('pageerror', (error) => errors.push(error.message));
        await page.routeWebSocket('**/*', (socket) => {
          violations.push('Unexpected WebSocket: ' + socket.url());
          socket.close();
        });
        await page.route('**/*', async (route) => {
          const request = route.request(),
            address = new URL(request.url());
          try {
            assert.equal(address.origin, origin);
            if (request.method() !== 'GET')
              writes.push(request.method() + ' ' + address.pathname);
            assert.equal(
              request.method(),
              'GET',
              'Visual verification never writes.',
            );
            if (!address.pathname.startsWith('/api/'))
              return await staticBuildAsset(route, address);
            assert(
              Object.hasOwn(fixture.data, address.pathname),
              'Unmocked API: ' + address.pathname,
            );
            return await route.fulfill({
              json: structuredClone(fixture.data[address.pathname]),
            });
          } catch (error) {
            violations.push(error.message);
            await route.abort('blockedbyclient');
          }
        });
        try {
          await page.clock.install({ time: new Date(fixture.at) });
          await page.goto(origin + routePath + '?date=' + fixture.date);
          const main = page.locator('main');
          await main
            .getByRole('heading', { name: heading, exact: true })
            .waitFor();
          if (name === 'dashboard')
            await main
              .getByTestId('dashboard-equity')
              .getByText('THB 15,000.00', { exact: true })
              .waitFor();
          if (name === 'crypto')
            await main
              .getByText('Crypto holdings value', { exact: true })
              .waitFor();
          if (name === 'brief')
            await main
              .getByText('Daily reports', { exact: true })
              .first()
              .waitFor();
          if (name === 'home') {
            await page
              .locator('iframe')
              .first()
              .contentFrame()
              .locator('canvas')
              .first()
              .waitFor();
            const office = page
              .frames()
              .find((frame) => frame !== page.mainFrame());
            assert(office, 'The native office frame is mounted.');
            await office.waitForFunction(
              () => {
                const canvas = document.querySelector('canvas');
                const context = canvas?.getContext('2d');
                if (!context || canvas.width === 0 || canvas.height === 0)
                  return false;
                const { data } = context.getImageData(
                  0,
                  0,
                  canvas.width,
                  canvas.height,
                );
                const colors = new Set();
                const step = Math.max(1, Math.floor(canvas.width / 100));
                for (
                  let y = Math.floor(canvas.height * 0.2);
                  y < canvas.height * 0.9;
                  y += step
                ) {
                  for (
                    let x = Math.floor(canvas.width * 0.1);
                    x < canvas.width * 0.85;
                    x += step
                  ) {
                    const index = (y * canvas.width + x) * 4;
                    colors.add(
                      (data[index] << 16) |
                        (data[index + 1] << 8) |
                        data[index + 2],
                    );
                    if (colors.size >= 48) return true;
                  }
                }
                return false;
              },
              null,
              { polling: 100, timeout: 10000 },
            );
          }
          const collapse = page.getByRole('button', {
            name: 'Collapse team chat',
            exact: true,
          });
          const expand = page.getByRole('button', {
            name: 'Expand team chat',
            exact: true,
          });
          if (before) {
            if (await collapse.isVisible()) await collapse.click();
          } else {
            await expand.waitFor();
            assert.equal(
              await expand.getAttribute('aria-expanded'),
              'false',
              'Team chat starts closed at every viewport.',
            );
            assert.equal(await collapse.isVisible(), false);
          }
          await page.evaluate(() => document.fonts.ready);
          await page.evaluate(async () => {
            const landscape = new Image();
            landscape.src = '/landscapes/dusk-valley.jpg';
            await landscape.decode();
          });
          await page.clock.runFor(300);
          await page.evaluate(() => window.scrollTo(0, 0));
          const measured = await page.evaluate(() => {
            const main = document.querySelector('main'),
              header = document.querySelector('header');
            const style = getComputedStyle(header);
            return {
              pageWidth: document.documentElement.scrollWidth,
              font: getComputedStyle(main.querySelector('h1')).fontFamily,
              fontLoaded: document.fonts.check('800 16px Nunito'),
              headerBackground: style.backgroundColor,
              headerBlur: style.backdropFilter,
              landscape: getComputedStyle(document.body, '::before')
                .backgroundImage,
              panelBlur: getComputedStyle(
                document.querySelector('.MuiPaper-outlined'),
              ).backdropFilter,
              panelToken: getComputedStyle(document.documentElement)
                .getPropertyValue('--surface-panel')
                .trim(),
              titleWeight: Number(
                getComputedStyle(main.querySelector('h1')).fontWeight,
              ),
              testHooks: [...main.querySelectorAll('[data-testid]')]
                .map((element) => element.getAttribute('data-testid'))
                .sort(),
              links: [...main.querySelectorAll('a[href]')]
                .map((element) => element.getAttribute('href'))
                .sort(),
            };
          });
          await page.screenshot({
            path: path.join(directory, `${width}-${name}.png`),
            fullPage: true,
            animations: 'disabled',
          });
          assert(
            measured.pageWidth <= width + 1,
            `${width}px ${name}: no page overflow.`,
          );
          if (!before) {
            assert.match(measured.font, /Nunito/);
            assert.equal(
              measured.fontLoaded,
              true,
              'Self-hosted rounded font is loaded.',
            );
            assert.match(
              measured.headerBackground,
              /(?:rgba\(|\/)/,
              'The shared header is translucent.',
            );
            assert.match(
              measured.headerBlur,
              /blur\(/,
              'The shared header has actual backdrop blur.',
            );
            assert(measured.panelToken.length > 0);
            assert.match(measured.landscape, /dusk-valley\.jpg/);
            assert.match(measured.panelBlur, /blur\(22px\)/);
            assert(
              measured.titleWeight >= 600,
              'Dusk headings retain a clear hierarchy.',
            );
          }
          if (compare) {
            const previous = JSON.parse(
              await fs.readFile(
                path.resolve(directory, '../before/results.json'),
                'utf8',
              ),
            ).find((entry) => entry.width === width && entry.name === name);
            assert(previous, 'Matching before capture is required.');
            assert.deepEqual(
              measured.testHooks,
              previous.measured.testHooks,
              'Existing test hooks survive the redesign.',
            );
            assert.deepEqual(
              measured.links,
              previous.measured.links,
              'Existing page destinations survive the redesign.',
            );
          }
          assert.deepEqual(writes, []);
          assert.deepEqual(violations, []);
          assert.deepEqual(errors, []);
          if (!before && [1440, 390].includes(width) && name === 'crypto') {
            await main
              .getByRole('button', { name: 'Trading settings', exact: true })
              .click();
            await page
              .getByRole('dialog', { name: 'Trading settings', exact: true })
              .waitFor();
            await page.screenshot({
              path: path.join(directory, `${width}-settings.png`),
              fullPage: true,
              animations: 'disabled',
            });
            await page
              .getByRole('button', {
                name: 'Close trading settings',
                exact: true,
              })
              .click();
          }
          if (!before) {
            const chatGeometry = { closed: await main.boundingBox() };
            assert(chatGeometry.closed, 'The page has measurable geometry.');
            await expand.click();
            const chat = page.getByRole('dialog', {
              name: 'Team chat',
              exact: true,
            });
            await chat.waitFor();
            await page.clock.runFor(400);
            chatGeometry.open = await main.boundingBox();
            if ([1440, 390].includes(width) && name === 'brief') {
              await page.screenshot({
                path: path.join(directory, `${width}-chat.png`),
                fullPage: true,
                animations: 'disabled',
              });
            }
            await page.keyboard.press('Escape');
            await page.clock.runFor(300);
            await chat.waitFor({ state: 'hidden' });
            await expand.waitFor();
            chatGeometry.closedAgain = await main.boundingBox();
            for (const state of ['open', 'closedAgain']) {
              assert(
                chatGeometry[state],
                'Main remains mounted under the modal.',
              );
              for (const coordinate of ['x', 'y', 'width', 'height']) {
                assert(
                  Math.abs(
                    chatGeometry[state][coordinate] -
                      chatGeometry.closed[coordinate],
                  ) <= 1,
                  `${width}px ${name}: chat ${state} preserves main ${coordinate}.`,
                );
              }
            }
            measured.chatGeometry = chatGeometry;
          }
          assert.deepEqual(writes, []);
          assert.deepEqual(violations, []);
          assert.deepEqual(errors, []);
          results.push({
            name,
            routePath,
            width,
            measured,
            writes,
            violations,
            errors,
          });
        } finally {
          await context.close();
        }
        await fs.writeFile(
          path.join(directory, 'results.json'),
          JSON.stringify(results, null, 2),
        );
      }
    }
    console.log(
      `${before ? 'Before captures' : 'Playful design checks'}: ${results.length} route/viewport cases, explicit synthetic GET data only; before comparison ${compare ? 'checked' : 'not requested'}.`,
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
