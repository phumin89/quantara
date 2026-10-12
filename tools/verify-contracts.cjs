const assert = require("node:assert/strict");
const { chromium } = require("playwright");

async function verifyContracts() {
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    let payload = { positions: "invalid API data" };
    await page.route("**/api/crypto/portfolio", (route) =>
      route.fulfill({ json: payload }),
    );
    await page.goto("http://127.0.0.1:3000/crypto");
    await page.getByRole("button", { name: "Retry", exact: true }).waitFor();
    await page
      .getByRole("button", { name: "Bitkub status: Error", exact: true })
      .waitFor();
    assert.equal(await page.getByTestId("total-assets").count(), 0);
    // Fixtures exist only in this isolated test browser.
    payload = {
      positions: [],
      summary: { totalAssets: null, availableThb: null, holdingCount: null },
      updatedAt: null,
      priceUpdatedAt: null,
      historyUpdatedAt: null,
      historyCoverage: "Test fixture",
      syncing: false,
      completedSymbols: 0,
      totalSymbols: 0,
      error: null,
    };
    await page.getByRole("button", { name: "Retry", exact: true }).click();
    await page
      .getByRole("button", { name: "Bitkub status: Checking", exact: true })
      .waitFor();
    for (const id of ["total-assets", "available-thb", "holding-count"])
      assert.equal(await page.getByTestId(id).textContent(), "—");
    assert.deepEqual(errors, []);
    console.log(
      "Malformed API payload rejected; valid unknown data recovers without invented values: passed",
    );
  } finally {
    await browser.close();
  }
}
verifyContracts().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
