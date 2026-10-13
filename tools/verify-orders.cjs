const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verifyOrders() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
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
  });
  let current = proposal();
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
  await page.route('**/api/crypto/orders', (route) =>
    route.fulfill({ json: { proposals: [current] } }),
  );
  await page.route('**/api/crypto/orders/**', async (route) => {
    assert.equal(route.request().method(), 'POST');
    if (new URL(route.request().url()).pathname.endsWith('/approve')) {
      assert.deepEqual(route.request().postDataJSON(), { confirmed: true });
      current = { ...current, status: 'submitted', brokerOrderId: 'broker-1' };
    } else {
      current = { ...current, status: 'rejected' };
    }
    await route.fulfill({ json: current });
  });
  try {
    await page.clock.install({ time: new Date('2026-10-13T04:00:00Z') });
    await page.goto(url + '/daily-brief');
    await page.getByText('Fixture proposal', { exact: false }).waitFor();
    const approve = page.getByRole('button', {
      name: 'Approve & submit',
      exact: true,
    });
    assert.equal(await approve.isDisabled(), true);
    await page
      .getByRole('checkbox', { name: 'I reviewed this exact order' })
      .check();
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
    current = proposal();
    await page.clock.setSystemTime(new Date('2026-10-13T04:00:00Z'));
    await page.reload();
    await page
      .getByRole('checkbox', { name: 'I reviewed this exact order' })
      .check();
    await page.clock.runFor(181000);
    await page.getByText('Expired', { exact: true }).waitFor();
    assert.equal(await approve.isDisabled(), true);
    assert.equal(writes.length, 1, 'expiry never auto-approves');
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.clock.setSystemTime(new Date('2026-10-13T04:00:00Z'));
    await page.reload();
    await page.getByText('Fixture proposal', { exact: false }).waitFor();
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.clock.runFor(500);
      await page.waitForTimeout(200);
      await page.screenshot({
        path: '.impeccable/review/orders-' + name + '.png',
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
    assert.deepEqual(errors, []);
    // These requests are refused before any broker access. Never exercise a real approval.
    const crossOrigin = await fetch(url + '/api/crypto/orders', {
      method: 'POST',
      headers: {
        Origin: 'https://attacker.invalid',
        'Content-Type': 'application/json',
      },
      body: '{}',
    });
    assert.equal(crossOrigin.status, 403);
    const invalidApproval = await fetch(
      url + '/api/crypto/orders/' + id + '/approve',
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmed: false }),
      },
    );
    assert.equal(invalidApproval.status, 400);
    console.log(
      'Orders: exact review, duplicate-click protection, server states, unknown outcome, no timeout approval, local-origin checks and desktop/tablet/phone passed. Isolated fixtures only; no live order sent.',
    );
  } finally {
    await browser.close();
  }
}
verifyOrders().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
