const path = require('node:path');
const assert = require('node:assert/strict');
const { ESLint } = require('eslint');

async function verify() {
  const eslint = new ESLint({ cwd: path.resolve('apps/web') });
  const filePath = path.resolve(
    'apps/web/features/crypto/components/crypto-page/CryptoPage.tsx',
  );
  const [invalid] = await eslint.lintText(
    'export default function CryptoPage() { return null; }\nexport function Extra() { return null; }\n',
    { filePath },
  );
  assert.equal(
    invalid.messages.filter(
      (message) => message.ruleId === 'quantara/file-ownership',
    ).length,
    3,
  );
  const [valid] = await eslint.lintText(
    "import { PureComponent } from 'react';\n\nexport default class CryptoPage extends PureComponent {\n  public override render() {\n    return null;\n  }\n}\n",
    { filePath },
  );
  assert.equal(valid.errorCount, 0);
  const [directService] = await eslint.lintText(
    "import { PureComponent } from 'react';\nimport { RequestService } from '../../../../shared/services/RequestService';\n\nexport default class CryptoPage extends PureComponent {\n  public readonly service = new RequestService();\n\n  public override render() {\n    return null;\n  }\n}\n",
    { filePath },
  );
  assert.ok(
    directService.messages.some(
      (message) => message.ruleId === 'quantara/injected-services',
    ),
  );
  assert.ok(
    invalid.messages.some(
      (message) => message.ruleId === 'quantara/class-based',
    ),
  );
  const [standalone] = await eslint.lintText(
    'export const CryptoPage = () => null;\n',
    { filePath },
  );
  assert.ok(
    standalone.messages.some(
      (message) => message.ruleId === 'quantara/class-based',
    ),
  );
  const [anonymous] = await eslint.lintText('export default () => null;\n', {
    filePath,
  });
  assert.ok(
    anonymous.messages.some(
      (message) => message.ruleId === 'quantara/class-based',
    ),
  );
  const [hook] = await eslint.lintText(
    "import { useState as state } from 'react';\n\nexport default class CryptoPage {\n  public value = state;\n}\n",
    { filePath },
  );
  assert.ok(
    hook.messages.some((message) => message.ruleId === 'quantara/class-based'),
  );
  const [explicitAny] = await eslint.lintText(
    'export default function CryptoPage(value: any) { return value; }\n',
    { filePath },
  );
  assert.ok(
    explicitAny.messages.some(
      (message) => message.ruleId === '@typescript-eslint/no-explicit-any',
    ),
  );
  const [missingBlankLine] = await eslint.lintText(
    'export default function CryptoPage() { const value = null; return value; }\n',
    { filePath },
  );
  assert.ok(
    missingBlankLine.messages.some(
      (message) =>
        message.ruleId === '@stylistic/padding-line-between-statements',
    ),
  );
  const [extraBlankLines] = await eslint.lintText(
    '\n\nexport default function CryptoPage() { return null; }\n',
    { filePath },
  );
  assert.ok(
    extraBlankLines.messages.some(
      (message) => message.ruleId === '@stylistic/no-multiple-empty-lines',
    ),
  );
  const [spaced] = await eslint.lintText(
    'export default class CryptoPage {\n  public render() {\n    const value = null;\n\n    return value;\n  }\n}\n',
    { filePath },
  );
  assert.equal(spaced.errorCount, 0);
  console.log(
    'Frontend editor lint requires classes and rejects standalone functions and hooks.',
  );
  console.log(
    'Frontend editor lint rejects missing/excess blank lines and accepts correctly spaced code.',
  );
  console.log(
    'Frontend editor lint rejects multiple components/mismatched filenames and accepts valid files.',
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
