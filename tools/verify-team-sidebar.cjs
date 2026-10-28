const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/readable-team-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

async function motion(page, opening, reduced) {
  await page.emulateMedia({
    reducedMotion: reduced ? 'reduce' : 'no-preference',
  });
  const control = page.locator(
    opening
      ? 'button[aria-label="Expand team chat"]'
      : 'button[aria-label="Collapse team chat"]',
  );
  if (opening) await control.focus();
  const sample = () =>
    page.locator('[aria-label="Team conversation"]').evaluate((sidebar) => {
      const paper = sidebar.closest('.MuiDrawer-paper');
      const content = sidebar.querySelector('#team-chat-content');
      const style = getComputedStyle(paper);
      const bounds = paper.getBoundingClientRect();
      return {
        x: bounds.x,
        width: bounds.width,
        visible:
          style.visibility !== 'hidden' &&
          style.display !== 'none' &&
          bounds.right > 0 &&
          bounds.left < innerWidth,
        contentVisible:
          content !== null &&
          !content.hidden &&
          getComputedStyle(content).display !== 'none' &&
          getComputedStyle(content).visibility !== 'hidden',
        duration: Math.max(
          ...style.transitionDuration
            .split(',')
            .map((value) =>
              value.trim().endsWith('ms')
                ? parseFloat(value) / 1000
                : parseFloat(value),
            ),
        ),
      };
    });
  const frames = [await sample()];
  await control.evaluate((button) => button.click());
  // Wall-clock samples observe CSS transitions; no fake-time jump skips frames.
  for (let frame = 0; frame < 28; frame++) {
    await page.waitForTimeout(20);
    frames.push(await sample());
  }
  const minimum = Math.min(...frames.map((frame) => frame.x));
  const maximum = Math.max(...frames.map((frame) => frame.x));
  const intermediate = frames.filter(
    (frame) => frame.x > minimum + 2 && frame.x < maximum - 2,
  );
  if (reduced) {
    assert.equal(
      intermediate.length,
      0,
      'Reduced motion changes state without intermediate panel movement.',
    );
    assert(
      frames.every((frame) => frame.duration <= 0.001),
      'Reduced motion removes the native Slide transition duration.',
    );
  } else {
    assert(
      maximum - minimum >= frames[0].width * 0.8,
      'Native Slide travels between its off-screen and open positions.',
    );
    assert(
      intermediate.some((frame) => frame.visible && frame.contentVisible),
      'Opening and closing both paint visible content at intermediate positions.',
    );
  }
  const conversation = page.locator(
    '[aria-label="Team conversation"] [role="region"][aria-label="Daily conversation"]',
  );
  await conversation.waitFor({ state: opening ? 'visible' : 'hidden' });
  return { opening, reduced, frames };
}

