const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const ts = require('typescript');
const syncFs = require('node:fs');
require.extensions['.ts'] = (module, filename) =>
  module._compile(
    ts.transpileModule(syncFs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
      },
      fileName: filename,
    }).outputText,
    filename,
  );
const { NextRequest } = require('next/server');
const { chromium } = require('playwright');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');
const path = require('node:path');

async function verifyOrders() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const staticBuild = process.env.STATIC_BUILD === '1';
  const captureDirectory =
    process.env.ORDER_CAPTURE_DIR || '.impeccable/review';
  if (staticBuild) {
    assert.equal(url, 'http://127.0.0.1:3108');
    assert(
      process.env.ORDER_CAPTURE_DIR,
      'Static checks need an explicit capture directory.',
    );
  }
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({ serviceWorkers: 'block' });
  const violations = [];
  if (staticBuild) {
    await page.context().route('**/*', async (route) => {
      try {
        const address = new URL(route.request().url());
        assert.equal(address.origin, url);
        await staticBuildAsset(route, address);
      } catch (error) {
        violations.push(error.message);
        await route.abort('blockedbyclient');
      }
    });
    await page.routeWebSocket('**/*', (socket) => {
      violations.push('Unexpected WebSocket: ' + socket.url());
      socket.close();
    });
    page.on('request', (request) => {
      if (new URL(request.url()).origin !== url) {
        violations.push('Unexpected request origin: ' + request.url());
      }
    });
  }
  await page.addInitScript(() => {
    // Observe cancellation at fetch; intercepted routes do not reliably emit requestfailed.
    window.orderReadAborts = 0;
    const originalFetch = window.fetch;
    window.fetch = async (input, options) => {
      const signal =
        input === '/api/crypto/orders' && options?.method === 'GET'
          ? options.signal
          : null;
      const aborted = () => window.orderReadAborts++;
      signal?.addEventListener('abort', aborted);
      try {
        return await originalFetch(input, options);
      } finally {
        signal?.removeEventListener('abort', aborted);
      }
    };
  });
  await page.route('**/api/**', (route) =>
    route.fulfill({
      status: 503,
      json: { error: 'Isolated fixture: unavailable.' },
    }),
  );
  const errors = [];
  const writes = [];
  const id = '218d734e-0446-492d-8b62-ffbd106d044e';
  const proposal = (
    status = 'pending',
    expiresAt = '2026-10-13T04:03:00Z',
  ) => ({
    id,
    symbol: 'BTC',
    side: 'buy',
    amount: '1000',
    limitPrice: '2800000',
    notionalThb: '1000',
    estimatedFeeThb: '2.50',
    referencePrice: '2801000',
    reason: 'Fixture proposal',
    createdAt: '2026-10-13T04:00:00Z',
    expiresAt,
    status,
    brokerOrderId: status === 'submitted' ? 'broker-1' : null,
    message:
      status === 'unknown'
        ? 'Unknown result. Check Bitkub before any further submission.'
        : null,
    sizing: {
      policyHash: 'a'.repeat(64),
      observedAt: '2026-10-13T04:00:00Z',
      equityThb: '10000',
      allocationFraction: '0.1',
      estimatedDownsideThb: '50',
      estimatedCostThb: '2.50',
      projectedExposureThb: '1000',
      code: 'fixture',
      explanation: 'Synthetic sizing explanation; no account authority.',
    },
  });
  let current = proposal();
  let listUnavailable = true;
  let empty = false;
  let holdList = false;
  const pendingLists = [];
  let listRequests = 0;
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (
      request.url().includes('/api/crypto/orders') &&
      request.method() !== 'GET'
    )
      writes.push(request);
  });
  await page.route('**/api/team', (route) =>
    route.fulfill({
      json: {
        configured: true,
        latestRun: null,
        company: {
          enabled: false,
          status: 'stopped',
          nextRunAt: null,
          lastRunId: null,
          error: null,
        },
      },
    }),
  );
  await page.route('**/api/team/brief?*', (route) =>
    route.fulfill({ json: { date: '2026-10-13', runs: [] } }),
  );
  await page.route('**/api/crypto/orders', async (route) => {
    listRequests++;
    if (listUnavailable) {
      await route.fulfill({
        status: 503,
        json: { error: 'Isolated order list unavailable.' },
      });
      return;
    }

    const response = { proposals: empty ? [] : [{ ...current }] };
    if (holdList) {
      await new Promise((resolve) => pendingLists.push(resolve));
    }
    await route.fulfill({ json: response });
  });
  await page.route('**/api/crypto/orders/**', async (route) => {
    assert.equal(route.request().method(), 'POST');
    if (new URL(route.request().url()).pathname.endsWith('/approve')) {
      assert.deepEqual(route.request().postDataJSON(), { confirmed: true });
      current = { ...current, status: 'submitted', brokerOrderId: 'broker-1' };
    } else if (new URL(route.request().url()).pathname.endsWith('/reconcile')) {
      assert.deepEqual(route.request().postDataJSON(), {});
      current = { ...current, status: 'filled', brokerOrderId: 'broker-1' };
    } else {
      current = { ...current, status: 'rejected' };
    }
    await route.fulfill({ json: current });
  });
  try {
    await page.clock.install({ time: new Date('2026-10-13T04:00:00Z') });
    await page.goto(url + '/crypto');
    const collapseChat = page.getByRole('button', {
      name: 'Collapse team chat',
      exact: true,
    });
    if (await collapseChat.count()) await collapseChat.click();
    await page
      .getByText('Unable to load order proposals.', { exact: true })
      .waitFor();
    assert.equal(
      await page.getByText('No order proposals.', { exact: true }).count(),
      0,
      'an unavailable initial list never claims a verified empty list',
    );
    listUnavailable = false;
    empty = true;
    await page.clock.runFor(6000);
    await page.getByText('No order proposals.', { exact: true }).waitFor();
    assert.equal(
      await page
        .getByText('Unable to load order proposals.', { exact: true })
        .count(),
      0,
      'a successful empty list replaces the unavailable state',
    );
    empty = false;
    await page.clock.runFor(6000);
    await page.getByText('Fixture proposal', { exact: false }).waitFor();
    listUnavailable = true;
    await page.clock.runFor(6000);
    await page
      .getByText('Unable to load order proposals.', { exact: true })
      .waitFor();
    assert.equal(
      await page.getByText('Fixture proposal', { exact: false }).isVisible(),
      true,
      'a later list failure retains verified nonempty proposals',
    );
    assert.equal(
      await page.getByText('No order proposals.', { exact: true }).count(),
      0,
    );
    assert.equal(writes.length, 0, 'list recovery never writes an order');
    listUnavailable = false;
    await page.clock.runFor(6000);
    await page
      .getByText('Unable to load order proposals.', { exact: true })
      .waitFor({ state: 'hidden' });
    const approve = page.getByRole('button', {
      name: 'Approve',
      exact: true,
    });
    assert.equal(await approve.isDisabled(), false);
    const enabledApprovalColor = await approve.evaluate(
      (button) => getComputedStyle(button).color,
    );
    assert.equal(await page.getByRole('checkbox').count(), 0);
    assert.equal(
      await page.getByRole('textbox', { name: 'Symbol' }).count(),
      0,
    );
    const approvalRequest = page.waitForRequest((request) =>
      request.url().endsWith('/approve'),
    );
    await approve.evaluate((button) => {
      button.click();
      button.click();
    });
    await approvalRequest;
    await page.getByText('submitted', { exact: true }).waitFor();
    assert.equal(
      writes.length,
      1,
      'double click sends exactly one explicit approval',
    );
    assert.equal(writes[0].postDataJSON().confirmed, true);
    current = proposal('unknown');
    await page.reload();
    await page.getByText('unknown', { exact: true }).waitFor();
    await page.getByText('Unknown result.', { exact: false }).waitFor();
    assert.equal(
      await approve.count(),
      0,
      'unknown outcome stays visible without a retry action',
    );
    await page
      .getByRole('button', { name: 'Check status', exact: true })
      .click();
    await page.locator('summary').filter({ hasText: 'Past proposals' }).click();
    await page.getByText('filled', { exact: true }).waitFor();
    assert.equal(
      writes.length,
      2,
      'status check sends exactly one reconciliation',
    );
    assert(writes[1].url().endsWith('/reconcile'));
    current = proposal();
    await page.clock.setSystemTime(new Date('2026-10-13T04:00:00Z'));
    await page.reload();
    await page.getByText('Fixture proposal', { exact: false }).waitFor();
    await page.clock.runFor(181000);
    await page.getByText('Expired', { exact: true }).waitFor();
    assert.equal(await approve.isDisabled(), true);
    assert.notEqual(
      await approve.evaluate((button) => getComputedStyle(button).color),
      enabledApprovalColor,
      'Expired approval uses the built-in disabled appearance.',
    );
    assert.equal(writes.length, 2, 'expiry never auto-approves');
    await fs.mkdir(captureDirectory, { recursive: true });
    await page.clock.setSystemTime(new Date('2026-10-13T04:00:00Z'));
    await page.reload();
    await page.getByText('Fixture proposal', { exact: false }).waitFor();
    if (await collapseChat.count()) await collapseChat.click();
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
      ['small-phone', 320, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.clock.runFor(500);
      await page.waitForTimeout(200);
      await page.screenshot({
        path: path.join(captureDirectory, 'orders-' + name + '.png'),
        fullPage: true,
        animations: 'disabled',
      });
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
        name + ' has no horizontal page overflow',
      );
    }
    empty = true;
    await page.reload();
    await page.getByText('No order proposals.', { exact: true }).waitFor();
    current = {
      ...proposal(),
      side: 'sell',
      amount: '0.001',
      notionalThb: '2800',
      estimatedFeeThb: '7.00',
    };
    empty = false;
    await page.clock.runFor(6000);
    await page.getByText('Sell 0.001 BTC at limit', { exact: false }).waitFor();
    await page
      .getByText('Approx. proceeds 2,800 THB', { exact: false })
      .waitFor();
    await page.getByText('Estimated fee 7.00 THB', { exact: true }).waitFor();
    await page
      .getByText('Why this size: 10.00% allocated, estimated downside 50 THB', {
        exact: true,
      })
      .waitFor();
    for (const width of [1440, 768, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 });
      const proceeds = await page
        .getByText('Approx. proceeds 2,800 THB', { exact: true })
        .boundingBox();
      const fee = await page
        .getByText('Estimated fee 7.00 THB', { exact: true })
        .boundingBox();
      assert(proceeds && fee && fee.y >= proceeds.y + proceeds.height - 1);
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
        width + 'px sell proposal has no page overflow',
      );
      await page.screenshot({
        path: path.join(captureDirectory, 'orders-sell-' + width + '.png'),
        fullPage: true,
        animations: 'disabled',
      });
    }
    await page.getByRole('button', { name: 'Reject', exact: true }).click();
    const pastProposals = page.locator('details').filter({
      has: page.locator('summary').filter({ hasText: 'Past proposals' }),
    });
    if ((await pastProposals.getAttribute('open')) === null) {
      await pastProposals.locator(':scope > summary').click();
    }
    await page.getByText('rejected', { exact: true }).waitFor();
    assert.equal(
      writes.length,
      3,
      'new proposal appears and Reject sends only one action',
    );
    assert(writes[2].url().endsWith('/reject'));

    await page.setViewportSize({ width: 1440, height: 1000 });
    // A slow initial read must survive multiple five-second poll ticks.
    current = { ...proposal(), reason: 'Delayed order list fixture' };
    holdList = true;
    const beforeSlowList = listRequests;
    await page.reload();
    await page.getByText('Loading order proposals…', { exact: true }).waitFor();
    await page.clock.runFor(11000);
    assert.equal(
      listRequests - beforeSlowList,
      1,
      'slow order reads remain single-flight across polling ticks',
    );
    assert.equal(
      pendingLists.length,
      1,
      'slow order read is held for the regression fixture',
    );
    holdList = false;
    pendingLists.splice(0).forEach((resolve) => resolve());
    await page
      .getByText('Delayed order list fixture', { exact: false })
      .waitFor();
    current = { ...current, reason: 'Polling resumed fixture' };
    await page.clock.runFor(6000);
    await page.getByText('Polling resumed fixture', { exact: false }).waitFor();

    // A read captured before an explicit action cannot overwrite its result.
    holdList = true;
    const beforeActionList = page.waitForRequest((request) =>
      request.url().endsWith('/api/crypto/orders'),
    );
    await page.clock.runFor(6000);
    await beforeActionList;
    const beforeActionAborts = await page.evaluate(
      () => window.orderReadAborts,
    );
    await approve.click();
    assert.equal(
      await page.evaluate(() => window.orderReadAborts),
      beforeActionAborts + 1,
      'explicit action cancels the pending list read',
    );
    await page.getByText('submitted', { exact: true }).waitFor();
    holdList = false;
    pendingLists.splice(0).forEach((resolve) => resolve());
    await page.clock.runFor(6000);
    await page.getByText('submitted', { exact: true }).waitFor();
    assert.equal(
      await approve.count(),
      0,
      'late reads cannot restore approval',
    );
    assert.equal(
      writes.length,
      4,
      'only the explicit action submits the fixture',
    );

    // Client-side navigation cancels the read and stops its polling timer.
    holdList = true;
    const beforeNavigationList = page.waitForRequest((request) =>
      request.url().endsWith('/api/crypto/orders'),
    );
    await page.clock.runFor(6000);
    await beforeNavigationList;
    const beforeNavigationAborts = await page.evaluate(
      () => window.orderReadAborts,
    );
    await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
    await page.waitForURL(url + '/dashboard');
    assert.equal(
      await page.evaluate(() => window.orderReadAborts),
      beforeNavigationAborts + 1,
      'unmount cancels the pending list read',
    );
    const afterNavigationLists = listRequests;
    holdList = false;
    pendingLists.splice(0).forEach((resolve) => resolve());
    await page.clock.runFor(11000);
    assert.equal(
      listRequests,
      afterNavigationLists,
      'unmounted list stops polling',
    );
    current = { ...current, reason: 'Remounted order list fixture' };
    await page.getByRole('link', { name: 'Crypto', exact: true }).click();
    await page
      .getByText('Remounted order list fixture', { exact: false })
      .waitFor();
    assert.equal(writes.length, 4, 'navigation never repeats an approval');

    assert.deepEqual(errors, []);
    assert.deepEqual(violations, []);
    // These requests are refused before any broker access. Never exercise a real approval.
    const {
      OrdersController,
    } = require('../apps/web/features/orders/api/OrdersController.ts');
    const originalFetch = global.fetch;
    let outbound = 0;
    global.fetch = async () => {
      outbound++;
      throw new Error('Outbound fetch forbidden in controller contract test.');
    };
    try {
      const crossOrigin = await OrdersController.create(
        new NextRequest('http://127.0.0.1/api/crypto/orders', {
          method: 'POST',
          headers: {
            Origin: 'https://attacker.invalid',
            'Content-Type': 'application/json',
          },
          body: '{}',
        }),
      );
      assert.equal(crossOrigin.status, 403);
      const invalidApproval = await OrdersController.approve(
        new NextRequest(
          'http://127.0.0.1/api/crypto/orders/' + id + '/approve',
          {
            method: 'POST',
            headers: {
              Host: '127.0.0.1',
              Origin: 'http://127.0.0.1',
              'Content-Type': 'application/json',
            },
            body: JSON.stringify({ confirmed: false }),
          },
        ),
        { params: Promise.resolve({ id }) },
      );
      assert.equal(invalidApproval.status, 400);
      assert.equal(outbound, 0);
    } finally {
      global.fetch = originalFetch;
    }
    console.log(
      'Orders: slow single-flight reads, stale-read cancellation, unmount/remount, exact review, duplicate-click protection, server states, unknown outcome, no timeout approval, local-origin checks and desktop/tablet/phone passed. Isolated fixtures only; no live order sent.',
    );
  } finally {
    pendingLists.splice(0).forEach((resolve) => resolve());
    await browser.close();
  }
}
verifyOrders().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
