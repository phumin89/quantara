import assert from 'node:assert/strict';
import { CodexModelPolicy } from '../services/ai-worker/CodexModelPolicy.mjs';
import { CodexTeamWorker } from '../services/ai-worker/CodexTeamWorker.mjs';
import { WorkerFailure } from '../services/ai-worker/WorkerFailure.mjs';

const source = { CODEX_API_KEY: 'synthetic-not-a-credential' };
let calls = 0;
for (const [role, profile] of Object.entries(CodexModelPolicy.roles)) {
  const input = {
    role,
    objective: 'Synthetic contract check',
    previous:
      role === 'analyst'
        ? [{ role: 'ceo', output: 'Synthetic' }]
        : role === 'reviewer'
          ? [{ role: 'intraday', output: 'Synthetic' }]
          : [],
    portfolio: null,
  };
  const factory = () => ({
    startThread: (options) => {
      assert.equal(options.model, profile.model);
      assert.equal(options.modelReasoningEffort, profile.effort);
      assert.equal(options.networkAccessEnabled, false);
      assert.equal(options.approvalPolicy, 'never');
      return {
        run: async () => {
          calls++;
          return {
            model: 'untrusted-extra-sdk-field',
            items: [],
            usage: {
              input_tokens: 12,
              output_tokens: 3,
              cached_input_tokens: 2,
            },
            finalResponse: JSON.stringify({
              output: 'Synthetic report',
              proposal: null,
              assessment: null,
              lesson:
                role === 'reviewer'
                  ? {
                      lesson: 'Observed',
                      evidence: 'Fixture',
                      nextTest: 'Recheck',
                    }
                  : null,
              modelAudit: { actualModel: 'invented-by-model' },
            }),
          };
        },
      };
    },
  });
  const result = await CodexTeamWorker.run(input, factory, source);
  assert.deepEqual(result.modelAudit, CodexModelPolicy.select(role, {}));
  assert.equal(result.modelAudit.actualModel, null);
  assert.equal(result.modelAudit.fallback, null);
  assert.deepEqual(result.usage, {
    inputTokens: 12,
    outputTokens: 3,
    cachedInputTokens: 2,
  });
}
assert.equal(calls, 8);
const overridden = CodexModelPolicy.select('news', {
  QUANTARA_CODEX_ROLE_POLICY:
    '{"news":{"model":"gpt-6.1-sol","effort":"medium"}}',
});
assert.equal(overridden.selectionSource, 'role-override');
assert.equal(overridden.requestedModel, 'gpt-6.1-sol');
assert.deepEqual(
  CodexModelPolicy.select('news', {
    QUANTARA_CODEX_PINNED_MODEL: JSON.stringify(overridden),
  }),
  overridden,
);
for (const invalid of [
  '',
  'null',
  '[]',
  '{',
  '{"unknown":{"model":"gpt-6.1-sol","effort":"low"}}',
  '{"news":{"model":"not-supported","effort":"low"}}',
  '{"reviewer":{"model":"gpt-6-luna","effort":"low"}}',
  '{"quant":{"model":"gpt-6.1-sol","effort":"ultra"}}',
  '{"news":{"model":"gpt-6-luna","effort":"low","extra":true}}',
]) {
  assert.throws(
    () =>
      CodexModelPolicy.select('news', { QUANTARA_CODEX_ROLE_POLICY: invalid }),
    /Invalid model policy/,
  );
}
for (const change of [
  { actualModel: 'self-reported' },
  { fallback: 'silent' },
  { provider: 'other' },
  { selectionSource: 'unknown' },
  { extra: true },
]) {
  assert.throws(
    () =>
      CodexModelPolicy.select('news', {
        QUANTARA_CODEX_PINNED_MODEL: JSON.stringify({
          ...overridden,
          ...change,
        }),
      }),
    /Invalid model policy/,
  );
}
const input = {
  role: 'news',
  objective: 'Synthetic failure',
  previous: [],
  portfolio: null,
};
await assert.rejects(
  CodexTeamWorker.run(
    input,
    () => {
      throw Error('Must not start');
    },
    {
      ...source,
      QUANTARA_CODEX_ROLE_POLICY:
        '{"news":{"model":"not-supported","effort":"low"}}',
    },
  ),
  /Invalid model policy/,
);
let failedCalls = 0;
const fail = (error) => () => ({
  startThread: () => ({
    run: async () => {
      failedCalls++;
      throw error;
    },
  }),
});
for (const [error, code] of [
  [new Error('model unavailable'), 'worker_runtime_failed'],
  [new Error('429 quota reached'), 'worker_rate_limited'],
  [
    Object.assign(new Error('cancelled'), { name: 'AbortError' }),
    'worker_timed_out',
  ],
]) {
  await assert.rejects(
    CodexTeamWorker.run(input, fail(error), source),
    (caught) => {
      assert.equal(WorkerFailure.from(caught).code, code);
      assert.equal(caught.modelAudit.requestedModel, 'gpt-6-luna');
      assert.equal(caught.modelAudit.fallback, null);
      assert.equal(caught.usage, null);
      return true;
    },
  );
}
assert.equal(
  failedCalls,
  3,
  'One application turn per failure, no model fallback/retry.',
);
await assert.rejects(
  CodexTeamWorker.run(
    input,
    () => ({
      startThread: () => ({
        run: async () => ({
          items: [],
          usage: { input_tokens: 20, output_tokens: 4, cached_input_tokens: 1 },
          finalResponse: '{malformed',
        }),
      }),
    }),
    source,
  ),
  (error) => {
    assert.equal(error.modelAudit.actualModel, null);
    assert.equal(error.usage.outputTokens, 4);
    assert.equal(WorkerFailure.from(error).code, 'worker_response_invalid');
    return true;
  },
);
for (const usage of [
  { input_tokens: -1, output_tokens: 0, cached_input_tokens: 0 },
  { input_tokens: 1, output_tokens: 0, cached_input_tokens: 2 },
  { input_tokens: 1, output_tokens: NaN, cached_input_tokens: 0 },
])
  assert.throws(() => CodexModelPolicy.usage({ usage }), /invalid usage/);
assert.equal(CodexModelPolicy.usage({ usage: null }), null);
console.log(
  'Model policy: 8 roles, pinned/overridden profiles, unsupported inputs rejected before spawn, truthful unknown model, safe usage, failures and no application retry/fallback passed using mocks only.',
);
