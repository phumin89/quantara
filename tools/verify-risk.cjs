const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verifyRisk() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  page.setDefaultTimeout(10000);
  const errors = [];
  const writes = [];
  let refreshCount = 0;
  let policyWrites = 0;
  let policyHash =
    '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef';
  let policy = {
    profile: 'disabled',
    maximumExposureFraction: 1,
    maximumConcentrationFraction: 1,
    riskToStopFraction: 0.05,
    atrMultiplier: 1,
    maximumSpreadFraction: 0.005,
    estimatedFeeRate: 0.0025,
    maximumDataAgeSeconds: 90,
    maximumSignalAgeSeconds: 600,
  };
  const overview = () => ({
    accountKey: 'fixture-account',
    policyHash,
    policy,
    capital: {
      accountKey: 'fixture-account',
      observedAt: '2026-10-16T04:00:00+00:00',
      spendableThb: refreshCount ? '1200' : null,
      equityThb: refreshCount ? '1500' : null,
      exposureThb: '300',
      reservedThb: '100',
      hasUncertainOrders: false,
      blocker: refreshCount ? null : 'Fixture capital is incomplete.',
    },
    performance: refreshCount
      ? {
          from: '2026-10-16T03:00:00+00:00',
          to: '2026-10-16T04:00:00+00:00',
          netEquityChangeThb: '-10',
          timeWeightedReturn: '-0.01',
          explanation:
            'Fixture flow-free interval; includes unrealized change.',
        }
      : null,
    refreshError: null,
    manualOnly: true,
  });
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (request.method() !== 'GET') writes.push(request);
  });
  await page.route('**/api/**', (route) =>
    route.fulfill({ status: 404, json: { error: 'Unmocked fixture API' } }),
  );
  await page.route('**/api/crypto/portfolio', (route) =>
    route.fulfill({
      json: {
        positions: [],
        summary: { totalAssets: 0, availableThb: 1500, holdingCount: 0 },
        updatedAt: '2026-10-16T04:00:00+00:00',
        priceUpdatedAt: '2026-10-16T04:00:00+00:00',
        historyUpdatedAt: '2026-10-16T04:00:00+00:00',
        historyCoverage: 'Fixture',
        syncing: false,
        completedSymbols: 0,
        totalSymbols: 0,
        error: null,
      },
    }),
  );
  await page.route('**/api/crypto/risk', (route) =>
    route.fulfill({ json: overview() }),
  );
  await page.route('**/api/crypto/risk/refresh', async (route) => {
    refreshCount += 1;
    if (refreshCount === 2) {
      policyHash = 'f'.repeat(64);
      policy = { ...policy, maximumExposureFraction: 0.5 };
    }
    await route.fulfill({ json: overview() });
  });
  await page.route('**/api/crypto/risk/policy', async (route) => {
    const body = route.request().postDataJSON();
    assert.equal(
      body.expectedPolicyHash,
      '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
    );
    assert.equal(body.policy.profile, 'aggressive');
    policyWrites += 1;
    if (policyWrites === 2) {
      assert.equal(
        body.policy.maximumExposureFraction,
        0.6,
        'refresh preserves the local draft',
      );
      await route.fulfill({
        status: 409,
        json: { error: 'Fixture policy changed. Reload before saving.' },
      });
      return;
    }
    assert.equal(body.policy.maximumExposureFraction, 0.75);
    policy = body.policy;
    await route.fulfill({ json: overview() });
  });
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.goto(`${url}/crypto`);
    await page
      .getByRole('heading', { name: 'Risk sizing', exact: true })
      .waitFor();
    assert.equal(
      writes.length,
      0,
      'risk mount and profile display never write',
    );
    await page.getByRole('combobox', { name: 'Risk profile' }).click();
    await page.getByRole('option', { name: 'Aggressive', exact: true }).click();
    assert.equal(writes.length, 0, 'profile selection never writes');
    await page.getByText('Advanced policy', { exact: true }).click();
    const exposure = page.getByRole('spinbutton', { name: 'Exposure %' });
    await exposure.fill('75');
    await page
      .getByRole('button', { name: 'Save policy', exact: true })
      .click();
    await page.getByText('Aggressive', { exact: true }).first().waitFor();
    assert.equal(writes.length, 1, 'save writes exactly once');
    await page.getByText('Advanced policy', { exact: true }).click();
    await page
      .getByRole('button', { name: 'Refresh capital', exact: true })
      .click();
    await page.getByText('Available 1200 THB', { exact: true }).waitFor();
    assert.equal(refreshCount, 1, 'refresh writes exactly once');
    assert.equal(await page.getByText('Unknown', { exact: true }).count(), 0);
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `.impeccable/review/risk-${name}.png` });
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
        `${name} overflow: ${await page.evaluate(() =>
          JSON.stringify(
            [...document.querySelectorAll('body *')]
              .filter(
                (el) =>
                  el.getBoundingClientRect().right > innerWidth &&
                  getComputedStyle(el).position !== 'fixed',
              )
              .slice(0, 8)
              .map((el) => ({
                tag: el.tagName,
                text: el.textContent.slice(0, 45),
                width: el.getBoundingClientRect().width,
              })),
          ),
        )}`,
      );
    }
    await page.getByText('Advanced policy', { exact: true }).click();
    await exposure.fill('60');
    const refreshed = page.waitForResponse(
      (response) =>
        response.url().endsWith('/api/crypto/risk/refresh') &&
        response.status() === 200,
    );
    await page
      .getByRole('button', { name: 'Refresh capital', exact: true })
      .click();
    await refreshed;
    await page
      .getByRole('button', { name: 'Save policy', exact: true })
      .click();
    await page
      .getByText('Fixture policy changed. Reload before saving.', {
        exact: true,
      })
      .waitFor();
    assert.equal(
      policyWrites,
      2,
      'stale draft retains its original hash and cannot silently overwrite a newer policy',
    );
    assert.deepEqual(errors, []);
    console.log(
      'Risk UI: cached load, explicit save/refresh, unknown capital and responsive layout passed. Isolated fixtures only.',
    );
  } finally {
    await browser.close();
  }
}

verifyRisk().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
