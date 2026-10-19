const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');

require.extensions['.ts'] = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  });
  module._compile(output.outputText, filename);
};

const componentRoot = 'apps/web/features/trading/components';
const panel = fs
  .readdirSync(componentRoot, { recursive: true })
  .filter((file) => file.endsWith('.tsx'))
  .map((file) => fs.readFileSync(`${componentRoot}/${file}`, 'utf8'))
  .join('\n');
const rulePresentation = fs.readFileSync(
  'apps/web/features/trading/presentation/TradingRulePresentation.ts',
  'utf8',
);
const rules = fs.readFileSync(
  'apps/web/features/trading/contracts/TradingRules.ts',
  'utf8',
);
const overview = fs.readFileSync(
  'apps/web/features/trading/contracts/TradingOverview.ts',
  'utf8',
);

assert.match(panel, /Autonomous trading/);
assert.match(panel, /Set up and review autonomous trading/);
assert.match(panel, /prepareAdaptiveSetup/);
assert.match(panel, /Enable Auto Trading/);
assert.match(rulePresentation, /adaptive-intraday-v1/);
assert.match(rulePresentation, /adaptive-intraday-v2/);
assert.match(rules, /adaptivePolicySchema/);
assert.match(panel, /\.prepare\(/);
assert.match(overview, /autonomy:/);
assert.match(overview, /dailyProfitTargetThb/);

const {
  tradingRulesSchema,
} = require('../apps/web/features/trading/contracts/TradingRules.ts');
const adaptiveRules = {
  version: 'adaptive-intraday-v1',
  symbols: [],
  allocatedThb: 1000,
  perOrderThb: 100,
  maxPositions: 16,
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
    excludedSymbols: ['USDT', 'USDC', 'DAI'],
    risk: {
      profile: 'aggressive',
      maximumExposureFraction: 1,
      maximumConcentrationFraction: 1,
      riskToStopFraction: 0.05,
      atrMultiplier: 1,
      maximumSpreadFraction: 0.005,
      estimatedFeeRate: 0.0025,
      maximumDataAgeSeconds: 90,
      maximumSignalAgeSeconds: 600,
    },
    dailyLossFraction: 0.1,
    dailyProfitTargetThb: 500,
  },
};
assert.equal(tradingRulesSchema.safeParse(adaptiveRules).success, true);
assert.equal(
  tradingRulesSchema.safeParse({
    ...adaptiveRules,
    version: 'adaptive-intraday-v2',
  }).success,
  true,
);
assert.equal(
  tradingRulesSchema.safeParse({
    ...adaptiveRules,
    version: 'adaptive-intraday-v2',
    symbols: ['BTC'],
  }).success,
  false,
);
assert.match(panel, /Prepare v2 upgrade and review/);

assert.match(panel, /Set up and review autonomous trading/);
assert.match(panel, /prepareAdaptiveSetup\(overview\)/);

console.log('Autonomous UI contract and staged activation checks pass.');

