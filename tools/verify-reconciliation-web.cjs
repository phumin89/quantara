const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) =>
    module._compile(
      ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
        compilerOptions: {
          module: ts.ModuleKind.CommonJS,
          target: ts.ScriptTarget.ES2022,
          jsx: ts.JsxEmit.ReactJSX,
          esModuleInterop: true,
        },
      }).outputText,
      filename,
    );
}
const { NextRequest } = require('next/server');
const {
  ReconciliationController,
} = require('../apps/web/app/api/trading/reconciliation/ReconciliationController.ts');
const {
  ReconciliationSafety,
} = require('../apps/web/features/trading/presentation/ReconciliationSafety.ts');
const {
  TradeSide,
} = require('../apps/web/features/crypto/contracts/TradeSide.ts');
const {
  ReconciliationProofStore,
} = require('../apps/web/features/trading/services/ReconciliationProofStore.ts');
const Panel =
  require('../apps/web/features/trading/components/reconciliation-panel/ReconciliationPanel.tsx').default;
const Review =
  require('../apps/web/features/trading/components/reconciliation-review/ReconciliationReview.tsx').default;
const Mandate =
  require('../apps/web/features/trading/components/trading-mandate-dialog/TradingMandateDialog.tsx').default;
const { Button } = require('@mui/material');
const TradingPanel =
  require('../apps/web/features/trading/components/trading-panel/TradingPanel.tsx').default;
const TradingControls =
  require('../apps/web/features/trading/components/trading-controls/TradingControls.tsx').default;
const fixture = require('./fixtures/workspace-data.cjs');
const memory = new Map();
Object.defineProperty(globalThis, 'sessionStorage', {
  configurable: true,
  value: {
    getItem: (key) => memory.get(key) ?? null,
    setItem: (key, value) => memory.set(key, value),
    removeItem: (key) => memory.delete(key),
  },
});
const account = 'a'.repeat(64);
const rulesHash = 'b'.repeat(64);
const now = new Date().toISOString();
const manual = {
  symbol: 'WLD',
  allocatedSoldQuantity: '2.500000000000000001',
  unallocatedHistoricalQuantity: '0.00001902',
  allocatedCostThb: null,
  realizedNetProfitThb: null,
  brokerBalance: { symbol: 'WLD', quantity: '0', available: '0' },
  brokerObservedAt: now,
  expiresAt: new Date(Date.now() + 300000).toISOString(),
  proof: 'fixture-manual-proof',
  acknowledgement: 'Fixture owner explicitly reviewed inventory archival.',
  applied: false,
  cashSettlementPending: true,
};
const cash = {
  accountKey: account,
  cashThb: '9007199254740993.00001902',
  availableThb: '9007199254740993.00001902',
  brokerObservedAt: now,
  expiresAt: manual.expiresAt,
  proof: 'fixture-cash-proof',
  acknowledgement: 'Fixture owner explicitly reviewed cash adoption.',
  applied: false,
  currentDayProfitUnknown: true,
};
function wire(result) {
  return JSON.stringify(result).replace(
    /"(allocatedSoldQuantity|unallocatedHistoricalQuantity|allocatedCostThb|realizedNetProfitThb|quantity|available|cashThb|availableThb)":"(-?\d+(?:\.\d+)?)"/g,
    '"$1":$2',
  );
}
function overview() {
  const value = structuredClone(fixture.data['/api/trading']);
  Object.assign(value, {
    accountKey: account,
    rulesHash,
    mode: 'stopped',
    mandate: {
      id: 'fixture-mandate',
      accountKey: account,
      rulesHash,
      createdAt: now,
      expiresAt: manual.expiresAt,
      revokedAt: now,
    },
  });
  Object.assign(value.live, {
    cashThb: '39.18',
    positions: [
      {
        symbol: 'WLD',
        quantity: '2.50001902',
        costThb: '100',
        openedAt: now,
        peakBid: '50',
      },
    ],
    orders: [],
  });
  return value;
}

function findComponent(node, component, text) {
  if (!node || typeof node !== 'object') return null;
  if (
    node.type === component &&
    (text === undefined || textContent(node) === text)
  )
    return node;
  for (const child of [node.props?.children].flat(Infinity)) {
    const found = findComponent(child, component, text);
    if (found) return found;
  }
  return null;
}

function textContent(node) {
  if (node === null || node === undefined || typeof node === 'boolean')
    return '';
  if (typeof node === 'string' || typeof node === 'number') return String(node);
  if (Array.isArray(node)) return node.map(textContent).join(' ');
  return textContent(node.props?.children);
}

