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
const {
  RequestService,
} = require('../apps/web/shared/services/RequestService.ts');
const {
  PortfolioService,
} = require('../apps/web/features/crypto/services/PortfolioService.ts');
const {
  HistoryService,
} = require('../apps/web/features/crypto/services/HistoryService.ts');
const {
  TradingService,
} = require('../apps/web/features/trading/services/TradingService.ts');
const {
  OrdersService,
} = require('../apps/web/features/orders/services/OrdersService.ts');
const uncertainWrite =
  'The action may have completed. Refresh status before taking another action.';

async function verify() {
  const calls = [];
  let response = new Response(
    JSON.stringify({ trades: [], coverage: 'Fixture' }),
  );
  let transportFailure = null;
  const requests = new RequestService('/api/', async function (url, options) {
    assert.equal(
      this,
      globalThis,
      'Browser fetch must keep its global receiver',
    );
    calls.push({ url, options });
    if (options.signal?.aborted)
      throw new DOMException('Aborted', 'AbortError');
    if (transportFailure) throw transportFailure;
    return response;
  });
  const history = new HistoryService(requests);
  const portfolio = new PortfolioService(requests);
  const controller = new AbortController();
  assert.deepEqual(await history.get('A&B', controller.signal), {
    trades: [],
    coverage: 'Fixture',
  });
  assert.equal(calls[0].url, '/api/crypto/history?coin=A%26B');
  assert.equal(calls[0].options.signal, controller.signal);
  assert.equal(calls[0].options.cache, 'no-store');
  assert.equal(calls[0].options.method, 'GET');
  response = new Response('', { status: 202 });
  await portfolio.sync(controller.signal);
  assert.equal(calls[1].url, '/api/crypto/sync');
  assert.equal(calls[1].options.method, 'POST');
  response = new Response(JSON.stringify({ error: 'Fixture failure' }), {
    status: 503,
  });
  await assert.rejects(history.get('BTC'), /Fixture failure/);
  response = new Response('{}', { status: 503 });
  await assert.rejects(portfolio.sync(), { message: uncertainWrite });
  response = new Response('{broken');
  await assert.rejects(history.get('BTC'), /Unable to load history/);
  response = new Response('<html>failure</html>', { status: 502 });
  await assert.rejects(history.get('BTC'), /Unable to load history\./);
  response = new Response('{broken', { status: 502 });
  await assert.rejects(history.get('BTC'), /Unable to load history\./);
  response = new Response('plain text failure', { status: 502 });
  await assert.rejects(history.get('BTC'), /Unable to load history\./);
  response = new Response('{"error":"   "}', { status: 503 });
  await assert.rejects(portfolio.sync(), { message: uncertainWrite });
  response = new Response('{"error":""}', { status: 503 });
  await assert.rejects(portfolio.sync(), { message: uncertainWrite });
  response = new Response('{broken');
  const malformedSendCalls = calls.length;
  await assert.rejects(
    requests.send(
      '/team/runs',
      { objective: 'Malformed' },
      'Unable to send task',
    ),
    { message: uncertainWrite },
  );
  assert.equal(calls.length, malformedSendCalls + 1);
  const streamAbort = new DOMException('Stream aborted', 'AbortError');
  const streamResponse = () =>
    new Response(
      new ReadableStream({
        start(controller) {
          controller.error(streamAbort);
        },
      }),
    );
  response = streamResponse();
  await assert.rejects(history.get('BTC'), (error) => error === streamAbort);
  const streamSendCalls = calls.length;
  response = streamResponse();
  await assert.rejects(
    requests.send(
      '/team/runs',
      { objective: 'Stream abort' },
      'Unable to send task',
    ),
    (error) => error === streamAbort,
  );
  assert.equal(calls.length, streamSendCalls + 1);
  response = new Response(JSON.stringify({ trades: 'invalid' }));
  await assert.rejects(history.get('BTC'), /History response is invalid/);
  const trade = {
    transactionId: 'history-fixture',
    symbol: 'BTC',
    side: 'buy',
    quantity: 0,
    price: 100,
    fee: 0,
    feeCredit: 0,
    timestamp: '2026-10-27T08:00:00.1234567+07:00',
  };
  for (const invalidTrade of [
    { ...trade, symbol: 'ETH' },
    { ...trade, timestamp: 'invalid' },
    { ...trade, timestamp: '2026-10-27T08:00:00' },
  ]) {
    response = new Response(JSON.stringify({ trades: [invalidTrade] }));
    await assert.rejects(history.get('BTC'), /History response is invalid/);
  }
  for (const symbol of ['BTC', 'btc']) {
    response = new Response(
      JSON.stringify({ trades: [trade], coverage: 'Fixture' }),
    );
    assert.deepEqual(await history.get(symbol), {
      trades: [trade],
      coverage: 'Fixture',
    });
  }
  response = new Response(
    JSON.stringify({ trades: [{ ...trade, symbol: 'ıtc' }] }),
  );
  await assert.rejects(history.get('ITC'), /History response is invalid/);
  response = new Response('{}');
  await assert.rejects(portfolio.get(), /Portfolio response is invalid/);
  assert.equal(calls.at(-1).url, '/api/crypto/portfolio');
  const portfolioFixture = structuredClone(
    require('./fixtures/workspace-data.cjs').data['/api/crypto/portfolio'],
  );
  const onePosition = {
    ...portfolioFixture,
    positions: [portfolioFixture.positions[0]],
    summary: {
      ...portfolioFixture.summary,
      totalAssets: portfolioFixture.positions[0].value,
      holdingCount: 1,
    },
  };
  for (const symbol of [
    '',
    'BTC/THB',
    '__proto__',
    'BTC ',
    'BTC\n',
    '฿TC',
    'A'.repeat(31),
  ]) {
    response = new Response(
      JSON.stringify({
        ...onePosition,
        positions: [{ ...onePosition.positions[0], symbol }],
      }),
    );
    await assert.rejects(
      portfolio.get(),
      /Portfolio response is invalid/,
      `Invalid portfolio symbol accepted: ${JSON.stringify(symbol)}`,
    );
  }
  for (const symbol of ['BTC', 'btc']) {
    response = new Response(
      JSON.stringify({
        ...portfolioFixture,
        positions: [
          portfolioFixture.positions[0],
          { ...portfolioFixture.positions[0], symbol },
        ],
        summary: {
          ...portfolioFixture.summary,
          totalAssets: 15000,
          holdingCount: 2,
        },
      }),
    );
    await assert.rejects(
      portfolio.get(),
      /Portfolio response is invalid/,
      'Ambiguous duplicate portfolio symbols must be rejected.',
    );
  }
  for (const symbol of [
    'BTC',
    'btc',
    '1INCH',
    'constructor',
    'toString',
    'hasOwnProperty',
    'A'.repeat(30),
  ]) {
    const validPortfolio = {
      ...onePosition,
      positions: [{ ...onePosition.positions[0], symbol }],
    };
    response = new Response(JSON.stringify(validPortfolio));
    assert.deepEqual(await portfolio.get(), validPortfolio);
  }
  const emptyPortfolio = {
    ...portfolioFixture,
    positions: [],
    summary: { totalAssets: null, availableThb: null, holdingCount: null },
    updatedAt: null,
  };
  response = new Response(JSON.stringify(emptyPortfolio));
  assert.deepEqual(await portfolio.get(), emptyPortfolio);
  controller.abort();
  await assert.rejects(history.get('BTC', controller.signal), {
    name: 'AbortError',
  });
  const targets = [];
  const alternate = new RequestService(
    'https://service.invalid/api/',
    async (url) => {
      targets.push(url);
      return new Response('{}');
    },
  );
  await alternate.get('health?check=1', 'Fixture');
  await alternate.get('/health?check=2', 'Fixture');
  assert.deepEqual(targets, [
    'https://service.invalid/api/health?check=1',
    'https://service.invalid/api/health?check=2',
  ]);
  response = new Response(JSON.stringify({ accepted: true }));
  assert.deepEqual(
    await requests.send(
      '/team/runs',
      { objective: 'New task' },
      'Fixture failure',
    ),
    { accepted: true },
  );
  const submitted = calls.at(-1);
  assert.equal(submitted.url, '/api/team/runs');
  assert.equal(submitted.options.headers['Content-Type'], 'application/json');
  assert.equal(
    submitted.options.body,
    JSON.stringify({ objective: 'New task' }),
  );

  for (const mode of ['transport', 'body', 'malformed', 'empty-error']) {
    transportFailure =
      mode === 'transport'
        ? new TypeError('Synthetic browser failure secret=do-not-leak')
        : null;
    response =
      mode === 'body'
        ? new Response(
            new ReadableStream({
              start(controller) {
                controller.error(
                  new TypeError('Synthetic body failure secret=do-not-leak'),
                );
              },
            }),
          )
        : new Response(mode === 'malformed' ? '{broken' : '{}', {
            status: mode === 'empty-error' ? 503 : 200,
          });
    const beforeFailure = calls.length;
    await assert.rejects(
      requests.send('/trading/disable', {}, 'Unable to update controls.'),
      { message: uncertainWrite },
      mode + ': an attempted write cannot be reported as definitely incomplete',
    );
    assert.equal(
      calls.length,
      beforeFailure + 1,
      'Failed writes are never repeated.',
    );
  }
  const browserReadFailure = new TypeError('Synthetic browser read failure');
  transportFailure = browserReadFailure;
  await assert.rejects(
    requests.get('/trading', 'Read fallback'),
    (error) => error === browserReadFailure,
  );
  const browserAbort = new DOMException('Synthetic cancellation', 'AbortError');
  transportFailure = browserAbort;
  await assert.rejects(
    requests.send('/trading/disable', {}, 'Write fallback'),
    (error) => error === browserAbort,
  );
  transportFailure = null;
  const cyclic = {};
  cyclic.self = cyclic;
  const beforeSerialization = calls.length;
  await assert.rejects(
    requests.send('/trading/config', cyclic, 'Write fallback'),
    (error) => error instanceof TypeError && /circular/i.test(error.message),
  );
  assert.equal(
    calls.length,
    beforeSerialization,
    'Serialization failure happens before dispatch.',
  );
  response = Response.json(
    { error: 'Verified account conflict.' },
    { status: 409 },
  );
  await assert.rejects(
    requests.send('/trading/disable', {}, 'Write fallback'),
    { message: 'Verified account conflict.' },
  );
  const trading = new TradingService(requests);
  const orders = new OrdersService(requests);
  for (const call of [
    () => trading.disable(),
    () => orders.approve('synthetic-order'),
  ]) {
    response = Response.json({});
    const beforeInvalid = calls.length;
    await assert.rejects(call(), { message: uncertainWrite });
    assert.equal(
      calls.length,
      beforeInvalid + 1,
      'Invalid write DTOs cannot trigger another submission.',
    );
  }
  const contradictoryCost = {
    ...onePosition,
    positions: [
      { ...onePosition.positions[0], costStatus: 'Unknown', averageCost: 100 },
    ],
  };
  const invalidPortfolios = [
    ['Unknown cost cannot carry a verified average.', contradictoryCost],
  ];
  for (const field of ['holdingCount', 'completedSymbols', 'totalSymbols']) {
    for (const value of [-1, 0.5]) {
      invalidPortfolios.push([
        `${field} rejects ${value}.`,
        field === 'holdingCount'
          ? {
              ...onePosition,
              summary: { ...onePosition.summary, holdingCount: value },
            }
          : { ...onePosition, [field]: value },
      ]);
    }
  }
  const boundaryChecks = invalidPortfolios.map(async ([label, payload]) => {
    const reader = new PortfolioService({ get: async () => payload });
    await assert.rejects(reader.get(), /Portfolio response is invalid/, label);
  });
  const proposal = {
    id: 'a0000000-0000-4000-8000-000000000001',
    symbol: 'ETH',
    side: 'buy',
    amount: '100',
    limitPrice: '110',
    notionalThb: '100',
    estimatedFeeThb: '0.25',
    referencePrice: '110',
    reason: 'Synthetic identity boundary',
    createdAt: '2026-10-01T00:00:00Z',
    expiresAt: '2026-11-01T00:00:00Z',
    status: 'pending',
    brokerOrderId: null,
    message: null,
  };
  const otherProposal = {
    ...proposal,
    id: 'b0000000-0000-4000-8000-000000000002',
  };
  for (const id of [proposal.id, proposal.id.toUpperCase()]) {
    boundaryChecks.push(
      (async () => {
        const reader = new OrdersService({
          get: async () => ({
            proposals: [
              proposal,
              {
                ...proposal,
                id,
                side: 'sell',
                amount: '1',
                notionalThb: '110',
                estimatedFeeThb: '0.275',
              },
            ],
          }),
        });
        await assert.rejects(
          reader.list(),
          /Order proposals response is invalid/,
          'Duplicate canonical order identity.',
        );
      })(),
    );
  }
  for (const action of ['approve', 'reject', 'reconcile']) {
    boundaryChecks.push(
      (async () => {
        let writes = 0;
        const writer = new OrdersService({
          send: async () => {
            writes++;
            return otherProposal;
          },
        });
        await assert.rejects(
          writer[action](proposal.id),
          { message: uncertainWrite },
          `${action} binds the requested order.`,
        );
        assert.equal(writes, 1, 'An uncertain identity is never retried.');
      })(),
    );
  }
  const boundaryResults = await Promise.allSettled(boundaryChecks);
  const boundaryFailures = boundaryResults.filter(
    (result) => result.status === 'rejected',
  );
  assert.equal(
    boundaryFailures.length,
    0,
    boundaryFailures.map((result) => result.reason.message).join('\n'),
  );
  const knownRealized = {
    ...onePosition,
    positions: [
      {
        ...onePosition.positions[0],
        costStatus: 'Unknown',
        averageCost: null,
        realizedPnl: 12.25,
      },
    ],
  };
  assert.deepEqual(
    await new PortfolioService({ get: async () => knownRealized }).get(),
    knownRealized,
  );
  const closed = {
    ...onePosition,
    positions: [
      {
        ...onePosition.positions[0],
        status: 'Closed',
        quantity: 0,
        value: 0,
        costStatus: 'Verified',
        averageCost: null,
        unrealizedPnl: 0,
        realizedPnl: 12.25,
      },
    ],
    summary: { ...onePosition.summary, totalAssets: 0, holdingCount: 0 },
  };
  assert.deepEqual(
    await new PortfolioService({ get: async () => closed }).get(),
    closed,
  );
  const zeroProgress = {
    ...emptyPortfolio,
    completedSymbols: 0,
    totalSymbols: 0,
  };
  assert.deepEqual(
    await new PortfolioService({ get: async () => zeroProgress }).get(),
    zeroProgress,
  );
  const listing = { proposals: [proposal, otherProposal] };
  assert.deepEqual(
    await new OrdersService({ get: async () => listing }).list(),
    listing,
  );
  for (const [action, status] of Object.entries({
    approve: 'submitted',
    reject: 'rejected',
    reconcile: 'unknown',
  })) {
    const receipt = { ...proposal, id: proposal.id.toUpperCase(), status };
    const writer = new OrdersService({ send: async () => receipt });
    assert.deepEqual(
      await writer[action](proposal.id),
      receipt,
      'Canonical identity case is preserved.',
    );
  }
  const creator = new OrdersService({ send: async () => otherProposal });
  assert.deepEqual(
    await creator.create({
      symbol: 'ETH',
      side: 'buy',
      amount: '100',
      limitPrice: '110',
      reason: 'Synthetic creation',
    }),
    otherProposal,
  );
  console.log(
    'Shared RequestService: transport, errors, cancellation and feature validation passed.',
  );
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
