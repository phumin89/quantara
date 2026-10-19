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
console.log('Assessment and news fallback fixtures passed.');