function verifyRecoveryAccess() {
  const panel = new TradingPanel({
    date: '2026-10-23',
    portfolio: {
      updatedAt: now,
      positions: [{ symbol: 'WLD', quantity: 0 }],
      performance: { unreconciledSymbols: ['WLD'] },
    },
    history: { get: async () => ({ trades: [] }) },
  });
  panel.setState = (next) => {
    panel.state = {
      ...panel.state,
      ...(typeof next === 'function' ? next(panel.state) : next),
    };
  };
  panel.state.overview = overview();
  let commands = 0;
  panel.context = {
    prepare: async () => {
      commands += 1;
    },
    activate: async () => {
      commands += 1;
    },
  };
  const controls = findComponent(panel.render(), TradingControls);
  assert(controls);
  assert.equal(
    controls.props.balanceReviewNeeded,
    true,
    'A known wallet mismatch must provide recovery before activation.',
  );
  controls.props.onReview();
  assert.equal(panel.state.balanceReviewOpen, true);
  assert.equal(panel.state.reviewOpen, false);
  assert(
    findComponent(panel.render(), Panel),
    'The balance review must be reachable.',
  );
  assert.equal(
    commands,
    0,
    'Opening balance review never configures or activates trading.',
  );
  for (const liveOn of [false, true]) {
    const recoveryControls = new TradingControls({
      ...controls.props,
      overview: { ...overview(), mode: 'autoLive' },
      liveOn,
      dirty: true,
      error: 'Status needs a refresh',
    });
    const reviewButton = findComponent(
      recoveryControls.render(),
      Button,
      'Review balances',
    );
    assert(
      reviewButton,
      'A balance review remains reachable while live status changes.',
    );
    assert.equal(
      reviewButton.props.disabled,
      false,
      'Activation restrictions must not disable the read-only balance review.',
    );
  }
  panel.state.overview.live.orders = [{ status: 'unknown', symbol: 'WLD' }];
  assert(
    findComponent(panel.render(), Button, 'Review balances'),
    'Order review must not hide saved balance confirmation recovery.',
  );
  panel.state.overview.live.orders = [];
  panel.state.overview.live.positions = [];
  panel.state.overview.autonomy = null;
  assert.equal(
    findComponent(panel.render(), TradingControls).props.balanceReviewNeeded,
    false,
    'Historical unknown profit must not trap already archived inventory in recovery.',
  );
  for (const mode of ['paper', 'manualReview', 'autoLive']) {
    panel.state.overview.mode = mode;
    assert(
      findComponent(panel.render(), Button, 'Review balances'),
      'Saved confirmation recovery remains reachable in every trading mode.',
    );
  }
  panel.state.overview.mode = 'stopped';
  panel.state.reviewOpen = true;
  panel.state.expiresAt = new Date(Date.now() + 86400000).toISOString();
  panel.receive(overview());
  const mandate = findComponent(panel.render(), Mandate);
  assert(
    mandate.props.error,
    'A new balance blocker is passed into the open activation review.',
  );
  const mandateView = new Mandate(mandate.props).renderMandate(
    panel.state.overview,
  );
  assert.equal(
    findComponent(mandateView, Button, 'Enable Auto Trading').props.disabled,
    true,
  );
  assert(
    textContent(mandateView).includes(mandate.props.error),
    'Explain why activation is now disabled.',
  );
  panel.receive({ ...overview(), accountKey: 'd'.repeat(64) });
  assert.equal(
    panel.state.balanceReviewOpen,
    false,
    'Account changes close the old review.',
  );
}
verifyRecoveryAccess();
function panelFor(current, apply, historyTrades = []) {
  const panel = new Panel({
    history: { get: async () => ({ trades: historyTrades }) },
    onApplied: () => {},
  });
  panel.mounted = true;
  panel.setState = (update, callback) => {
    panel.state = {
      ...panel.state,
      ...(typeof update === 'function'
        ? update(panel.state, panel.props)
        : update),
    };
    callback?.();
  };
  panel.context = {
    get: async () => structuredClone(current.value),
    previewManualSale: async () => structuredClone(manual),
    previewCashBaseline: async () => structuredClone(cash),
    applyManualSale: apply,
    applyCashBaseline: apply,
    pendingReconciliation: (value) => ReconciliationProofStore.read(value),
    retainReconciliation: (value) => ReconciliationProofStore.save(value),
    clearReconciliation: (value, proof) =>
      ReconciliationProofStore.clear(value, proof),
  };
  panel.state = {
    ...panel.state,
    overview: current.value,
    symbol: 'WLD',
    executionId: 'fixture-sale',
    now: Date.now(),
  };
  return panel;
}
async function verifySavedConfirmationRecovery() {
  for (const kind of ['manual-sale', 'cash-baseline']) {
    for (const change of [
      (value) => {
        value.rulesHash = 'c'.repeat(64);
      },
      (value) => {
        value.mode = 'paper';
      },
      (value) => {
        value.mandate.revokedAt = null;
      },
      (value) => {
        value.live.orders = [{ status: 'unknown' }];
      },
    ]) {
      const original = overview();
      if (kind === 'cash-baseline') original.live.positions = [];
      const result = kind === 'manual-sale' ? manual : cash;
      const saved = {
        kind,
        result: {
          ...result,
          expiresAt: new Date(Date.now() - 1000).toISOString(),
        },
        binding: ReconciliationSafety.binding(
          original,
          kind === 'manual-sale' ? 'WLD' : undefined,
        ),
      };
      const current = { value: structuredClone(original) };
      change(current.value);
      const stateBeforeRecovery = structuredClone(current.value);
      ReconciliationProofStore.save(saved);
      let calls = 0;
      const recovering = panelFor(current, async (proof, acknowledgement) => {
        calls += 1;
        assert.equal(proof, saved.result.proof);
        assert.equal(acknowledgement, saved.result.acknowledgement);
        return { ...saved.result, applied: true };
      });
      await recovering.loadStatus();
      assert.equal(calls, 0, 'Restoring a saved confirmation never retries.');
      assert.equal(recovering.state.uncertain, true);
      assert.equal(recovering.state.acknowledged, false);
      assert.equal(
        ReconciliationSafety.current(saved, current.value, true),
        true,
        'The same account can recover an exact retained proof after eligibility changes.',
      );
      assert.equal(
        ReconciliationSafety.current(saved, current.value, false),
        false,
        'Changed eligibility still blocks a new confirmation.',
      );
      const review = findComponent(recovering.render(), Review);
      assert.equal(review.props.blocked, false);
      assert(
        textContent(recovering.render()).includes(
          'Check the result of your previous balance confirmation.',
        ),
      );
      assert.equal(
        findComponent(recovering.render(), Button, 'Pause trading to review'),
        null,
      );
      const getStatus = recovering.context.get;
      let releaseStatus;
      recovering.context.get = () =>
        new Promise((resolve) => {
          releaseStatus = resolve;
        });
      const refreshing = recovering.loadStatus();
      const refreshingView = new Review(
        findComponent(recovering.render(), Review).props,
      ).render();
      assert(textContent(refreshingView).includes('Please wait…'));
      assert(
        textContent(refreshingView).includes(
          'Waiting for the server to verify this review.',
        ),
      );
      assert(!textContent(refreshingView).includes('Recording confirmation'));
      releaseStatus(structuredClone(current.value));
      await refreshing;
      recovering.context.get = getStatus;
      await recovering.confirm();
      assert.equal(
        calls,
        0,
        'Recovery requires a fresh owner acknowledgement.',
      );
      recovering.setState({ acknowledged: true });
      await Promise.all([recovering.confirm(), recovering.confirm()]);
      assert.equal(
        calls,
        1,
        'One explicit retry sends only the exact saved proof.',
      );
      assert.equal(recovering.state.preview, null);
      assert.equal(memory.size, 0);
      assert.deepEqual(
        current.value,
        stateBeforeRecovery,
        'Recovery never changes trading controls or order state.',
      );
    }
  }

  const previous = overview();
  const saved = {
    kind: 'manual-sale',
    result: { ...manual },
    binding: ReconciliationSafety.binding(previous, 'WLD'),
  };
  ReconciliationProofStore.save(saved);
  const changedAccount = { value: previous };
  let forbiddenCalls = 0;
  const isolated = panelFor(changedAccount, async () => {
    forbiddenCalls += 1;
  });
  await isolated.loadStatus();
  changedAccount.value = { ...previous, accountKey: 'd'.repeat(64) };
  isolated.setState({ acknowledged: true });
  await isolated.confirm();
  assert.equal(
    forbiddenCalls,
    0,
    'Account changes before recovery must block submission.',
  );
  assert.equal(
    ReconciliationProofStore.read(account).result.proof,
    saved.result.proof,
  );
  ReconciliationProofStore.clear(account);

  ReconciliationProofStore.save(saved);
  let rejectedCalls = 0;
  const rejected = panelFor(
    { value: { ...previous, mode: 'paper' } },
    async () => {
      rejectedCalls += 1;
      throw new Error(
        'Review expired or material state changed; request a new preview.',
      );
    },
  );
  await rejected.loadStatus();
  rejected.setState({ acknowledged: true });
  await rejected.confirm();
  assert.equal(rejectedCalls, 1);
  assert.equal(rejected.state.uncertain, true);
  assert.equal(rejected.state.acknowledged, false);
  assert.equal(rejected.state.busy, false);
  assert.equal(
    ReconciliationProofStore.read(account).result.proof,
    saved.result.proof,
  );
  await rejected.loadStatus();
  assert.equal(
    rejectedCalls,
    1,
    'A rejected recovery is never retried automatically.',
  );
  ReconciliationProofStore.clear(account);
}

