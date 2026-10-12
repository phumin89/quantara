const path = require("node:path");
const assert = require("node:assert/strict");
const { ESLint } = require("eslint");

async function verify() {
  const eslint = new ESLint({ cwd: path.resolve("apps/web") });
  const filePath = path.resolve(
    "apps/web/features/crypto/components/CryptoPage.tsx",
  );
  const [invalid] = await eslint.lintText(
    "export default function CryptoPage() { return null; }\nexport function Extra() { return null; }\n",
    { filePath },
  );
  assert.equal(
    invalid.messages.filter(
      (message) => message.ruleId === "quantara/file-ownership",
    ).length,
    3,
  );
  const [valid] = await eslint.lintText(
    "export default function CryptoPage() { return null; }\n",
    { filePath },
  );
  assert.equal(valid.errorCount, 0);
  console.log(
    "Frontend editor lint rejects multiple components/mismatched filenames and accepts valid files.",
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
