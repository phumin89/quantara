const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) => {
    const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
        jsx: ts.JsxEmit.ReactJSX,
        esModuleInterop: true,
      },
    });
    module._compile(output.outputText, filename);
  };
}

const root = path.resolve(__dirname, '..');
const { tradingOverviewSchema } = require(
  path.join(root, 'apps/web/features/trading/contracts/TradingOverview.ts'),
);

const rules = {
  version: 'adaptive-intraday-v2',
  symbols: [],
  allocatedThb: 1000,
  perOrderThb: 100,
  maxPositions: 2,
  dailyLossThb: 100,
  maxSpreadBps: 50,
  slippageBps: 20,
  maxDataAgeSeconds: 90,
  takeProfitBps: 200,
  stopLossBps: 100,
  trailingStopBps: 100,
  timeExitMinutes: 120,
  cooldownMinutes: 15,
  orderLifetimeMinutes: 15,
  feeBps: 25,
  volumeRatio: 1.5,
  adaptive: {
    universeVersion: 'bitkub-thb-spot-v1',
    includeFutureDeposits: true,
    excludedSymbols: ['USDT'],
    dailyLossFraction: 0.5,
    dailyProfitTargetThb: 500,
    risk: {
      profile: 'aggressive',
      maximumExposureFraction: 1,
      maximumConcentrationFraction: 1,
      riskToStopFraction: 0.5,
      atrMultiplier: 1,
      maximumSpreadFraction: 0.005,
      estimatedFeeRate: 0.0025,
      maximumDataAgeSeconds: 90,
      maximumSignalAgeSeconds: 600,
    },
  },
};
const book = {
  cashThb: '1000',
  equityThb: '1000',
  realizedGrossThb: '0',
  realizedNetThb: '0',
  feesThb: '0',
  unrealizedThb: '0',
  positions: [],
  orders: [],
};
const base = {
  accountKey: 'account',
  mode: 'stopped',
  rules,
  rulesHash: 'a'.repeat(64),
  mandate: null,
  emergencyStopped: false,
  heartbeatAt: null,
  fault: null,
  liveActivationAvailable: true,
  paper: book,
  live: book,
  reports: [],
};

for (const viewport of [1440, 820, 390]) {
  assert.equal(
    tradingOverviewSchema.safeParse(base).success,
    true,
    `legacy/default payload valid at ${viewport}px`,
  );
}

const enabled = {
  ...base,
  aiPolicy: {
    enabled: true,
    capitalCeilingFraction: 1,
    expiresAt: '2026-11-01T00:00:00+07:00',
    originalRulesHash: 'a'.repeat(64),
    effectiveRulesHash: 'b'.repeat(64),
    status: 'active',
    reason: 'Reviewer-approved candidate applied at a safe policy boundary.',
    lastChangedAt: '2026-10-28T20:00:00+07:00',
  },
};
assert.equal(
  tradingOverviewSchema.safeParse(enabled).success,
  true,
  'AI policy status payload validates',
);
assert.equal(
  tradingOverviewSchema.safeParse({
    ...base,
    aiPolicy: { ...enabled.aiPolicy, capitalCeilingFraction: 0.9 },
  }).success,
  false,
  'capital ceiling cannot exceed the fixed 100% contract',
);

const service = fs.readFileSync(
  path.join(root, 'apps/web/features/trading/services/TradingService.ts'),
  'utf8',
);
assert.match(service, /signal\?\: AbortSignal,\s*aiManaged = false/);
assert.match(service, /acknowledgement: aiManaged/);
assert.match(service, /\.\.\.\(aiManaged \? \{ aiManaged: true \} : \{\}\)/);
const panel = fs.readFileSync(
  path.join(
    root,
    'apps/web/features/trading/components/trading-panel/TradingPanel.tsx',
  ),
  'utf8',
);
assert.match(
  panel,
  /aiManaged\s*&&\s*overview\.rules\.version === 'adaptive-intraday-v2'/,
);
assert.match(panel, /aiManaged: false/);

const dialog = fs.readFileSync(
  path.join(
    root,
    'apps/web/features/trading/components/trading-mandate-dialog/TradingMandateDialog.tsx',
  ),
  'utf8',
);
assert.match(dialog, /Let AI manage strategy/);
assert.match(dialog, /overview\.rules\.version === 'adaptive-intraday-v2'/);
assert.doesNotMatch(
  dialog,
  /\{TradingRulePresentation\.isAdaptive\(overview\.rules\.version\) && \(/,
);

const controller = fs.readFileSync(
  path.join(root, 'apps/web/app/api/trading/TradingController.ts'),
  'utf8',
);
assert.match(controller, /enable-ai-managed-trading/);
assert.match(controller, /aiManaged: z\.boolean\(\)\.optional\(\)/);

console.log(
  'AI policy contract checks passed: default-off, adaptive-v2-only opt-in, status reason, viewport payloads, service argument contract.',
);
