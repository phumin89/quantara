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
  const [missingBlankLine] = await eslint.lintText(
    "export default function CryptoPage() { const value = null; return value; }\n",
    { filePath },
  );
  assert.ok(
    missingBlankLine.messages.some(
      (message) =>
        message.ruleId === "@stylistic/padding-line-between-statements",
    ),
  );
  const [extraBlankLines] = await eslint.lintText(
    "\n\nexport default function CryptoPage() { return null; }\n",
    { filePath },
  );
  assert.ok(
    extraBlankLines.messages.some(
      (message) => message.ruleId === "@stylistic/no-multiple-empty-lines",
    ),
  );
  const [spaced] = await eslint.lintText(
    "export default function CryptoPage() {\n  const value = null;\n\n  return value;\n}\n",
    { filePath },
  );
  assert.equal(spaced.errorCount, 0);
  console.log(
    "Frontend editor lint rejects missing/excess blank lines and accepts correctly spaced code.",
  );
  console.log(
    "Frontend editor lint rejects multiple components/mismatched filenames and accepts valid files.",
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
