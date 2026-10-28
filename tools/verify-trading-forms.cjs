const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function bounded(pending, message) {
  let timer;
  try {
    return await Promise.race([
      pending,
      new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error(message)), 2500);
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

async function staticAsset(route, address) {
  const pathname = decodeURIComponent(address.pathname);
  let root;
  let relative;
  if (
    ['/', '/dashboard', '/daily-brief', '/crypto', '/team'].includes(pathname)
  ) {
    root = path.resolve('apps/web/.next/server/app');
    relative =
      (pathname === '/' ? 'index' : pathname.slice(1)) +
      (route.request().headers().rsc === '1' ? '.rsc' : '.html');
  } else if (pathname.startsWith('/_next/static/')) {
    root = path.resolve('apps/web/.next/static');
    relative = pathname.slice('/_next/static/'.length);
  } else {
    root = path.resolve('apps/web/public');
    relative = pathname.slice(1);
  }
  const file = path.resolve(root, relative);
  assert(
    file.startsWith(root + path.sep),
    'Static files stay in their build root.',
  );
  await route.fulfill({
    path: file,
    ...(relative.endsWith('.rsc') ? { contentType: 'text/x-component' } : {}),
  });
}

async function verify() {
  const base = process.env.TEST_URL;
  assert.equal(base, 'http://127.0.0.1:3108');
  const output = process.env.TRADING_FORM_CAPTURE_DIR;
  assert(output, 'An explicit private capture directory is required.');
  await fs.mkdir(output, { recursive: true });
  const buildId = (await fs.readFile('apps/web/.next/BUILD_ID', 'utf8')).trim();
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  const failures = [];
  const writes = [];
  const syntheticWrites = [];
  const escaped = [];
  const errors = [];
  const transportErrors = [];
  const validationConsole = [];
  const failureText = 'Portfolio data is unavailable. Refresh to try again.';
  const uncertainWrite =
    'The action may have completed. Refresh status before taking another action.';
  try {
    for (const [device, width, height] of [
      ['small-phone', 320, 740],
      ['phone', 390, 844],
      ['tablet', 820, 1180],
      ['desktop', 1440, 1000],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(5000);
      await page.routeWebSocket('**/*', (socket) => {
        escaped.push({ device, url: socket.url() });
        socket.close();
      });
      page.on('pageerror', (error) =>
        errors.push({ device, error: error.message }),
      );
      page.on('console', (message) => {
        if (
          message.type() === 'error' &&
          /not focusable/.test(message.text())
        ) {
          validationConsole.push({ device, error: message.text() });
        }
      });
      const data = structuredClone(fixture.data);
      let failTrading = false;
      let holdTrading = false;
      let statusReads = 0;
      let holdSeen;
      const held = [];
      const result = { device, width, height };
      const check = (condition, message) => {
        if (!condition) failures.push(`${device}: ${message}`);
      };
      await context.route('**/*', async (route) => {
        const request = route.request();
        const address = new URL(request.url());
        if (address.origin !== base) {
          escaped.push({ device, origin: address.origin });
          await route.abort();
          return;
        }
        if (
          request.method() === 'POST' &&
          address.pathname === '/api/trading/emergency-stop'
        ) {
          syntheticWrites.push({ device, path: address.pathname });
          await route.fulfill({ status: 503, json: { error: uncertainWrite } });
          return;
        }
        if (request.method() !== 'GET') {
          writes.push({ device, method: request.method(), url: request.url() });
          await route.abort();
          return;
        }
        try {
          if (!address.pathname.startsWith('/api/')) {
            await staticAsset(route, address);
            return;
          }
          if (address.pathname === '/api/trading') {
            statusReads += 1;
            if (holdTrading) {
              held.push(route);
              holdSeen?.();
              return;
            }
            if (failTrading) {
              await route.fulfill({
                status: 503,
                json: { error: failureText },
              });
              return;
            }
          }
          const body = data[address.pathname];
          await route.fulfill({
            status: body ? 200 : 404,
            json: body ?? { error: 'Unmocked synthetic API' },
          });
        } catch (error) {
          transportErrors.push({
            device,
            path: address.pathname,
            error: error.message,
          });
          await route.abort().catch(() => undefined);
        }
      });
      try {
        await page.clock.install({ time: new Date(fixture.at) });
        await page.goto(base + '/crypto');
        const chatControl = page.getByRole('button', {
          name: 'Expand team chat',
          exact: true,
        });
        await chatControl.waitFor();
        assert.equal(await chatControl.getAttribute('aria-expanded'), 'false');
        result.chatInitiallyCollapsed = true;
        await page
          .getByRole('button', { name: 'Trading settings', exact: true })
          .click();
        const drawer = page.getByRole('dialog', {
          name: 'Trading settings',
          exact: true,
        });
        await drawer.waitFor();
        const advanced = drawer.locator('details').filter({
          has: page
            .locator('summary')
            .filter({ hasText: /^Advanced settings/ }),
        });
        const summary = advanced.locator('summary');
        const spread = advanced.locator('input[name="maxSpreadBps"]');
        const slippage = advanced.locator('input[name="slippageBps"]');
        await summary.click();
        await spread.fill('');
        await summary.click();
        await drawer
          .getByRole('button', {
            name: 'Save rules and disarm live',
            exact: true,
          })
          .click();
        result.invalidSectionRevealed = await advanced.evaluate(
          (element) => element.open,
        );
        result.invalidFieldFocused = await spread.evaluate(
          (element) => document.activeElement === element,
        );
        const fieldBounds = await spread.boundingBox();
        const headerBounds = await drawer
          .getByRole('heading', { name: 'Trading settings', exact: true })
          .boundingBox();
        result.invalidFieldInViewport = !!(
          fieldBounds &&
          headerBounds &&
          fieldBounds.y >= headerBounds.y + headerBounds.height &&
          fieldBounds.y + fieldBounds.height <= height
        );
        check(
          result.invalidSectionRevealed,
          'Native invalid field must reveal its collapsed Advanced section.',
        );
        check(
          result.invalidFieldFocused,
          'Native invalid field must receive focus.',
        );
        check(
          result.invalidFieldInViewport,
          'The focused invalid field must be visible below the drawer header.',
        );
        await page.screenshot({
          path: path.join(output, device + '-invalid.png'),
        });

        if (!(await advanced.evaluate((element) => element.open)))
          await summary.click();
        await spread.fill('50');
        await slippage.fill('21');
        await summary.click();
        failTrading = true;
        await page.clock.runFor(10001);
        await page
          .getByText(failureText)
          .first()
          .waitFor({ state: 'attached' });
        const contextualError = drawer.getByText(failureText);
        result.errorVisibleInDrawer = await contextualError.isVisible();
        const retry = drawer.getByRole('button', {
          name: 'Refresh status',
          exact: true,
        });
        result.contextualRetryVisible = await retry.isVisible();
        const errorBounds = await contextualError.boundingBox();
        const retryBounds = await retry.boundingBox();
        result.recoveryInViewport = !!(
          errorBounds &&
          retryBounds &&
          errorBounds.y >= 0 &&
          errorBounds.y + errorBounds.height <= height &&
          retryBounds.y >= 0 &&
          retryBounds.y + retryBounds.height <= height &&
          retryBounds.height >= 44
        );
        check(
          result.errorVisibleInDrawer,
          'Polling failure must be explained inside the open settings drawer.',
        );
        check(
          result.contextualRetryVisible,
          'Open settings must provide Refresh status.',
        );
        check(
          result.recoveryInViewport,
          'The error and recovery control must stay in view after scrolling.',
        );
        await page.screenshot({
          path: path.join(output, device + '-poll-error.png'),
        });
        failTrading = false;
        if (result.contextualRetryVisible) {
          holdTrading = true;
          const requested = new Promise((resolve) => {
            holdSeen = resolve;
          });
          await retry.click();
          await bounded(
            requested,
            'Contextual retry did not request a synthetic status read.',
          );
          const pendingRetry = retry;
          result.retryDisabledWhilePending = await pendingRetry.isDisabled();
          result.retryLoadingFeedback =
            (await pendingRetry.getAttribute('aria-busy')) === 'true' ||
            (await pendingRetry.getByRole('progressbar').count()) > 0 ||
            /Retrying|Checking|Refreshing/i.test(
              await pendingRetry.innerText(),
            );
          check(
            result.retryDisabledWhilePending,
            'Retry must prevent duplicate reads while pending.',
          );
          check(
            result.retryLoadingFeedback,
            'Retry must show pending feedback.',
          );
          await page.screenshot({
            path: path.join(output, device + '-retry-pending.png'),
          });
          holdTrading = false;
          for (const route of held.splice(0))
            await route.fulfill({ json: data['/api/trading'] });
          await contextualError.waitFor({ state: 'hidden' });
          result.recoveredWithoutClosingDrawer = await drawer.isVisible();
          result.focusRetainedAfterRecovery = await drawer.evaluate((element) =>
            element.contains(document.activeElement),
          );
          check(
            result.focusRetainedAfterRecovery,
            'Status recovery must keep keyboard focus inside the drawer.',
          );
        } else {
          // Baseline recovery records the defect without waiting on a missing control.
          await drawer
            .getByRole('button', {
              name: 'Close trading settings',
              exact: true,
            })
            .click();
          await drawer.waitFor({ state: 'hidden' });
          await page
            .locator('#trading')
            .getByRole('button', { name: 'Refresh status', exact: true })
            .click();
          await page
            .locator('#trading')
            .getByText(failureText, { exact: true })
            .waitFor({ state: 'hidden' });
          await page
            .getByRole('button', { name: 'Trading settings', exact: true })
            .click();
          await drawer.waitFor();
          result.recoveredWithoutClosingDrawer = false;
        }
        check(
          result.recoveredWithoutClosingDrawer,
          'Status recovery must not require closing the settings drawer.',
        );
        if (!(await advanced.evaluate((element) => element.open)))
          await summary.click();
        result.draftValueAfterRecovery = await slippage.inputValue();
        check(
          result.draftValueAfterRecovery === '21',
          'Status recovery must preserve unsaved overrides.',
        );
        result.noHorizontalOverflow = await drawer.evaluate((element) =>
          [element, element.querySelector('[role="region"]')].every(
            (region) => region && region.scrollWidth <= region.clientWidth + 1,
          ),
        );
        check(
          result.noHorizontalOverflow,
          'Settings must fit its viewport without horizontal overflow.',
        );
        await page.screenshot({
          path: path.join(output, device + '-recovered.png'),
        });
        data['/api/trading'] = {
          ...data['/api/trading'],
          accountKey: 'second-synthetic-trading-account',
        };
        await page.clock.runFor(10001);
        await drawer.locator('button[type="submit"]:disabled').waitFor();
        result.draftResetForChangedAccount =
          (await slippage.inputValue()) === '20';
        check(
          result.draftResetForChangedAccount,
          'Draft values must not carry into a different trading account with the same rules hash.',
        );
        result.focusRetainedAfterAccountChange = await drawer.evaluate(
          (element) => element.contains(document.activeElement),
        );
        check(
          result.focusRetainedAfterAccountChange,
          'Changing accounts must keep keyboard focus inside the drawer.',
        );
        await page.keyboard.press('Escape');
        await drawer.waitFor({ state: 'hidden' });
        result.focusRestoredOnClose = await page
          .getByRole('button', { name: 'Trading settings', exact: true })
          .evaluate((element) => document.activeElement === element);
        check(
          result.focusRestoredOnClose,
          'Escape must close settings and restore focus to its opener.',
        );
        const readsBeforeAction = statusReads;
        const trading = page.locator('#trading');
        await trading
          .getByRole('button', { name: 'Emergency stop', exact: true })
          .click();
        const uncertainAlert = trading
          .getByRole('alert')
          .filter({ hasText: uncertainWrite });
        await uncertainAlert.waitFor();
        await page.clock.runFor(20001);
        await uncertainAlert.scrollIntoViewIfNeeded();
        result.uncertainActionTitleVisible = await uncertainAlert
          .getByText('Check latest status', { exact: true })
          .isVisible();
        check(
          result.uncertainActionTitleVisible,
          'An uncertain action must use a neutral heading.',
        );
        result.actionWasNotRepeated =
          syntheticWrites.filter((entry) => entry.device === device).length ===
          1;
        check(
          result.actionWasNotRepeated,
          'Status recovery must not repeat an uncertain action.',
        );
        result.statusReadAfterUncertainAction = statusReads > readsBeforeAction;
        check(
          result.statusReadAfterUncertainAction,
          'An uncertain action must refresh read-only status.',
        );
        const alertBounds = await uncertainAlert.boundingBox();
        result.uncertaintyFitsViewport = !!(
          alertBounds &&
          alertBounds.x >= 0 &&
          alertBounds.x + alertBounds.width <= width &&
          alertBounds.y >= 0 &&
          alertBounds.y + alertBounds.height <= height &&
          (await uncertainAlert.evaluate(
            (element) => element.scrollWidth <= element.clientWidth + 1,
          ))
        );
        check(
          result.uncertaintyFitsViewport,
          'Uncertain action guidance must remain readable inside the viewport.',
        );
        await page.screenshot({
          path: path.join(output, device + '-uncertain-action.png'),
        });
      } catch (error) {
        result.incomplete = error.message;
        failures.push(`${device}: ${error.message}`);
        await page
          .screenshot({ path: path.join(output, device + '-incomplete.png') })
          .catch(() => undefined);
      } finally {
        holdTrading = false;
        for (const route of held.splice(0))
          await route.abort().catch(() => undefined);
        await context.close();
        results.push(result);
      }
    }
  } finally {
    await browser.close();
  }
  const evidence = {
    buildId,
    results,
    failures,
    writes,
    syntheticWrites,
    escaped,
    errors,
    transportErrors,
    validationConsole,
    syntheticOnly: true,
    serverStarted: false,
  };
  await fs.writeFile(
    path.join(output, 'results.json'),
    JSON.stringify(evidence, null, 2),
  );
  assert.deepEqual(writes, [], 'No unexpected write may leave interception.');
  assert.equal(syntheticWrites.length, 4, 'One intercepted action per size.');
  assert.deepEqual(escaped, [], 'No external origin may be reached.');
  assert.deepEqual(errors, [], 'No unexpected client runtime error.');
  assert.deepEqual(
    validationConsole,
    [],
    'Native validation must not leave an invalid control hidden from focus.',
  );
  assert.deepEqual(
    transportErrors,
    [],
    'Every static file must come from the selected build.',
  );
  assert.deepEqual(
    failures,
    [],
    'Trading form regressions must pass at every size.',
  );
  console.log(
    'Trading forms: invalid-field focus, contextual status recovery, drafts, pending feedback and uncertain-action guidance passed at four sizes; all requests intercepted, no repeated action and no server.',
  );
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