async function verify() {
  await verifySavedConfirmationRecovery();
  const retained = overview();
  const beforeHistory = ReconciliationSafety.material(retained);
  retained.live.orders = Array.from({ length: 1000 }, (_, index) => ({
    id: 'completed-' + index,
    symbol: 'BTC',
    side: 'buy',
    amount: '100',
    limitPrice: '100',
    status: 'filled',
    createdAt: now,
    brokerOrderId: 'old-broker-' + index,
    reason: 'Retained completed history',
    remaining: '0',
  }));
  assert.equal(
    ReconciliationSafety.material(retained),
    beforeHistory,
    'Completed order history does not enlarge or invalidate a current balance review.',
  );
  const preview = {
    kind: 'manual-sale',
    result: manual,
    binding: ReconciliationSafety.binding(retained, 'WLD'),
  };
  ReconciliationProofStore.save(preview);
  assert.equal(
    ReconciliationProofStore.read(account).result.proof,
    manual.proof,
  );
  ReconciliationProofStore.clear(account);

  const replacementProof = {
    ...preview,
    result: { ...manual, proof: 'fixture-newer-pending-proof' },
  };
  ReconciliationProofStore.save(replacementProof);
  ReconciliationProofStore.clear(account, manual.proof);
  assert.equal(
    ReconciliationProofStore.read(account)?.result.proof,
    replacementProof.result.proof,
    'A late older confirmation cannot clear a newer saved proof.',
  );
  ReconciliationProofStore.clear(account, replacementProof.result.proof);
  assert.equal(ReconciliationProofStore.read(account), null);

  let releaseOlderConfirmation;
  const olderPanel = panelFor(
    { value: overview() },
    () =>
      new Promise((resolve) => {
        releaseOlderConfirmation = resolve;
      }),
  );
  await olderPanel.preview('manual-sale');
  olderPanel.setState({ acknowledged: true });
  const olderConfirmation = olderPanel.confirm();
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(typeof releaseOlderConfirmation, 'function');
  olderPanel.componentWillUnmount();
  ReconciliationProofStore.clear(account);
  ReconciliationProofStore.save(replacementProof);
  releaseOlderConfirmation({ ...manual, applied: true });
  await olderConfirmation;
  assert.equal(
    ReconciliationProofStore.read(account)?.result.proof,
    replacementProof.result.proof,
    'An unmounted panel response preserves the replacement confirmation for recovery.',
  );
  ReconciliationProofStore.clear(account);

  const legacy = {
    ...preview,
    binding: { ...preview.binding, material: 'x'.repeat(25000) },
  };
  const key = 'quantara-reconciliation-pending:' + account;
  memory.set(key, JSON.stringify(legacy));
  const restored = ReconciliationProofStore.read(account);
  assert.equal(
    restored.binding.material,
    legacy.binding.material,
    'Previously saved material between the old limit and total size limit remains recoverable.',
  );
  assert.equal(
    restored.result.proof,
    legacy.result.proof,
    'Recovery retains the exact confirmation proof.',
  );
  assert.equal(
    restored.binding.originalLot.quantity,
    legacy.binding.originalLot.quantity,
  );
  const recovered = panelFor({ value: retained }, async () => {
    throw new Error('Recovery must not send');
  });
  await recovered.loadStatus();
  assert.equal(recovered.state.preview.result.proof, manual.proof);
  assert.equal(recovered.state.acknowledged, false);
  ReconciliationProofStore.clear(account);
  assert.throws(
    () =>
      ReconciliationProofStore.save({
        ...preview,
        result: { ...manual, proof: '' },
      }),
    /cannot be retained/,
  );
  assert.throws(
    () =>
      ReconciliationProofStore.save({
        ...legacy,
        binding: { ...legacy.binding, material: 'x'.repeat(33000) },
      }),
    /cannot be retained/,
  );
  assert.equal(
    memory.size,
    0,
    'Invalid confirmations are never persisted or sent.',
  );

  for (const invalid of [
    '{broken',
    'x'.repeat(32001),
    JSON.stringify({
      ...preview,
      binding: { ...preview.binding, accountKey: 'other-account' },
    }),
  ]) {
    memory.set(key, invalid);
    let posted = 0;
    const unreadable = panelFor({ value: retained }, async () => {
      posted++;
      return { ...manual, applied: true };
    });
    await unreadable.loadStatus();
    await unreadable.loadStatus();
    assert.equal(
      unreadable.state.overview.accountKey,
      account,
      'Current status remains readable.',
    );
    assert.equal(unreadable.state.preview, null);
    assert.equal(unreadable.state.unreadableAccount, account);
    assert.match(unreadable.state.error, /may already be recorded/);
    assert(
      findComponent(unreadable.render(), Button, 'Discard saved confirmation'),
    );
    await unreadable.preview('manual-sale');
    await unreadable.confirm();
    assert.equal(
      posted,
      0,
      'Unreadable prior confirmation blocks another review or apply.',
    );
    unreadable.discard();
    assert.equal(memory.size, 0);
    assert.equal(
      unreadable.state.overview,
      null,
      'Explicit discard requires a fresh status before continuing.',
    );
    assert.match(unreadable.state.notice, /does not undo or confirm/);
    await unreadable.loadStatus();
    assert.equal(unreadable.state.unreadableAccount, null);
    assert.equal(unreadable.state.error, null);
  }
  assert.equal(
    ReconciliationSafety.sameAmount('5096.69000000', '5096.69'),
    true,
  );
  assert.equal(
    ReconciliationSafety.sameAmount(
      '9007199254740993.00001902',
      '9007199254740993.00001903',
    ),
    false,
  );
  const fetchBefore = globalThis.fetch;
  const calls = [];
  process.env.QUANTARA_TRADING_CONTROL_TOKEN = 'synthetic-owner-token';
  process.env.PORTFOLIO_SERVICE_URL = 'http://fixture.invalid';
  let responseBody = wire(manual);
  globalThis.fetch = async (url, options) => {
    calls.push({ url, options });
    return new Response(responseBody, {
      headers: { 'Content-Type': 'application/json' },
    });
  };
  const request = (body, origin = 'http://127.0.0.1:3000') =>
    new NextRequest(
      'http://127.0.0.1:3000/api/trading/reconciliation/manual-sale/preview',
      {
        method: 'POST',
        headers: {
          host: '127.0.0.1:3000',
          'Content-Type': 'application/json',
          Origin: origin,
        },
        body: JSON.stringify(body),
      },
    );
  try {
    let result = await ReconciliationController.action(
      request({ symbol: 'WLD', executionId: 'fixture-sale' }),
      { params: Promise.resolve({ path: ['manual-sale', 'preview'] }) },
    );
    assert.equal(result.status, 200);
    const parsed = await result.json();
    assert.equal(parsed.allocatedSoldQuantity, manual.allocatedSoldQuantity);
    assert.equal(parsed.brokerBalance.available, '0');
    assert.equal(
      JSON.stringify(parsed).includes('synthetic-owner-token'),
      false,
    );
    assert.equal(
      calls[0].options.headers['X-Quantara-Trading-Control'],
      'synthetic-owner-token',
    );
    assert.equal(
      calls[0].url,
      'http://fixture.invalid/api/trading/reconciliation/manual-sale/preview',
    );
    responseBody = wire(cash);
    result = await ReconciliationController.action(request({}), {
      params: Promise.resolve({
        path: ['manual-sale', 'cash-baseline', 'preview'],
      }),
    });
    assert.equal(
      (await result.json()).cashThb,
      cash.cashThb,
      'Raw decimal tokens survive beyond Number precision.',
    );
    const before = calls.length;
    assert.equal(
      (
        await ReconciliationController.action(request({}), {
          params: Promise.resolve({ path: ['activate'] }),
        })
      ).status,
      404,
    );
    assert.equal(
      (
        await ReconciliationController.action(
          request({
            symbol: 'WLD',
            executionId: 'fixture-sale',
            autoApply: true,
          }),
          { params: Promise.resolve({ path: ['manual-sale', 'preview'] }) },
        )
      ).status,
      400,
    );
    assert.equal(
      (
        await ReconciliationController.action(
          request({}, 'https://evil.invalid'),
          {
            params: Promise.resolve({
              path: ['manual-sale', 'cash-baseline', 'preview'],
            }),
          },
        )
      ).status,
      403,
    );
    assert.equal(
      calls.length,
      before,
      'Rejected actions never reach the backend.',
    );
  } finally {
    globalThis.fetch = fetchBefore;
  }

  const current = { value: overview() };
  const initial = panelFor(current, async () => {
    throw new Error('No automatic apply');
  });
  initial.state.overview = null;
  initial.state.symbol = '';
  await initial.loadStatus();
  assert.equal(
    initial.state.symbol,
    'WLD',
    'The only retained lot is selected on first open.',
  );
  const sale = {
    transactionId: 'fixture-sale',
    symbol: 'WLD',
    side: TradeSide.Sell,
    quantity: 2.5,
    price: 40,
    fee: 0,
    feeCredit: 0,
    timestamp: now,
  };
  const queueCommits = (panel) => {
    const updates = [];
    panel.setState = (update) => updates.push(update);
    return async () => {
      for (let turn = 0; turn < 8; turn += 1) {
        await new Promise((resolve) => setImmediate(resolve));
        const batch = updates.splice(0);
        for (const update of batch) {
          panel.state = {
            ...panel.state,
            ...(typeof update === 'function' ? update(panel.state) : update),
          };
        }
        if (batch.length) panel.componentDidUpdate();
      }
    };
  };
  const noApply = async () => {
    throw new Error('Initial preview must never apply');
  };
  const manualMode = { value: overview() };
  manualMode.value.mode = 'manualReview';
  const paused = panelFor(manualMode, noApply, [sale]);
  let disableCalls = 0;
  paused.context.disable = async () => {
    disableCalls += 1;
    manualMode.value.mode = 'stopped';
    return structuredClone(manualMode.value);
  };
  const commitPaused = queueCommits(paused);
  await paused.loadStatus();
  await commitPaused();
  assert.equal(paused.state.preview, null);
  assert.equal(disableCalls, 0, 'Opening a review never stops trading.');
  const pauseButton = (node) => {
    if (!node || typeof node !== 'object') return null;
    if (node.props?.children === 'Pause trading to review') return node;
    for (const child of [node.props?.children].flat(Infinity)) {
      const result = pauseButton(child);
      if (result) return result;
    }
    return null;
  };
  assert(
    pauseButton(paused.render()),
    'Manual review needs an actionable stop.',
  );
  await Promise.all([paused.stopForReview(), paused.stopForReview()]);
  await commitPaused();
  assert.equal(disableCalls, 1, 'An explicit double click issues one stop.');
  assert.equal(paused.state.overview.mode, 'stopped');
  assert(paused.state.preview, 'Stopping prepares a fresh read-only review.');
  assert.equal(paused.state.acknowledged, false);
  const stopFailureState = { value: { ...overview(), mode: 'manualReview' } };
  const stopFailure = panelFor(stopFailureState, noApply, [sale]);
  let failedStops = 0;
  stopFailure.context.disable = async () => {
    failedStops += 1;
    throw new Error('Fixture stop failed');
  };
  const commitStopFailure = queueCommits(stopFailure);
  await stopFailure.loadStatus();
  await commitStopFailure();
  await stopFailure.stopForReview();
  await commitStopFailure();
  assert.equal(failedStops, 1);
  assert.match(stopFailure.state.error, /Fixture stop failed/);
  assert.equal(stopFailure.state.preview, null);
  assert(pauseButton(stopFailure.render()), 'A failed stop remains retryable.');
  const changedAccount = { value: { ...overview(), mode: 'manualReview' } };
  const switchedStop = panelFor(changedAccount, noApply, [sale]);
  switchedStop.context.disable = async () => {
    throw new Error('Must not stop a different account');
  };
  changedAccount.value = {
    ...changedAccount.value,
    accountKey: 'd'.repeat(64),
  };
  await switchedStop.stopForReview();
  assert.match(switchedStop.state.error, /account|changed/i);
  assert.equal(switchedStop.state.preview, null);
  const activeOrderState = { value: { ...overview(), mode: 'manualReview' } };
  const activeOrderReview = panelFor(activeOrderState, noApply, [sale]);
  activeOrderReview.context.disable = async () => {
    throw new Error('Must not ignore unresolved orders');
  };
  activeOrderState.value.live.orders = [{ status: 'unknown' }];
  assert.equal(pauseButton(activeOrderReview.render()), null);
  await activeOrderReview.stopForReview();
  assert.match(activeOrderReview.state.error, /Orders & positions/);
  const noMandate = panelFor(
    { value: { ...overview(), mandate: null } },
    noApply,
  );
  assert.equal(
    pauseButton(noMandate.render()),
    null,
    'Stopping cannot invent a mandate.',
  );
  const pendingStopState = { value: { ...overview(), mode: 'manualReview' } };
  const pendingStop = panelFor(pendingStopState, noApply, [sale]);
  const savedBeforeStop = {
    kind: 'manual-sale',
    result: manual,
    binding: ReconciliationSafety.binding(pendingStopState.value, 'WLD'),
  };
  pendingStop.context.pendingReconciliation = () => savedBeforeStop;
  pendingStop.context.disable = async () => {
    pendingStopState.value.mode = 'stopped';
    return structuredClone(pendingStopState.value);
  };
  const commitPendingStop = queueCommits(pendingStop);
  await pendingStop.loadStatus();
  await commitPendingStop();
  await pendingStop.stopForReview();
  await commitPendingStop();
  assert.deepEqual(pendingStop.state.preview, savedBeforeStop);
  assert.equal(pendingStop.state.uncertain, true);
  assert.equal(pendingStop.state.acknowledged, false);
  const automatic = panelFor(current, noApply, [sale]);
  automatic.state.overview = null;
  automatic.state.symbol = '';
  const commitAutomatic = queueCommits(automatic);
  await automatic.loadStatus();
  assert.equal(automatic.state.overview, null, 'State is not synchronous.');
  await commitAutomatic();
  assert(
    automatic.state.preview,
    'One matching sale prepares a read-only preview.',
  );
  assert.equal(automatic.state.acknowledged, false);
  automatic.discard();
  await commitAutomatic();
  await automatic.loadStatus();
  await commitAutomatic();
  assert.equal(automatic.state.preview, null, 'Discard/refresh never loop.');
  const ambiguous = panelFor(current, noApply, [
    sale,
    { ...sale, transactionId: 'second-sale' },
  ]);
  const commitAmbiguous = queueCommits(ambiguous);
  await ambiguous.loadStatus();
  await commitAmbiguous();
  assert.equal(
    ambiguous.state.preview,
    null,
    'Multiple sales require explicit selection.',
  );
  const cashOnly = { value: overview() };
  cashOnly.value.live.positions = [];
  const noLots = panelFor(cashOnly, noApply);
  const commitCash = queueCommits(noLots);
  await noLots.loadStatus();
  await commitCash();
  assert.equal(noLots.state.preview.kind, 'cash-baseline');
  const multipleLots = { value: overview() };
  multipleLots.value.live.positions.push({
    ...multipleLots.value.live.positions[0],
    symbol: 'BTC',
  });
  const multiple = panelFor(multipleLots, noApply, [sale]);
  const commitMultiple = queueCommits(multiple);
  await multiple.loadStatus();
  await commitMultiple();
  assert.equal(multiple.state.preview, null);
  const switching = panelFor(current, noApply);
  let releaseHistory;
  switching.props.history.get = () =>
    new Promise((resolve) => {
      releaseHistory = resolve;
    });
  const commitSwitch = queueCommits(switching);
  await switching.loadStatus();
  await commitSwitch();
  assert(releaseHistory, 'History request started after status committed.');
  switching.state.overview = { ...current.value, accountKey: 'd'.repeat(64) };
  releaseHistory({ trades: [sale] });
  await commitSwitch();
  assert.equal(switching.state.preview, null);
  assert.deepEqual(switching.state.trades, []);
  const failed = panelFor(current, noApply, [sale]);
  let previewCalls = 0;
  failed.context.previewManualSale = async () => {
    previewCalls += 1;
    throw new Error('Fixture preview failed');
  };
  const commitFailed = queueCommits(failed);
  await failed.loadStatus();
  await commitFailed();
  await failed.loadStatus();
  await commitFailed();
  assert.equal(
    previewCalls,
    1,
    'Failed preview is never retried automatically.',
  );
  const pending = panelFor(current, noApply, [sale]);
  const pendingPreview = {
    kind: 'manual-sale',
    result: manual,
    binding: ReconciliationSafety.binding(current.value, 'WLD'),
  };
  pending.context.pendingReconciliation = () => pendingPreview;
  pending.context.previewManualSale = async () => {
    throw new Error('Uncertain confirmation must not be re-previewed');
  };
  const commitPending = queueCommits(pending);
  await pending.loadStatus();
  await commitPending();
  assert.deepEqual(pending.state.preview, pendingPreview);
  assert.equal(pending.state.uncertain, true);

  const nextLot = {
    ...overview().live.positions[0],
    symbol: 'BTC',
    quantity: '1',
    costThb: '100',
  };
  async function nextStage(remainingLots, uncertain = false) {
    memory.clear();
    const live = { value: overview() };
    live.value.live.positions.push(...structuredClone(remainingLots));
    const historySymbols = [];
    const manualRequests = [];
    let cashPreviews = 0;
    let confirmations = 0;
    const transition = panelFor(live, async (proof) => {
      confirmations += 1;
      assert.equal(proof, manual.proof);
      live.value.live.positions = structuredClone(remainingLots);
      if (uncertain)
        throw new Error('Synthetic uncertain confirmation after commit');
      return { ...manual, applied: true };
    });
    transition.props.history.get = async (symbol) => {
      historySymbols.push(symbol);
      return {
        trades: [
          {
            ...sale,
            symbol,
            quantity: symbol === 'WLD' ? sale.quantity : 1,
            transactionId: symbol === 'WLD' ? sale.transactionId : 'next-sale',
          },
        ],
      };
    };
    transition.context.previewManualSale = async (symbol, executionId) => {
      manualRequests.push([symbol, executionId]);
      return {
        ...manual,
        symbol,
        allocatedSoldQuantity:
          symbol === 'WLD' ? manual.allocatedSoldQuantity : '1',
        unallocatedHistoricalQuantity:
          symbol === 'WLD' ? manual.unallocatedHistoricalQuantity : '0',
        proof: symbol === 'WLD' ? manual.proof : 'fixture-next-proof',
        brokerBalance: { ...manual.brokerBalance, symbol },
      };
    };
    transition.context.previewCashBaseline = async () => {
      cashPreviews += 1;
      return structuredClone(cash);
    };
    const commit = queueCommits(transition);
    await transition.loadStatus();
    await commit();
    if (!transition.state.preview) {
      findComponent(
        transition.render(),
        Button,
        'Find matching sales',
      ).props.onClick();
      await commit();
      findComponent(
        transition.render(),
        Button,
        'Verify sold holdings',
      ).props.onClick();
      await commit();
    }
    assert.equal(transition.state.preview.kind, 'manual-sale');
    assert.equal(transition.state.preview.result.symbol, 'WLD');
    transition.setState({ acknowledged: true });
    await commit();
    await transition.confirm();
    await commit();
    assert.equal(
      confirmations,
      1,
      'Only the owner-confirmed first stage is applied.',
    );
    assert.equal(
      transition.state.acknowledged,
      false,
      'The next stage never inherits acknowledgement.',
    );
    if (uncertain) {
      await transition.loadStatus();
      await commit();
      assert.equal(transition.state.preview.result.proof, manual.proof);
      assert.equal(transition.state.uncertain, true);
      assert.equal(
        cashPreviews,
        0,
        'Unknown confirmation outcome cannot prepare the cash stage.',
      );
      assert.deepEqual(historySymbols, ['WLD']);
      assert.deepEqual(manualRequests, [['WLD', sale.transactionId]]);
      assert.equal(
        ReconciliationProofStore.read(account).result.proof,
        manual.proof,
      );
    } else if (remainingLots.length === 0) {
      assert.equal(
        transition.state.preview?.kind,
        'cash-baseline',
        'A confirmed last sale prepares cash review.',
      );
      assert.equal(cashPreviews, 1);
      assert.deepEqual(historySymbols, ['WLD']);
      assert.deepEqual(manualRequests, [['WLD', sale.transactionId]]);
      assert.equal(
        memory.size,
        0,
        'The confirmed first proof is cleared before preparing another review.',
      );
    } else if (remainingLots.length === 1) {
      assert.equal(
        transition.state.symbol,
        'BTC',
        'The sole remaining lot is selected.',
      );
      assert.equal(transition.state.executionId, 'next-sale');
      assert.deepEqual(
        historySymbols,
        ['WLD', 'BTC'],
        'The next lot loads its own history once.',
      );
      assert.deepEqual(manualRequests, [
        ['WLD', sale.transactionId],
        ['BTC', 'next-sale'],
      ]);
      assert.equal(transition.state.preview?.kind, 'manual-sale');
      assert.equal(transition.state.preview.result.symbol, 'BTC');
      assert.equal(cashPreviews, 0);
    } else {
      assert.equal(
        transition.state.symbol,
        '',
        'Multiple remaining lots require explicit selection.',
      );
      assert.equal(transition.state.preview, null);
      assert.equal(transition.state.executionId, '');
      assert.deepEqual(transition.state.trades, []);
      assert.deepEqual(historySymbols, ['WLD']);
      assert.deepEqual(manualRequests, [['WLD', sale.transactionId]]);
      assert.equal(cashPreviews, 0);
    }
    assert.equal(transition.state.busy, false);
    memory.clear();
  }
  await nextStage([]);
  await nextStage([nextLot]);
  await nextStage([nextLot, { ...nextLot, symbol: 'ETH' }]);
  await nextStage([], true);

  const replacement = { value: overview() };
  replacement.value.live.positions.push(structuredClone(nextLot));
  const replacing = panelFor(replacement, noApply);
  replacing.state.trades = [sale];
  const commitReplacement = queueCommits(replacing);
  await replacing.loadStatus();
  await commitReplacement();
  replacement.value.live.positions = [structuredClone(nextLot)];
  await replacing.loadStatus();
  await commitReplacement();
  assert.equal(replacing.state.symbol, 'BTC');
  assert.equal(
    replacing.state.executionId,
    '',
    'Replacing a selected lot clears its previous sale ID.',
  );
  assert.deepEqual(
    replacing.state.trades,
    [],
    'The previous symbol history cannot appear under a new lot.',
  );
  assert.equal(
    findComponent(replacing.render(), Button, 'Verify sold holdings').props
      .disabled,
    true,
  );

  const priorWindow = global.window;
  global.window = { setInterval: () => 1, clearInterval: () => {} };
  try {
    const remounting = panelFor({ value: overview() }, noApply);
    remounting.state.overview = null;
    remounting.state.symbol = '';
    const releases = [];
    remounting.context.get = () =>
      new Promise((resolve) => releases.push(resolve));
    const commitRemount = queueCommits(remounting);
    remounting.componentDidMount();
    remounting.componentWillUnmount();
    remounting.componentDidMount();
    assert.equal(
      releases.length,
      2,
      'A remount starts a new status read despite the cancelled old request.',
    );
    releases[0](overview());
    await commitRemount();
    assert.equal(
      remounting.state.busy,
      true,
      'Old completion cannot clear the new busy status.',
    );
    assert.equal(
      remounting.inFlight,
      true,
      'Old finally cannot release the newer request gate.',
    );
    await remounting.loadStatus();
    assert.equal(
      releases.length,
      2,
      'The newer request still excludes concurrent status reads.',
    );
    const newest = overview();
    newest.live.orders = [{ status: 'unknown' }];
    releases[1](newest);
    await commitRemount();
    assert.equal(
      remounting.state.busy,
      false,
      'The latest status read settles idle.',
    );
    assert.equal(remounting.inFlight, false);
    assert.deepEqual(remounting.state.overview, newest);
    remounting.componentWillUnmount();
  } finally {
    global.window = priorWindow;
  }

  for (const cancellationState of ['claimed', 'unknown', 'confirmed']) {
    const terminal = overview();
    terminal.live.orders = [{ status: 'filled', cancellationState }];
    assert.equal(
      ReconciliationSafety.stopped(terminal),
      cancellationState === 'confirmed',
      'Terminal order status cannot hide an unresolved cancellation.',
    );
    if (cancellationState !== 'confirmed') {
      let forbiddenApplies = 0;
      const blocked = panelFor({ value: terminal }, async () => {
        forbiddenApplies += 1;
      });
      blocked.state.preview = {
        kind: 'manual-sale',
        result: manual,
        binding: ReconciliationSafety.binding(overview(), 'WLD'),
      };
      blocked.state.acknowledged = true;
      await blocked.confirm();
      assert.equal(
        forbiddenApplies,
        0,
        'An unresolved cancellation cannot apply a fresh review.',
      );
    }
  }
  let applied = 0;
  let panel = panelFor(current, async () => {
    applied += 1;
    return { ...manual, applied: true };
  });
  await panel.preview('manual-sale');
  assert(panel.state.preview);
  await panel.confirm();
  assert.equal(applied, 0, 'Preview never auto-acknowledges or applies.');
  panel.setState({ acknowledged: true });
  await Promise.all([panel.confirm(), panel.confirm()]);
  assert.equal(applied, 1, 'Double click has one submission.');
  assert.equal(memory.size, 0);

  panel = panelFor(current, async () => {
    applied += 1;
    throw new Error('Synthetic interrupted response');
  });
  await panel.preview('manual-sale');
  panel.setState({ acknowledged: true });
  await panel.confirm();
  assert.equal(panel.state.uncertain, true);
  assert.equal(panel.state.acknowledged, false);
  panel.setState({ busy: true });
  const pendingReview = findComponent(panel.render(), Review);
  assert(pendingReview);
  const pendingText = textContent(new Review(pendingReview.props).render());
  assert(pendingText.includes('Waiting for the server to verify this review'));
  assert(!pendingText.includes('The confirmation may already be saved'));
  panel.setState({ busy: false });
  const uncertainReview = findComponent(panel.render(), Review);
  assert(uncertainReview);
  const uncertainText = textContent(new Review(uncertainReview.props).render());
  assert(uncertainText.includes('Retry only this same confirmation'));
  const saved = ReconciliationProofStore.read(account);
  assert.equal(saved.result.proof, manual.proof);
  const callsAfterFailure = applied;
  current.value.rulesHash = 'c'.repeat(64);
  await panel.loadStatus();
  assert.equal(
    panel.state.preview.result.proof,
    manual.proof,
    'Changed rules do not hide unresolved confirmation.',
  );
  assert.equal(panel.state.uncertain, true);
  assert.equal(
    ReconciliationSafety.current(panel.state.preview, current.value, true),
    true,
  );
  assert.throws(
    () =>
      ReconciliationProofStore.save({
        ...saved,
        result: { ...manual, proof: 'different-proof' },
      }),
    /unresolved/,
  );
  assert.equal(applied, callsAfterFailure, 'Reload never retries.');
  panel = panelFor(current, async () => {
    throw new Error('Must not run');
  });
  await panel.loadStatus();
  assert.equal(
    panel.state.acknowledged,
    false,
    'Restored confirmation requires a new explicit owner check.',
  );
  assert.equal(panel.state.preview.result.proof, manual.proof);
  panel.discard();
  assert.equal(memory.size, 0);

  current.value = overview();
  panel = panelFor(current, async () => {
    throw new Error('Expired proof must not submit');
  });
  await panel.preview('manual-sale');
  panel.state.preview.result.expiresAt = new Date(Date.now() - 1).toISOString();
  panel.setState({ acknowledged: true });
  await panel.confirm();
  assert.equal(panel.state.uncertain, false);
  current.value.accountKey = 'd'.repeat(64);
  await panel.loadStatus();
  assert.equal(panel.state.preview, null);
  assert.equal(panel.state.executionId, '');
  assert.equal(panel.state.trades.length, 0);
  console.log(
    'PASS: exact numeric wire proxy, owner token isolation, strict routes/CSRF, explicit confirmation, doubleclick, interruption/recovery, next review stages, remount races, selection invalidation, unresolved cancellation, changed rules, expiry, account switch; synthetic only.',
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
