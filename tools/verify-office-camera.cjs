const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  const origin = process.env.TEST_URL;
  assert.match(origin || '', /^http:\/\/127\.0\.0\.1:(3000|3108)$/);
  const output = process.env.OFFICE_CAPTURE_DIR;
  assert(output, 'OFFICE_CAPTURE_DIR is required.');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 834, 1112],
      ['phone', 390, 844],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        hasTouch: device !== 'desktop',
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      const rejected = [];
      const errors = [];
      await context.route('**/*', (route) => {
        const request = route.request();
        const url = new URL(request.url());
        if (url.origin !== origin || request.method() !== 'GET') {
          rejected.push(`${request.method()} ${url.pathname}`);
          return route.abort();
        }
        if (!url.pathname.startsWith('/api/')) return route.continue();
        const data = fixture.data[url.pathname];
        if (!data) {
          rejected.push(url.pathname);
          return route.abort();
        }
        return route.fulfill({ status: 200, json: data });
      });
      const page = await context.newPage();
      page.on('pageerror', (error) => errors.push(error.message));
      await page.clock.install({ time: new Date(fixture.at) });
      await page.goto(origin);
      const collapse = page.getByRole('button', {
        name: 'Collapse team chat',
        exact: true,
      });
      await page.locator('iframe[data-ready="true"]').waitFor();
      const frame = page.frameLocator(
        'iframe[title="Pixel Agents research office"]',
      );
      const canvas = frame.getByTestId('office-canvas');
      await canvas.waitFor();
      await page.waitForLoadState('networkidle');
      if (await collapse.isVisible()) await collapse.click();
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .waitFor();
      const team = page.getByRole('region', { name: 'Team', exact: true });
      const ceo = team.getByRole('button', { name: /^CEO / });
      const settle = async () => {
        await canvas.evaluate(
          () =>
            new Promise((resolve) =>
              requestAnimationFrame(() => requestAnimationFrame(resolve)),
            ),
        );
      };
      const pixels = async () => {
        await page.mouse.move(0, 0);
        await settle();
        return createHash('sha256')
          .update(await canvas.evaluate((element) => element.toDataURL()))
          .digest('hex');
      };
      await ceo.click();
      await canvas.scrollIntoViewIfNeeded();
      const focused = await pixels();
      await canvas.dispatchEvent('wheel', {
        deltaX: 120,
        deltaY: 70,
        bubbles: true,
        cancelable: true,
      });
      const panned = await pixels();
      assert.notEqual(panned, focused, `${device}: manual pan moves the view.`);
      await canvas.screenshot({
        path: path.join(output, `${device}-panned.png`),
      });
      const refreshed = page.waitForResponse(
        (response) => new URL(response.url()).pathname === '/api/team',
      );
      await page
        .getByRole('button', { name: 'Refresh status', exact: true })
        .click();
      await refreshed;
      await page.waitForLoadState('networkidle');
      await canvas.scrollIntoViewIfNeeded();
      const afterRefresh = await pixels();
      await canvas.screenshot({
        path: path.join(output, `${device}-refreshed.png`),
      });
      assert.equal(
        afterRefresh,
        panned,
        `${device}: refreshing status preserves the manually panned view.`,
      );
      await ceo.click();
      await canvas.scrollIntoViewIfNeeded();
      const refocused = await pixels();
      assert.equal(
        refocused,
        focused,
        `${device}: selecting the same role refocuses.`,
      );
      const news = team.getByRole('button', { name: /^News / });
      await news.click();
      await canvas.scrollIntoViewIfNeeded();
      assert.notEqual(
        await pixels(),
        focused,
        `${device}: another role changes focus.`,
      );
      const nativeCeo = frame.getByRole('button', { name: /^CEO:/ });
      await nativeCeo.click();
      await team
        .locator('button[aria-pressed="true"]')
        .filter({ hasText: /^CEO/ })
        .waitFor();
      assert.equal(await ceo.getAttribute('aria-pressed'), 'true');
      await canvas.scrollIntoViewIfNeeded();
      assert.equal(
        await pixels(),
        focused,
        `${device}: native selection reaches the parent.`,
      );
      if (device !== 'desktop') {
        // Read native geometry only; the selection itself must use a real touch.
        const target = await canvas.evaluate((element) => {
          let fiber =
            element[
              Object.keys(element).find((key) =>
                key.startsWith('__reactFiber$'),
              )
            ];
          while (fiber && !fiber.memoizedProps?.officeState)
            fiber = fiber.return;
          if (!fiber) throw new Error('Native office geometry is unavailable.');
          const { officeState, zoom, panRef } = fiber.memoizedProps;
          const layout = officeState.getLayout();
          const dpr = window.devicePixelRatio || 1;
          const offsetX =
            Math.floor((element.width - layout.cols * 16 * zoom) / 2) +
            Math.round(panRef.current.x);
          const offsetY =
            Math.floor((element.height - layout.rows * 16 * zoom) / 2) +
            Math.round(panRef.current.y);
          for (const character of officeState.characters.values()) {
            if (character.id === officeState.selectedAgentId) continue;
            const worldY = character.y - 12;
            const x = (offsetX + character.x * zoom) / dpr;
            const y = (offsetY + worldY * zoom) / dpr;
            if (
              x > 0 &&
              x < element.clientWidth &&
              y > 0 &&
              y < element.clientHeight &&
              officeState.getCharacterAt(character.x, worldY) === character.id
            ) {
              return { id: character.id, x, y };
            }
          }
          throw new Error('No unselected character is visible for touch.');
        });
        const box = await canvas.boundingBox();
        assert(box, 'The native canvas is visible.');
        await page.touchscreen.tap(box.x + target.x, box.y + target.y);
        const labels = [
          'CEO',
          'Analyst',
          'Reviewer',
          'Intraday',
          'Swing',
          'News',
          'Positions',
          'Quant',
        ];
        const selectedRole = team.getByRole('button', {
          name: new RegExp(`^${labels[target.id - 1]} `),
        });
        await selectedRole
          .and(team.locator('[aria-pressed="true"]'))
          .waitFor({ timeout: 5000 });
        const touched = await pixels();
        await canvas.screenshot({
          path: path.join(output, `${device}-touch-canvas.png`),
        });
        await selectedRole.click();
        await canvas.scrollIntoViewIfNeeded();
        assert.equal(
          await pixels(),
          touched,
          `${device}: first touch focuses the same character as its role control.`,
        );
      }
      await page.evaluate(() => window.scrollTo(0, 0));
      await settle();
      await page.screenshot({
        path: path.join(output, `${device}-focused.png`),
        fullPage: false,
      });
      assert.deepEqual(
        rejected,
        [],
        'Every API read is mocked; writes and external requests are blocked.',
      );
      assert.deepEqual(errors, [], 'No browser runtime errors.');
      results.push({
        device,
        preservedPan: true,
        explicitRefocus: true,
        nativeSelection: true,
        firstTouchSelection: device !== 'desktop',
        rejected,
        errors,
      });
      await fs.writeFile(
        path.join(output, 'results.json'),
        JSON.stringify(results, null, 2),
      );
      await context.close();
    }
  } finally {
    await browser.close();
  }
  console.log(JSON.stringify(results));
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