async function verify() {
  const url = process.env.TEST_URL;
  assert.match(url ?? '', /^http:\/\/127\.0\.0\.1:(3000|3108)$/);
  const output = process.env.SIDEBAR_CAPTURE_DIR;
  assert(output);
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const writes = [],
    escaped = [],
    errors = [],
    captures = [],
    results = [];
  try {
    for (const [device, width, height] of [
      ['wide-desktop', 1920, 1080],
      ['desktop', 1440, 1000],
      ['small-desktop', 1024, 768],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
      ['small-mobile', 320, 740],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(10000);
      let count = 3,
        account = 'a',
        hangOverview = false,
        hangBrief = false;
      const held = [];
      await page.routeWebSocket('**/*', (socket) => {
        escaped.push('Unexpected WebSocket: ' + socket.url());
        socket.close();
      });
      await context.route('**/*', async (route) => {
        const request = route.request(),
          address = new URL(request.url());
        if (address.origin !== url) {
          escaped.push(address.origin);
          await route.abort();
          return;
        }
        if (request.method() !== 'GET') {
          writes.push(address.pathname);
          await route.fulfill({
            status: 405,
            json: { error: 'Synthetic reads only' },
          });
          return;
        }
        if (!address.pathname.startsWith('/api/')) {
          if (process.env.STATIC_BUILD === '1') {
            await staticBuildAsset(route, address);
          } else {
            await route.continue();
          }
          return;
        }
        let body = structuredClone(fixture.base.data[address.pathname]);
        const runs = fixture.runs(count, account);
        if (address.pathname === '/api/team') {
          if (hangOverview) {
            held.push(route);
            return;
          }
          body.latestRun = runs.at(-1);
        }
        if (address.pathname === '/api/team/brief') {
          if (hangBrief) {
            held.push(route);
            return;
          }
          body = { date: address.searchParams.get('date'), runs };
        }
        if (
          address.pathname === '/api/trading' ||
          address.pathname === '/api/crypto/risk'
        ) {
          body.accountKey = 'synthetic-readable-' + account;
          if (body.capital) body.capital.accountKey = body.accountKey;
        }
        await route.fulfill({
          status: body ? 200 : 404,
          json: body ?? { error: 'Unmocked synthetic route' },
        });
      });
      page.on('pageerror', (error) => errors.push(error.message));
      await page.clock.install({ time: new Date(fixture.at) });
      await page.goto(url + '/daily-brief');
      // DOM locators retain access while the native modal hides background roles.
      const sidebar = page.locator('[aria-label="Team conversation"]');
      const conversation = sidebar.locator(
        '[role="region"][aria-label="Daily conversation"]',
      );
      const paper = page.locator('.MuiDrawer-paper').filter({ has: sidebar });
      const dialog = () =>
        page.getByRole('dialog', { name: 'Team chat', exact: true });
      const expand = () =>
        page.getByRole('button', { name: 'Expand team chat', exact: true });
      const collapse = () =>
        page.getByRole('button', { name: 'Collapse team chat', exact: true });
      const open = async () => {
        if (await expand().isVisible()) await expand().click();
        await page.clock.runFor(400);
        await conversation.waitFor();
        assert.equal(await conversation.count(), 1);
        assert.equal(await dialog().count(), 1);
        assert.equal(
          await page
            .locator('header button[aria-label="Toggle team chat"]')
            .count(),
          1,
          'The header launcher remains mounted while its modal is open.',
        );
      };
      const close = async () => {
        if (await collapse().isVisible()) await collapse().click();
        await page.clock.runFor(300);
        await conversation.waitFor({ state: 'hidden' });
        assert.equal(
          await paper.evaluate((element) => element.inert),
          true,
          'The retained closed transcript is inert.',
        );
      };
      const geometry = async () => ({
        main: await page.locator('#main-content').boundingBox(),
        document: await page.evaluate(() => ({
          width: document.documentElement.scrollWidth,
          height: document.documentElement.scrollHeight,
          scrollY: window.scrollY,
        })),
      });
      const sameGeometry = (before, after) => {
        for (const field of ['x', 'y', 'width', 'height']) {
          assert(
            Math.abs(before.main[field] - after.main[field]) < 2,
            'Overlay toggling preserves main ' + field,
          );
        }
        assert.deepEqual(
          after.document,
          before.document,
          'Overlay toggling preserves document size and scroll position.',
        );
      };
      const layout = async () => {
        const main = await page.locator('#main-content').boundingBox();
        const side = await sidebar.boundingBox();
        const header = await page.locator('header').boundingBox();
        const panel = await paper.boundingBox();
        const spacing = await page.locator('#main-content').evaluate((main) => {
          let surface = main.querySelector(
            '[aria-labelledby="office-heading"], [aria-label="Key performance indicators"], [aria-label="Portfolio summary"]',
          );
          if (!surface) {
            surface = main.querySelector('h1');
            while (
              surface &&
              getComputedStyle(surface).borderTopLeftRadius !== '16px'
            )
              surface = surface.parentElement;
          }
          const style = surface && getComputedStyle(surface);
          return {
            gutter: parseFloat(getComputedStyle(main).paddingLeft),
            padding:
              style &&
              [
                style.paddingTop,
                style.paddingRight,
                style.paddingBottom,
                style.paddingLeft,
              ].map(parseFloat),
            radius: style && parseFloat(style.borderTopLeftRadius),
          };
        });
        assert.equal(
          spacing.gutter,
          width >= 1024 ? 32 : width >= 600 ? 24 : 16,
        );
        let panelPadding = width >= 600 ? 24 : 16;
        if (new URL(page.url()).pathname === '/daily-brief')
          panelPadding = width >= 600 ? 16 : 12;
        assert.deepEqual(spacing.padding, Array(4).fill(panelPadding));
        assert.equal(spacing.radius, 16);
        assert(main.width > 0 && side.width > 0);
        assert(Math.abs(panel.y - header.y - header.height - 16) < 2);
        assert(Math.abs(panel.x + panel.width - width + 16) < 2);
        assert(Math.abs(panel.y + panel.height - height + 16) < 2);
        assert(Math.abs(panel.width - Math.min(380, width - 32)) < 2);
        const corners = await paper.evaluate((element) => {
          const style = getComputedStyle(element);
          return [
            style.borderTopLeftRadius,
            style.borderTopRightRadius,
            style.borderBottomLeftRadius,
            style.borderBottomRightRadius,
          ].map(parseFloat);
        });
        assert(
          corners.every((corner) => Math.abs(corner - 16) < 0.1),
          'The overlay uses the same 16px radius on all four corners.',
        );
        assert.equal(
          await paper.evaluate((element) => getComputedStyle(element).position),
          'fixed',
        );
        assert.equal(await page.locator('footer[data-credits]').count(), 1);
        assert.equal(
          await page.getByRole('main').count(),
          0,
          'The modal makes background main content inaccessible at every width.',
        );
        assert.equal(await page.getByRole('banner').count(), 0);
        assert.equal(
          await page
            .locator('body')
            .evaluate((element) => getComputedStyle(element).overflow),
          'hidden',
          'Native modal scroll lock is active at every width.',
        );
        assert(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth + 1,
          ),
          'No horizontal overflow.',
        );
        assert.equal(
          await page
            .getByRole('dialog', { name: 'Team chat', exact: true })
            .count(),
          1,
        );
        const toggle = await collapse().boundingBox();
        assert(toggle.width >= 44 && toggle.height >= 44);
        assert(
          toggle.x >= side.x && toggle.x + toggle.width <= side.x + side.width,
        );
        assert(toggle.y >= header.y + header.height);
        assert(
          await collapse().evaluate((element) => {
            const bounds = element.getBoundingClientRect();
            return element.contains(
              document.elementFromPoint(bounds.x + 8, bounds.y + 22),
            );
          }),
          'The close control is clickable inside the chat header.',
        );
        return { main, side };
      };
      assert.equal(await page.locator('.MuiFab-root').count(), 0);
      await expand().waitFor();
      await conversation.waitFor({ state: 'hidden' });
      assert.equal(
        await sidebar.count(),
        1,
        'One retained transcript is mounted.',
      );
      assert.equal(await conversation.count(), 1);
      assert.equal(await paper.evaluate((element) => element.inert), true);
      const launcher = await expand().elementHandle();
      await open();
      await conversation.locator('[id^="team-chat-run-"]').first().waitFor();
      const transcript = await conversation.elementHandle();
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      await page.mouse.click(8, 96);
      await page.waitForTimeout(40);
      await page.mouse.click(8, 96);
      await page.clock.runFor(400);
      await conversation.waitFor({ state: 'hidden' });
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await open();
      const modalKeyboard = async () => {
        await collapse().focus();
        for (const key of ['Tab', 'Shift+Tab']) {
          for (let index = 0; index < 12; index++) {
            await page.keyboard.press(key);
            assert(
              await dialog().evaluate((element) =>
                element.contains(document.activeElement),
              ),
              'Tab and Shift+Tab remain inside the native modal.',
            );
          }
        }
        const scrollY = await page.evaluate(() => window.scrollY);
        await page.mouse.move(8, 96);
        await page.mouse.wheel(0, 500);
        await page.waitForTimeout(50);
        assert.equal(await page.evaluate(() => window.scrollY), scrollY);
      };
      await modalKeyboard();
      assert.equal(await sidebar.getByRole('textbox').count(), 0);
      assert.equal(
        await sidebar.getByText(/Research draft|Draft only/).count(),
        0,
      );
      await layout();
      await close();
      const collapsedToggle = await expand().boundingBox();
      assert(collapsedToggle.width >= 44 && collapsedToggle.height >= 44);
      const collapsedHeader = await page.locator('header').boundingBox();
      assert(
        await expand().evaluate(
          (element) => element.closest('header') !== null,
        ),
        'The labeled chat control has its own space in the shared header.',
      );
      assert(
        collapsedToggle.y >= collapsedHeader.y &&
          collapsedToggle.y + collapsedToggle.height <=
            collapsedHeader.y + collapsedHeader.height &&
          collapsedToggle.x >= 16 &&
          collapsedToggle.x + collapsedToggle.width <= width - 16,
        'The chat control stays inside the header and never floats over page actions.',
      );
      assert(
        await expand().evaluate((element) => {
          const bounds = element.getBoundingClientRect();
          return element.contains(
            document.elementFromPoint(
              bounds.x + bounds.width / 2,
              bounds.y + bounds.height / 2,
            ),
          );
        }),
        'The chat control is clickable within its header space.',
      );
      const brand = await page
        .getByRole('link', { name: 'Quantara home', exact: true })
        .boundingBox();
      assert(
        brand.width >= 44 && brand.height >= 44,
        'The home link has a 44px touch target.',
      );
      const collapsedRail = await page.locator('body').evaluate(() =>
        [...document.querySelectorAll('*')].some((element) => {
          const style = getComputedStyle(element);
          const rect = element.getBoundingClientRect();
          return (
            rect.width >= 48 &&
            rect.width <= 56 &&
            rect.height >= window.innerHeight - 80 &&
            style.position === 'fixed' &&
            style.visibility !== 'hidden' &&
            rect.left < window.innerWidth &&
            rect.right >= window.innerWidth - 1 &&
            element !== document.body
          );
        }),
      );
      assert.equal(collapsedRail, false, 'Collapsed state has no tall rail.');
      const collapsedFile = output + '/' + device + '-collapsed.png';
      await page.screenshot({ path: collapsedFile });
      captures.push(collapsedFile);
      const initialGeometry = await geometry();
      await open();
      sameGeometry(initialGeometry, await geometry());
      await conversation.evaluate((element) => (element.scrollTop = 160));
      await page.clock.runFor(50);
      const reading = await conversation.evaluate(
        (element) => element.scrollTop,
      );
      await close();
      sameGeometry(initialGeometry, await geometry());
      await open();
      assert(await launcher.evaluate((element) => element.isConnected));
      assert(await transcript.evaluate((element) => element.isConnected));
      assert(
        Math.abs(
          (await conversation.evaluate((element) => element.scrollTop)) -
            reading,
        ) < 2,
      );
      if (device === 'desktop') {
        const original = await conversation.elementHandle();
        await page.setViewportSize({ width: 768, height: 1000 });
        await page.clock.runFor(300);
        assert(
          await original.evaluate((element) => element.isConnected),
          'Breakpoint changes keep the existing conversation mounted.',
        );
        assert(
          Math.abs(
            (await conversation.evaluate((element) => element.scrollTop)) -
              reading,
          ) < 2,
          'Breakpoint changes preserve the reading position.',
        );
        await page.setViewportSize({ width, height });
        await page.clock.runFor(300);
        assert(await original.evaluate((element) => element.isConnected));
        assert(
          Math.abs(
            (await conversation.evaluate((element) => element.scrollTop)) -
              reading,
          ) < 2,
        );
        assert.equal(await page.getByRole('main').count(), 0);
        assert.equal(await page.getByRole('banner').count(), 0);
        assert.equal(await sidebar.count(), 1);
        await original.dispose();
      }
      for (const [route, title] of [
        ['/', 'Home'],
        ['/dashboard', 'Dashboard'],
        ['/daily-brief', 'Daily Brief'],
        ['/crypto', 'Crypto'],
      ]) {
        await close();
        const menu = page.getByRole('button', {
          name: 'Open menu',
          exact: true,
        });
        if (await menu.isVisible()) {
          await menu.click();
          await page.clock.runFor(300);
        }
        await page.getByRole('link', { name: title, exact: true }).click();
        await page.waitForURL(url + route);
        await page.locator('#main-content h1').waitFor();
        await page.clock.runFor(350);
        await page.getByRole('main').waitFor();
        if (route === '/') {
          await page.clock.runFor(1500);
          await page.locator('iframe[data-ready="true"]').waitFor();
          await page
            .frameLocator('iframe[title="Pixel Agents research office"]')
            .getByTestId('office-canvas')
            .waitFor();
        }
        const before = await geometry();
        await open();
        sameGeometry(before, await geometry());
        await layout();
        assert(await transcript.evaluate((element) => element.isConnected));
        assert.equal(await conversation.count(), 1);
        await close();
        sameGeometry(before, await geometry());
        await open();
        await page.evaluate(() => window.scrollTo(0, 0));
        const file =
          output +
          '/' +
          device +
          '-' +
          title.toLowerCase().replace(' ', '-') +
          '.png';
        await page.screenshot({ path: file });
        captures.push(file);
      }
      const motionResults = [];
      if (device === 'desktop' || device === 'mobile') {
        await close();
        for (const reduced of [false, true]) {
          motionResults.push(await motion(page, true, reduced));
          motionResults.push(await motion(page, false, reduced));
          assert(await transcript.evaluate((element) => element.isConnected));
        }
        await page.emulateMedia({ reducedMotion: 'reduce' });
        await open();
        // A backdrop click closes the modal without touching background actions.
        await page.mouse.click(8, 96);
        await page.clock.runFor(300);
        await conversation.waitFor({ state: 'hidden' });
        assert(
          await expand().evaluate(
            (element) => element === document.activeElement,
          ),
          'Backdrop close restores focus to the stable header launcher.',
        );
        await open();
      }
      await conversation.evaluate((element) => (element.scrollTop = 160));
      await page.clock.runFor(50);
      const position = await conversation.evaluate(
        (element) => element.scrollTop,
      );
      count = 4;
      await sidebar
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await conversation
        .getByText('Synthetic new reply 4 from reviewer.', { exact: true })
        .waitFor();
      await sidebar
        .getByRole('button', { name: 'New messages', exact: true })
        .waitFor();
      assert(
        Math.abs(
          (await conversation.evaluate((element) => element.scrollTop)) -
            position,
        ) < 2,
      );
      await sidebar
        .getByRole('button', { name: 'New messages', exact: true })
        .click();
      count = 5;
      await sidebar
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await conversation
        .getByText('Synthetic new reply 5 from reviewer.', { exact: true })
        .waitFor();
      assert(
        await conversation.evaluate(
          (element) =>
            element.scrollHeight - element.clientHeight - element.scrollTop <
            50,
        ),
      );
      hangOverview = true;
      hangBrief = true;
      account = 'b';
      const requested = Promise.all([
        page.waitForRequest(
          (request) => new URL(request.url()).pathname === '/api/team',
        ),
        page.waitForRequest(
          (request) => new URL(request.url()).pathname === '/api/team/brief',
        ),
      ]);
      await sidebar
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await requested;
      await page.clock.runFor(8100);
      await sidebar.getByText('Research status request timed out.').waitFor();
      await sidebar
        .getByText(
          'Previous verified research is shown. This refresh did not verify the current account context and reports.',
          { exact: true },
        )
        .waitFor();
      assert(
        (await conversation
          .locator('#team-chat-run-00000000-0000-4000-8000-000000000001')
          .count()) > 0,
      );
      hangOverview = false;
      hangBrief = false;
      await Promise.all(
        held.splice(0).map((route) => route.abort().catch(() => undefined)),
      );
      hangBrief = true;
      const observed = page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === '/api/team' &&
          response.status() === 200,
      );
      await sidebar
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      const response = await observed;
      assert.equal(
        (await response.json()).latestRun.accountKey,
        'synthetic-readable-b',
      );
      await response.finished();
      await page.clock.runFor(8100);
      await sidebar.getByText('Research status request timed out.').waitFor();
      assert.equal(
        await conversation.locator('[id^="team-chat-run-"]').count(),
        0,
      );
      hangBrief = false;
      await Promise.all(
        held.splice(0).map((route) => route.abort().catch(() => undefined)),
      );
      await sidebar
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await conversation
        .locator('#team-chat-run-00000000-0000-4000-8000-000000000105')
        .waitFor();
      await collapse().focus();
      await page.keyboard.press('Escape');
      await page.clock.runFor(300);
      await conversation.waitFor({ state: 'hidden' });
      assert(
        await expand().evaluate(
          (element) => element === document.activeElement,
        ),
      );
      await expand().press('Enter');
      await conversation.waitFor();
      assert.equal(
        await dialog().evaluate((element) =>
          element.contains(document.activeElement),
        ),
        true,
        'Explicit expansion moves focus into the native modal.',
      );
      assert.equal(
        await sidebar.getAttribute('aria-label'),
        'Team conversation',
      );
      results.push({
        device,
        overlay: true,
        pageLayouts: 4,
        geometryStable: true,
        footerPreserved: true,
        modalFocusAndScrollLock: true,
        motion: motionResults,
        readOnly: true,
        scrollback: true,
        newMessages: true,
        accountTimeouts: true,
        keyboard: true,
      });
      await launcher.dispose();
      await transcript.dispose();
      await context.close();
    }
    assert.deepEqual(writes, []);
    assert.deepEqual(escaped, []);
    assert.deepEqual(errors, []);
    await fs.writeFile(
      output + '/results.json',
      JSON.stringify(
        { results, captures, writes, escaped, errors, syntheticOnly: true },
        null,
        2,
      ),
    );
    console.log(
      `Team chat: four pages at ${results.length} viewport sizes; labeled launcher, in-panel close, overlay geometry unchanged at every width, retained transcript, native modal focus/scroll lock and motion preferences, no overflow, scroll/new messages, account timeouts and keyboard passed; synthetic GET only.`,
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
