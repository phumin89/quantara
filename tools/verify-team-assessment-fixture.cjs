const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const { createRequire } = require('node:module');
const path = require('node:path');

function loadSchema(filename) {
  const source = fs.readFileSync(filename, 'utf8');
  const output = ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  }).outputText;
  const module = { exports: {} };
  new Function('require', 'module', 'exports', output)(
    createRequire(path.resolve(filename)),
    module,
    module.exports,
  );
  return module.exports;
}

require.extensions['.ts'] = (module, filename) => {
  module.exports = loadSchema(filename);
};

const current = {
  schemaVersion: 'research-assessment-v1',
  accountKey: 'a'.repeat(64),
  runId: '00000000-0000-4000-8000-000000000001',
  policyHash: null,
  asOf: '2026-10-18T12:00:00Z',
  expiresAt: '2026-10-18T18:00:00Z',
  evidenceIds: ['market:BTC'],
  positions: [
    {
      symbol: 'BTC',
      action: 'Keep',
      thesis: 'Trend intact',
      reason: 'Fresh bars',
      reviewAfter: '2026-10-18T13:00:00Z',
      expiresAt: '2026-10-18T18:00:00Z',
      missingFields: [],
    },
  ],
  opportunities: [],
};
const newsFallback = {
  schema: 'news-v1',
  observedAt: '2026-10-18T12:00:00Z',
  executionAuthority: false,
  events: [],
  omittedEventCount: 0,
  sourceStates: [
    {
      id: 'market-data',
      label: 'Market data',
      url: 'https://example.com',
      status: 'Unavailable',
      lastSuccessAt: null,
      nextRetryAt: null,
      reason: 'source_unavailable',
    },
  ],
  pilot: {
    startedAt: null,
    lastPollAt: '2026-10-18T12:00:00Z',
    completed72Hours: false,
    gapCount: 1,
  },
};
const validEvent = {
  eventId: 'event-1',
  revision: 1,
  sourceId: 'market-data',
  title: 'BTC update',
  url: 'https://example.com/news',
  publishedAt: null,
  firstSeenAt: '2026-10-18T12:00:00Z',
  revisionSeenAt: '2026-10-18T12:00:00Z',
  fetchedAt: '2026-10-18T12:00:00Z',
  expiresAt: null,
  symbols: ['BTC'],
  marketWide: false,
  category: 'market',
  factStatus: 'confirmed',
  contentHash: 'hash-1',
};
const assessmentSchema = loadSchema(
  'apps/web/features/team/contracts/TradeAssessment.ts',
).tradeAssessmentSchema;
const newsSchema = loadSchema(
  'apps/web/features/team/contracts/NewsPacket.ts',
).newsPacketSchema;
const teamRunSchema = loadSchema(
  'apps/web/features/team/contracts/TeamRun.ts',
).teamRunSchema;
const run = {
  id: current.runId,
  accountKey: current.accountKey,
  objective: 'Review evidence',
  status: 'completed',
  createdAt: current.asOf,
  updatedAt: current.asOf,
  error: null,
  steps: ['ceo', 'analyst', 'reviewer'].map((role) => ({
    role,
    status: 'completed',
    output: 'Report',
    error: null,
  })),
  assessment: current,
};
assert.equal(teamRunSchema.safeParse(run).success, true);
for (const roles of [
  ['ceo', 'reviewer'],
  ['intraday', 'reviewer'],
  ['swing', 'reviewer'],
  ['news', 'reviewer'],
  ['positions', 'reviewer'],
  ['quant', 'reviewer'],
]) {
  assert.equal(
    teamRunSchema.safeParse({
      ...run,
      steps: roles.map((role) => ({
        role,
        status: 'completed',
        output: 'Report',
        error: null,
      })),
      assessment: undefined,
    }).success,
    true,
  );
}
for (const roles of [
  ['analyst', 'reviewer'],
  ['ceo', 'ceo', 'reviewer'],
  ['intraday', 'analyst', 'reviewer'],
  ['ceo', 'reviewer', 'intraday'],
]) {
  assert.equal(
    teamRunSchema.safeParse({
      ...run,
      steps: roles.map((role) => ({
        role,
        status: 'completed',
        output: 'Report',
        error: null,
      })),
      assessment: undefined,
    }).success,
    false,
  );
}
assert.equal(
  teamRunSchema.safeParse({
    ...run,
    steps: run.steps.map((step, index) =>
      index === 0
        ? {
            ...step,
            usage: { inputTokens: 10, outputTokens: 5, cachedInputTokens: 2 },
          }
        : step,
    ),
  }).success,
  true,
);
assert.equal(
  teamRunSchema.safeParse({ ...run, accountKey: 'b'.repeat(64) }).success,
  false,
);
assert.equal(
  teamRunSchema.safeParse({
    ...run,
    id: '00000000-0000-4000-8000-000000000002',
  }).success,
  false,
);
assert.equal(
  teamRunSchema.safeParse({
    ...run,
    accountKey: undefined,
    assessment: undefined,
  }).success,
  true,
);
assert.equal(assessmentSchema.safeParse(current).success, true);
assert.equal(newsSchema.safeParse(newsFallback).success, true);
assert.equal(
  assessmentSchema.safeParse({
    ...current,
    positions: [{ ...current.positions[0], action: 'Buy' }],
  }).success,
  false,
);
assert.equal(
  assessmentSchema.safeParse({
    ...current,
    schemaVersion: 'research-assessment-v2',
  }).success,
  false,
);
assert.equal(
  newsSchema.safeParse({
    ...newsFallback,
    sourceStates: [
      { ...newsFallback.sourceStates[0], url: 'http://untrusted.invalid' },
    ],
  }).success,
  false,
);
assert.equal(
  newsSchema.safeParse({
    ...newsFallback,
    events: [{ ...validEvent, revision: 0 }],
  }).success,
  false,
);
const completedNews = {
  ...newsFallback,
  sourceStates: ['federal-reserve', 'bls'].map((id) => ({
    id,
    label: id,
    url:
      id === 'bls'
        ? 'https://www.bls.gov/feed/bls_latest.rss'
        : 'https://www.federalreserve.gov/feeds/press_all.xml',
    status: 'NoNewEvents',
    lastSuccessAt: '2026-10-18T11:45:00Z',
    nextRetryAt: null,
    reason: null,
  })),
  pilot: {
    startedAt: '2026-10-15T12:00:00Z',
    lastPollAt: '2026-10-18T11:50:00Z',
    completed72Hours: true,
    gapCount: 3,
  },
};
assert.equal(newsSchema.safeParse(completedNews).success, true);
assert.equal(
  newsSchema.safeParse({
    ...completedNews,
    sourceStates: [
      ...completedNews.sourceStates,
      { ...newsFallback.sourceStates[0], id: 'bitkub' },
    ],
  }).success,
  true,
);
const invalidPilots = [
  { startedAt: null },
  { startedAt: '2026-10-15T12:00:00.001Z' },
  { startedAt: '2026-10-15T12:00:00.0000001Z' },
  { startedAt: '2026-10-18T11:55:00Z' },
  { lastPollAt: null },
  { lastPollAt: '2026-10-18T11:49:59.999Z' },
  { lastPollAt: '2026-10-18T12:00:05.001Z' },
  { lastPollAt: '2026-10-18T12:00:05.0000001Z' },
  { lastPollAt: '2026-10-15T11:59:59Z' },
];
for (const pilot of invalidPilots) {
  assert.equal(
    newsSchema.safeParse({
      ...completedNews,
      pilot: { ...completedNews.pilot, ...pilot },
    }).success,
    false,
    `Unproved completed pilot accepted: ${JSON.stringify(pilot)}`,
  );
}
for (const source of [
  { status: 'Unavailable' },
  { status: 'Stale' },
  { lastSuccessAt: null },
  { lastSuccessAt: '2026-10-18T11:44:59.999Z' },
  { lastSuccessAt: '2026-10-18T12:00:05.001Z' },
  { lastSuccessAt: '2026-10-18T12:00:05.0000001Z' },
]) {
  assert.equal(
    newsSchema.safeParse({
      ...completedNews,
      sourceStates: [
        { ...completedNews.sourceStates[0], ...source },
        completedNews.sourceStates[1],
      ],
    }).success,
    false,
    `Unproved source accepted for completed pilot: ${JSON.stringify(source)}`,
  );
}
for (const sourceStates of [
  [],
  completedNews.sourceStates.slice(0, 1),
  [completedNews.sourceStates[0], completedNews.sourceStates[0]],
  [
    ...completedNews.sourceStates,
    { ...newsFallback.sourceStates[0], status: 'Healthy' },
  ],
]) {
  assert.equal(
    newsSchema.safeParse({ ...completedNews, sourceStates }).success,
    false,
  );
}
assert.equal(
  newsSchema.safeParse({
    ...completedNews,
    pilot: { ...completedNews.pilot, lastPollAt: '2026-10-18T12:00:05Z' },
    sourceStates: completedNews.sourceStates.map((source) => ({
      ...source,
      status: 'Healthy',
      lastSuccessAt: '2026-10-18T12:00:05Z',
    })),
  }).success,
  true,
);
assert.equal(
  newsSchema.safeParse({
    ...completedNews,
    observedAt: '2026-10-18T19:00:00.1234567+07:00',
    pilot: {
      ...completedNews.pilot,
      startedAt: '2026-10-15T12:00:00.1234567Z',
      lastPollAt: '2026-10-18T11:50:00.1234567Z',
    },
    sourceStates: completedNews.sourceStates.map((source) => ({
      ...source,
      lastSuccessAt: '2026-10-18T11:45:00.1234567Z',
    })),
  }).success,
  true,
);
console.log('Assessment, news fallback and pilot evidence fixtures passed.');