async function verifyComponent() {
  require.extensions['.tsx'] = (module, filename) => {
    const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
        jsx: ts.JsxEmit.ReactJSX,
      },
    });
    module._compile(output.outputText, filename);
  };
  const React = require('react');
  const TradingPanel =
    require('../apps/web/features/trading/components/trading-panel/TradingPanel.tsx').default;
  const {
    tradingOverviewSchema,
  } = require('../apps/web/features/trading/contracts/TradingOverview.ts');
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
  const { adaptive: unused, ...legacyRules } = adaptiveRules;
  let current = tradingOverviewSchema.parse({
    accountKey: 'fixture-account',
    mode: 'stopped',
    rules: {
      ...legacyRules,
      version: 'ema20-50-breakout-v1',
      symbols: ['BTC'],
    },
    rulesHash: 'legacy-hash',
    mandate: null,
    emergencyStopped: false,
    heartbeatAt: new Date().toISOString(),
    fault: null,
    liveActivationAvailable: true,
    paper: book,
    live: book,
    reports: [],
  });
  const writes = [];
  let reads = 0;
  let preparationError = null;
  const instance = new TradingPanel({ date: '2026-10-16' });
  const savedWindow = global.window;
  global.window = { setTimeout: () => 1, clearTimeout: () => {} };
  instance.setState = (update, callback) => {
    const change =
      typeof update === 'function'
        ? update(instance.state, instance.props)
        : update;
    if (change) instance.state = { ...instance.state, ...change };
    if (callback) callback();
  };
  instance.context = {
    get: async () => {
      reads += 1;
      return structuredClone(current);
    },
    configure: async (value) => {
      writes.push('configure');
      current = {
        ...current,
        rules: tradingRulesSchema.parse(value),
        rulesHash: 'prepared-hash',
      };
      return structuredClone(current);
    },
    prepare: async (accountKey, rulesHash) => {
      assert.equal(accountKey, current.accountKey);
      assert.equal(rulesHash, current.rulesHash);
      writes.push('prepare');
      if (preparationError) throw preparationError;
      current = {
        ...current,
        rules: tradingRulesSchema.parse(
          current.rules.adaptive
            ? { ...current.rules, version: 'adaptive-intraday-v2' }
            : { ...adaptiveRules, version: 'adaptive-intraday-v2' },
        ),
        rulesHash: 'prepared-hash',
      };
      return structuredClone(current);
    },
    activate: async (accountKey, rulesHash, expiresAt) => {
      writes.push('activate');
      current = {
        ...current,
        mode: 'autoLive',
        mandate: {
          id: 'fake',
          accountKey,
          rulesHash,
          createdAt: new Date().toISOString(),
          expiresAt,
          revokedAt: null,
        },
      };
      return structuredClone(current);
    },
  };
  const flush = () => new Promise(setImmediate);
  const find = (node, text) => {
    if (!React.isValidElement(node)) return null;
    if (
      typeof node.type === 'function' &&
      node.type.prototype instanceof React.Component
    ) {
      return find(new node.type(node.props).render(), text);
    }
    if (
      node.props.children === text &&
      typeof node.props.onClick === 'function'
    )
      return node;
    for (const child of React.Children.toArray(node.props.children)) {
      const found = find(child, text);
      if (found) return found;
    }
    return null;
  };
  try {
    instance.componentDidMount();
    await flush();
    await instance.load();
    assert(reads >= 2);
    assert.deepEqual(writes, [], 'mount and polling never change authority');
    const prepare = find(instance.render(), 'Set up and review');
    assert(prepare, 'the primary action prepares automatic setup');
    prepare.props.onClick();
    prepare.props.onClick();
    await flush();
    assert.deepEqual(writes, ['prepare'], 'double-click prepares once');
    assert.equal(
      instance.state.reviewOpen,
      true,
      'successful setup opens the exact saved review',
    );
    assert.equal(instance.state.overview.rules.version, 'adaptive-intraday-v2');
    assert.deepEqual(instance.state.overview.rules.symbols, []);
    await instance.activate(current);
    assert.deepEqual(writes, ['prepare'], 'no expiry means no activation');
    instance.setState({
      reviewOpen: true,
      expiresAt: new Date(Date.now() + 86_400_000).toISOString().slice(0, 16),
    });
    await Promise.all([instance.activate(current), instance.activate(current)]);
    assert.deepEqual(
      writes,
      ['prepare', 'activate'],
      'only an explicit confirmation sends one fake activation',
    );
    current = {
      ...current,
      rules: { ...current.rules, version: 'adaptive-intraday-v1' },
      rulesHash: 'unchanged-v1-hash',
    };
    instance.setState({
      overview: structuredClone(current),
      dirty: false,
      reviewOpen: false,
    });
    await instance.load();
    assert.equal(
      instance.state.overview.rules.version,
      'adaptive-intraday-v1',
      'polling retains the saved v1 mandate',
    );
    current.rules.adaptive.risk.riskToStopFraction = 0.123456789;
    current.rules.adaptive.dailyLossFraction = 0.234567891;
    instance.setState({ overview: structuredClone(current), open: true });
    await instance.prepareAdaptiveSetup(instance.state.overview);
    assert.equal(instance.state.overview.rules.version, 'adaptive-intraday-v2');
    assert.equal(instance.state.dirty, false);
    assert.equal(instance.state.open, false, 'setup closes its drawer');
    assert.equal(instance.state.reviewOpen, true);
    assert.equal(
      current.rules.version,
      'adaptive-intraday-v2',
      'an explicit upgrade prepares server rules for review',
    );
    const upgraded = structuredClone(current.rules);
    await instance.load();
    assert.equal(
      instance.state.overview.rules.version,
      'adaptive-intraday-v2',
      'polling retains the exact saved policy',
    );
    assert.deepEqual(writes, ['prepare', 'activate', 'prepare']);
    const partialOverride = new FormData();
    partialOverride.set('cooldownMinutes', '16');
    await instance.save(partialOverride);
    assert.equal(current.rules.cooldownMinutes, 16);
    assert.deepEqual(current.rules.adaptive, upgraded.adaptive);
    assert.equal(current.rules.takeProfitBps, upgraded.takeProfitBps);
    const unchangedCaps = new FormData();
    unchangedCaps.set(
      'riskToStopFraction',
      String(current.rules.adaptive.risk.riskToStopFraction * 100),
    );
    unchangedCaps.set(
      'dailyLossFraction',
      String(current.rules.adaptive.dailyLossFraction * 100),
    );
    await instance.save(unchangedCaps);
    assert.deepEqual(current.rules.adaptive, upgraded.adaptive);
    const writesBeforeBlank = writes.length;
    const blankOverride = new FormData();
    blankOverride.set('feeBps', '');
    await instance.save(blankOverride);
    assert.equal(
      writes.length,
      writesBeforeBlank,
      'Blank overrides never become zero fees',
    );
    assert(instance.state.validationError);
    preparationError = new Error('Fixture preparation failed');
    instance.setState({ reviewOpen: false, open: true });
    await instance.prepareAdaptiveSetup(instance.state.overview);
    assert.equal(instance.state.reviewOpen, false);
    assert.equal(instance.state.open, true, 'failed setup stays recoverable');
    assert.match(instance.state.error, /Fixture preparation failed/);
    console.log(
      'Actual class component: read-only mount/poll, setup/review, expiry and duplicate confirmation checks pass.',
    );
  } finally {
    instance.componentWillUnmount();
    if (savedWindow === undefined) delete global.window;
    else global.window = savedWindow;
  }
}

verifyComponent().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
