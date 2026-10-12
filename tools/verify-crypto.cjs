const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const { chromium } = require("playwright");

async function verifyCrypto() {
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await fs.mkdir(".impeccable/review", { recursive: true });
  try {
    for (const [name, width, height] of [
      ["desktop", 1440, 900],
      ["tablet", 768, 1024],
      ["mobile", 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.goto("http://127.0.0.1:3000/crypto");
      await page
        .getByRole("columnheader", { name: "Coin", exact: true })
        .waitFor();
      if (width < 1040) {
        const region = page.getByRole('region', { name: 'Crypto holdings and trading history' });
        await region.focus();
        await page.keyboard.press('ArrowRight');
        await page.waitForFunction(() => document.querySelector('[aria-label="Crypto holdings and trading history"]').scrollLeft > 0);
        await region.evaluate(element => { element.scrollLeft = 0; });
      }
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
      );
      const snapshot = await (
        await page.request.get("http://127.0.0.1:3000/api/crypto/portfolio")
      ).json();
      const formattedMoney = value => value == null ? "—" : `฿${value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
      await page.getByRole("region", { name: "Portfolio summary", exact: true }).waitFor();
      const expectedSummary = [
        ["total-assets", formattedMoney(snapshot.summary.totalAssets)],
        ["available-thb", formattedMoney(snapshot.summary.availableThb)],
        ["holding-count", snapshot.summary.holdingCount == null ? "—" : String(snapshot.summary.holdingCount)],
      ];
      for (const filter of ["Holding", "Closed", "All"]) {
        await page.getByRole("button", { name: filter, exact: true }).click();
        const expected = snapshot.positions.filter(
          (position) => filter === "All" || position.status === filter,
        ).length;
        assert.equal(
          await page.getByRole("button", { name: /^View .* history$/ }).count(),
          expected,
        );
        for (const [id, value] of expectedSummary)
          assert.equal(await page.getByTestId(id).textContent(), value, `${id} stays independent of the table filter`);
      }
      if (snapshot.positions.length) {
        const symbol = snapshot.positions[0].symbol;
        await page
          .getByRole("button", { name: `View ${symbol} history`, exact: true })
          .click();
        await page
          .getByRole("columnheader", { name: "Date", exact: true })
          .waitFor();
        await page.locator(".MuiCollapse-entered").waitFor();
      }
      await page.mouse.move(0, 0);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `.impeccable/review/crypto-${name}.png` });
      console.log(`${name}: real data, filters, history and overflow passed`);
    }
    // Failure simulation is confined to this test browser, never the product.
    await page.route("**/api/crypto/portfolio", (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ error: "Test: backend unavailable" }),
      }),
    );
    await page.reload();
    await page.getByRole("button", { name: "Retry", exact: true }).waitFor();
    assert.equal(
      await page.getByText("Test: backend unavailable").isVisible(),
      true,
    );
    await page.unroute("**/api/crypto/portfolio");
    await page.getByRole("button", { name: "Retry", exact: true }).click();
    await page
      .getByRole("columnheader", { name: "Coin", exact: true })
      .waitFor();
    assert.deepEqual(errors, []);
    console.log("Backend failure/retry and browser errors: passed");
  } finally {
    await browser.close();
  }
}
verifyCrypto().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
