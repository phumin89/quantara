const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');

async function verify() {
  const tag = process.env.VERIFY_TAG ?? '7873a7a';
  assert(/^[a-f0-9]{6,8}$/.test(tag), 'Use the task verification namespace.');
  const project = `quantara-verify-${tag}`;
  const url = 'http://127.0.0.1:3109';
  assert.equal(
    process.env.TEST_URL,
    url,
    'Only the isolated Docker fixture port is allowed.',
  );
  const docker = (...args) =>
    execFileSync('docker', args, { encoding: 'utf8' }).trim();
  for (const name of [`${project}-web`, `${project}-fixture`]) {
    const info = JSON.parse(docker('inspect', name))[0];
    assert.equal(info.Config.Labels['quantara.verification'], tag);
    assert.deepEqual(Object.keys(info.NetworkSettings.Networks), [
      `quantara-verify-run-${tag}`,
    ]);
    if (name.endsWith('-web'))
      assert(
        info.Config.Env.includes('QUANTARA_CONTAINER_RUNTIME=true'),
        'Use the same explicit container runtime flag as compose.',
      );
  }
  const modePath = path.resolve(
    process.env.FIXTURE_MODE_PATH ?? 'work/quality/integration/home-mode.json',
  );
  assert(
    modePath.startsWith(path.resolve('work/quality/integration') + path.sep),
    'Mode file must stay in the isolated task.',
  );
  const mode = (value) =>
    fs.writeFile(modePath, JSON.stringify({ mode: value }));
  const ready = async () => {
    for (let attempt = 0; attempt < 40; attempt++) {
      try {
        if ((await fetch(url, { signal: AbortSignal.timeout(1000) })).ok)
          return;
      } catch {}
      await new Promise((resolve) => setTimeout(resolve, 250));
    }
    throw new Error('Isolated web did not become ready.');
  };
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  const errors = [];
  const writes = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (request.url().includes('/api/') && request.method() !== 'GET')
      writes.push(request.method());
  });
  await page.route('**/*', (route) =>
    new URL(route.request().url()).origin === url
      ? route.continue()
      : route.abort(),
  );
  const team = page.getByRole('region', { name: 'Team', exact: true });
  const refresh = () =>
    page.getByRole('button', { name: 'Refresh status', exact: true }).click();
  try {
    await mode('healthy');
    await ready();
    await page.goto(url);
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    await page.locator('iframe[data-ready="true"]').waitFor();
    for (const asset of [
      'office/index.html',
      'office/fonts/FSPixelSansUnicode-Regular.ttf',
      'office/provenance.json',
      'LICENSE',
      'NOTICE.txt',
    ]) {
      assert.equal(
        (
          await page.request.get(`${url}/vendor/pixel-agents/${asset}`)
        ).status(),
        200,
        'Standalone image must retain public renderer/license assets.',
      );
    }
    docker('stop', '--timeout', '2', `${project}-fixture`);
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Stale', exact: true })
      .waitFor();
    await page
      .getByText('Stale / unavailable', { exact: true })
      .first()
      .waitFor();
    docker('start', `${project}-fixture`);
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    await mode('older');
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Stale', exact: true })
      .waitFor();
    await mode('empty');
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Idle', exact: true })
      .waitFor();
    await mode('unavailable');
    await page.reload();
    await team
      .getByRole('button', { name: 'Intraday Unknown', exact: true })
      .waitFor();
    await mode('healthy');
    docker('restart', '--timeout', '2', `${project}-web`);
    await ready();
    await page.goto(url);
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    await page.locator('iframe[data-ready="true"]').waitFor();
    await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
    await page.getByTestId('total-assets').waitFor();
    assert(
      (await page.getByTestId('total-assets').textContent()).includes(
        '12,500.00',
      ),
    );
    await page
      .getByTestId('dashboard-day-net')
      .getByText('THB 12.25', { exact: true })
      .waitFor();
    const stats = JSON.parse(
      docker(
        'exec',
        `${project}-web`,
        'node',
        '-e',
        'fetch("http://fixture:5052/__fixture/stats").then(r=>r.text()).then(console.log)',
      ),
    );
    assert.equal(stats.writes, 0);
    assert.deepEqual(writes, []);
    assert.deepEqual(errors, []);
    console.log(
      'Container Home passed: standalone assets, real HTTP proxy, backend loss/recovery, old/empty/initial failure state, web restart/readiness and GET-only requests. Dedicated task fixture network; no real accounts.',
    );
  } finally {
    await mode('healthy');
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
