import assert from 'node:assert/strict';
import { CodexOutputSchema } from '../services/ai-worker/CodexOutputSchema.mjs';
import { CodexTeamWorker } from '../services/ai-worker/CodexTeamWorker.mjs';

const base = {
  output: 'ok',
  proposal: null,
  lesson: { lesson: 'x', evidence: 'y', nextTest: 'z' },
  researchSymbols: [],
  assessment: null,
};
const candidate = {
  runId: '00000000-0000-0000-0000-000000000001',
  accountKey: 'a'.repeat(64),
  baselineRulesHash: 'b'.repeat(64),
  asOf: '2026-10-28T00:00:00Z',
  expiresAt: '2026-10-28T00:20:00Z',
  evidenceIds: ['evidence-1'],
  reason: 'fresh evidence',
  parameters: {
    maxPositions: 2,
    maxSpreadBps: 50,
    slippageBps: 20,
    takeProfitBps: 200,
    stopLossBps: 100,
    trailingStopBps: 100,
    volumeRatio: 1.5,
    timeExitMinutes: 120,
    cooldownMinutes: 15,
    orderLifetimeMinutes: 15,
    maximumExposureFraction: 1,
    maximumConcentrationFraction: 1,
    riskToStopFraction: 0.5,
    dailyLossFraction: 0.5,
    atrMultiplier: 1,
  },
};
const enabled = CodexOutputSchema.create('reviewer', false, {
  aiPolicy: { enabled: true },
});
assert(enabled.required.includes('policyCandidate'));
assert(
  Object.keys(enabled.properties).every((name) =>
    enabled.required.includes(name),
  ),
);
function assertRequired(node) {
  if (!node || typeof node !== 'object') return;
  if (node.type === 'object' && node.properties) {
    assert.deepEqual(
      Object.keys(node.properties).sort(),
      [...(node.required ?? [])].sort(),
    );
    Object.values(node.properties).forEach(assertRequired);
  }
  (node.anyOf ?? []).forEach(assertRequired);
}
assertRequired(enabled);
const disabled = CodexOutputSchema.create('reviewer', false, null);
assert(!Object.hasOwn(disabled.properties, 'policyCandidate'));
assert(!disabled.required.includes('policyCandidate'));
const decoded = CodexTeamWorker.decodeResponse(
  JSON.stringify({ ...base, policyCandidate: candidate }),
  'reviewer',
  false,
  true,
);
assert.deepEqual(decoded.policyCandidate, candidate);
assert.throws(() =>
  CodexTeamWorker.decodeResponse(
    JSON.stringify({ ...base, policyCandidate: candidate }),
    'reviewer',
    false,
    false,
  ),
);
console.log('AI policy worker schema and roundtrip checks passed.');
