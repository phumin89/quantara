const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');

async function verify() {
  const project = 'quantara-verify-7873a7a';
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
    assert.equal(info.Config.Labels['quantara.verification'], '7873a7a');
    assert.deepEqual(Object.keys(info.NetworkSettings.Networks), [
      'quantara-verify-run-7873a7a',
    ]);
  }
  const modePath = path.resolve('work/quality/integration/home-mode.json');
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
    await page.locator('canvas[data-ready="true"]').waitFor();
    for (const asset of [
      'renderer.js',
      'char_0.png',
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
    docker('stop', '--time', '2', `${project}-fixture`);
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Stale', exact: true })
      .waitFor();
    await page.getByText('Stale / unavailable', { exact: true }).waitFor();
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
    docker('restart', '--time', '2', `${project}-web`);
    await ready();
    await page.goto(url);
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    await page.locator('canvas[data-ready="true"]').waitFor();
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
      'Container Home passed: standalone assets, real HTTP proxy, backend loss/recovery, old/empty/initial failure state, web restart/readiness and GET-only requests. Internal fixture network; no real accounts.',
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
