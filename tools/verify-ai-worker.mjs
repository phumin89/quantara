import assert from 'node:assert/strict';
import { access } from 'node:fs/promises';
import { CodexTeamWorker } from '../services/ai-worker/CodexTeamWorker.mjs';

const input = {
  role: 'ceo',
  objective: 'Review the portfolio',
  previous: [],
  portfolio: '{"positions":[]}',
};
for (const role of Object.keys(CodexTeamWorker.roles)) {
  const instructions = CodexTeamWorker.instructions(role);
  assert(instructions.includes('AGENTS.md for ' + role + ':'));
  assert(instructions.includes('SKILL.md for ' + role + ':'));
  assert.match(
    instructions,
    /---\r?\nname: quantara-[a-z]+\r?\ndescription: [^\r\n]+\r?\n---/,
  );
  const previousRoles =
    role === 'analyst'
      ? ['ceo']
      : role === 'reviewer'
        ? ['ceo', 'analyst']
        : [];
  const roleInput = {
    ...input,
    role,
    previous: previousRoles.map((previousRole) => ({
      role: previousRole,
      output: 'Earlier report',
    })),
  };
  const prompt = CodexTeamWorker.prompt(roleInput);
  assert.match(prompt, /Authority: no broker credentials/);
  assert(prompt.includes(instructions));
  assert(prompt.includes('Only the exact value manualReview'));
  assert(prompt.includes('every role must return proposal=null'));
  assert(prompt.includes('Caveman lite'));
  assert(prompt.includes('never claim you called tools yourself'));
  for (const other of Object.keys(CodexTeamWorker.roles).filter(
    (value) => value !== role,
  )) {
    assert(!prompt.includes('AGENTS.md for ' + other + ':'));
  }
}
assert.throws(
  () => CodexTeamWorker.instructions('../ceo'),
  /Invalid worker role/,
);
assert.throws(
  () => CodexTeamWorker.validate({ ...input, role: 'unknown' }),
  /Invalid worker input/,
);
assert.throws(
  () => CodexTeamWorker.validate({ ...input, role: 'reviewer' }),
  /Invalid role handoff/,
);
assert.throws(
  () => CodexTeamWorker.validate({ ...input, objective: ' ' }),
  /Invalid worker input/,
);
const validLesson = { lesson: 'A', evidence: 'B', nextTest: 'C' };
assert(CodexTeamWorker.validLesson('reviewer', validLesson));
assert(CodexTeamWorker.validLesson('ceo', null));
assert(CodexTeamWorker.validLesson('analyst', null));
for (const invalid of [
  { ...validLesson, lesson: ' ' },
  { ...validLesson, evidence: 'x'.repeat(501) },
  { ...validLesson, extra: 'x' },
])
  assert(!CodexTeamWorker.validLesson('reviewer', invalid));
assert(!CodexTeamWorker.validLesson('reviewer', null));
assert(!CodexTeamWorker.validLesson('ceo', validLesson));
await assert.rejects(
  CodexTeamWorker.run(
    input,
    () => {
      throw new Error('Must not spawn');
    },
    {},
  ),
  /authentication/,
);
let options;
let threadOptions;
let turnOptions;
let reviewerTurnOptions;
const source = {
  CODEX_API_KEY: 'test-key',
  BITKUB_API_KEY: 'must-not-leak',
  BITKUB_API_SECRET: 'must-not-leak',
  CODEX_HOME: 'real-home',
  PATH: 'test-path',
};
const factory = (value) => {
  options = value;
  return {
    startThread: (thread) => {
      threadOptions = thread;
      return {
        run: async (prompt, turn) => {
          turnOptions = turn;
          assert.deepEqual(turn.outputSchema.properties.lesson, {
            type: 'null',
          });
          assert.deepEqual(turn.outputSchema.properties.output, {
            type: 'string',
            minLength: 1,
            maxLength: 16000,
          });
          assert.deepEqual(turn.outputSchema.properties.proposal, {
            type: 'null',
          });
          assert.equal(
            turn.outputSchema.properties.researchSymbols.maxItems,
            3,
          );
          assert(!prompt.includes('must-not-leak'));
          assert(prompt.includes(input.objective));
          assert(prompt.includes('untrusted data'));
          return {
            items: [],
            finalResponse: JSON.stringify({
              output: 'Verified report',
              proposal: null,
              lesson: null,
            }),
          };
        },
      };
    },
  };
};
assert.deepEqual(await CodexTeamWorker.run(input, factory, source), {
  output: 'Verified report',
  proposal: null,
  lesson: null,
  usage: null,
});
const proposal = {
  symbol: 'BTC',
  side: 'buy',
  amount: '10',
  limitPrice: '100',
  reason: 'Evidence-backed review candidate',
  evidenceAt: '2026-10-13T00:00:00Z',
};
const proposalFactory = () => ({
  startThread: () => ({
    run: async (prompt, turn) => {
      reviewerTurnOptions = turn;
      return {
        items: [],
        finalResponse: JSON.stringify({
          output: 'Review',
          proposal,
          lesson: {
            lesson: 'Use observed spread',
            evidence: 'Two snapshots',
            nextTest: 'Recheck in one hour',
          },
        }),
      };
    },
  }),
});
const reviewerInput = {
  ...input,
  role: 'reviewer',
  previous: [
    { role: 'ceo', output: 'Earlier report' },
    { role: 'analyst', output: 'Earlier report' },
  ],
};
assert.deepEqual(
  await CodexTeamWorker.run(reviewerInput, proposalFactory, source),
  {
    output: 'Review',
    proposal,
    lesson: {
      lesson: 'Use observed spread',
      evidence: 'Two snapshots',
      nextTest: 'Recheck in one hour',
    },
    usage: null,
  },
);
assert.deepEqual(reviewerTurnOptions.outputSchema.properties.lesson, {
  type: 'object',
  properties: {
    lesson: { type: 'string', minLength: 1, maxLength: 500 },
    evidence: { type: 'string', minLength: 1, maxLength: 500 },
    nextTest: { type: 'string', minLength: 1, maxLength: 500 },
  },
  required: ['lesson', 'evidence', 'nextTest'],
  additionalProperties: false,
});
assert.equal(reviewerTurnOptions.outputSchema.properties.lesson.type, 'object');
assert.equal(
  reviewerTurnOptions.outputSchema.properties.researchSymbols.maxItems,
  0,
);
assert.equal(
  reviewerTurnOptions.outputSchema.properties.lesson.anyOf,
  undefined,
);
await assert.rejects(
  CodexTeamWorker.run(
    {
      ...input,
      role: 'analyst',
      previous: [{ role: 'ceo', output: 'Earlier report' }],
    },
    proposalFactory,
    source,
  ),
  /invalid pending proposal/,
);
assert.equal(
  reviewerTurnOptions.outputSchema.properties.researchSymbols.maxItems,
  0,
);
await assert.rejects(
  CodexTeamWorker.run(
    reviewerInput,
    () => ({
      startThread: () => ({
        run: async () => ({
          items: [],
          finalResponse: JSON.stringify({
            output: 'Review',
            proposal: { ...proposal, unknown: true },
          }),
        }),
      }),
    }),
    source,
  ),
  /invalid pending proposal/,
);
assert.equal(options.apiKey, 'test-key');
assert.equal(options.env.BITKUB_API_KEY, undefined);
assert.equal(options.env.BITKUB_API_SECRET, undefined);
assert.notEqual(options.env.CODEX_HOME, 'real-home');
assert.equal(options.config.features.shell_tool, false);
assert.equal(options.config.features.multi_agent, false);
assert.equal(threadOptions.sandboxMode, 'read-only');
assert.equal(threadOptions.networkAccessEnabled, false);
assert.equal(threadOptions.webSearchMode, 'disabled');
const chatSource = {
  ...source,
  QUANTARA_CODEX_AUTH_HOME: 'existing-auth-home',
  QUANTARA_CODEX_LAUNCHER: 'isolated-launcher',
};
assert.deepEqual(await CodexTeamWorker.run(input, factory, chatSource), {
  output: 'Verified report',
  proposal: null,
  lesson: null,
  usage: null,
});
assert.equal(
  options.apiKey,
  undefined,
  'ChatGPT mode must not silently use an API key',
);
assert.equal(options.env.CODEX_HOME, 'existing-auth-home');
assert.equal(options.codexPathOverride, 'isolated-launcher');
assert.equal(options.config.forced_login_method, 'chatgpt');
assert.equal(options.env.CODEX_API_KEY, undefined);
assert.equal(options.env.BITKUB_API_KEY, undefined);
await access(options.env.QUANTARA_CODEX_EXECUTABLE);
assert(turnOptions.signal instanceof AbortSignal);
await assert.rejects(access(threadOptions.workingDirectory));
const badFactory = () => ({
  startThread: () => ({
    run: async () => ({
      items: [{ type: 'command_execution' }],
      finalResponse: '{"output":"Unsafe"}',
    }),
  }),
});
await assert.rejects(
  CodexTeamWorker.run(input, badFactory, source),
  /unsupported tool/,
);
console.log(
  'Codex worker: input/handoff validation, isolated environment, tool restrictions, structured reports and cleanup passed. No real AI call.',
);
const entryPlan = {
  targetPrice: '105',
  invalidationPrice: '95',
  reviewAfterMinutes: 60,
};
assert(CodexTeamWorker.validProposal('reviewer', { ...proposal, entryPlan }));
for (const invalid of [
  { ...entryPlan, targetPrice: '0' },
  { ...entryPlan, invalidationPrice: 'private-value' },
  { ...entryPlan, reviewAfterMinutes: 0 },
  { ...entryPlan, reviewAfterMinutes: 1441 },
  { ...entryPlan, automaticExit: true },
])
  assert(
    !CodexTeamWorker.validProposal('reviewer', {
      ...proposal,
      entryPlan: invalid,
    }),
  );
assert(
  !CodexTeamWorker.validProposal('reviewer', {
    ...proposal,
    side: 'sell',
    entryPlan,
  }),
);
const selectionFactory =
  (researchSymbols, role = 'ceo') =>
  () => ({
    startThread: () => ({
      run: async () => ({
        items: [],
        finalResponse: JSON.stringify({
          output: 'Candidate selection',
          proposal: null,
          lesson: role === 'reviewer' ? validLesson : null,
          researchSymbols,
        }),
      }),
    }),
  });
assert.deepEqual(
  (await CodexTeamWorker.run(input, selectionFactory(['KUB', 'BTC']), source))
    .researchSymbols,
  ['KUB', 'BTC'],
);
for (const invalid of [
  ['KUB', 'KUB'],
  ['KUB_THB'],
  ['KUB', 'BTC', 'ETH', 'SOL'],
  ['https://private.test'],
  ['kub'],
]) {
  await assert.rejects(
    CodexTeamWorker.run(input, selectionFactory(invalid), source),
    /invalid response research selection/,
  );
}
await assert.rejects(
  CodexTeamWorker.run(
    reviewerInput,
    selectionFactory(['KUB'], 'reviewer'),
    source,
  ),
  /invalid response research selection/,
);
console.log(
  'Resting entry plan and read-only CEO candidate selection contracts passed.',
);

const assessment = {
  schemaVersion: 'research-assessment-v1',
  accountKey: 'a'.repeat(64),
  runId: '00000000-0000-4000-8000-000000000001',
  policyHash: null,
  asOf: '2026-10-19T01:00:00Z',
  expiresAt: '2026-10-19T02:00:00Z',
  evidenceIds: ['news:official-event@2'],
  positions: [],
  opportunities: [],
};
const assessmentInput = {
  ...reviewerInput,
  portfolio: JSON.stringify({ assessmentContext: assessment }),
};
const assessmentFactory = (value) => () => ({
  startThread: () => ({
    run: async (_, options) => {
      assert(
        !options.outputSchema.properties.assessment.anyOf.some(
          (item) => item.type === 'null',
        ),
      );
      assert(options.outputSchema.required.includes('assessment'));
      return {
        items: [],
        finalResponse: JSON.stringify({
          output: 'Research only',
          proposal: null,
          lesson: validLesson,
          researchSymbols: [],
          assessment: value,
        }),
      };
    },
  }),
});
assert.deepEqual(
  (
    await CodexTeamWorker.run(
      assessmentInput,
      assessmentFactory(assessment),
      source,
    )
  ).assessment,
  assessment,
  'Typed assessment must survive the Node process result.',
);
await assert.rejects(
  CodexTeamWorker.run(assessmentInput, assessmentFactory(null), source),
  /invalid response assessment/,
);
console.log(
  'Structured reviewer assessment survives transport; missing required assessment is rejected.',
);

assert.deepEqual(
  CodexTeamWorker.validate({
    ...input,
    role: 'intraday',
    previous: [],
  }),
  { ...input, role: 'intraday', previous: [] },
);
assert.deepEqual(
  CodexTeamWorker.validate({
    ...input,
    role: 'reviewer',
    previous: [{ role: 'intraday', output: 'Bars and spread' }],
  }).previous,
  [{ role: 'intraday', output: 'Bars and spread' }],
);
assert.throws(
  () =>
    CodexTeamWorker.validate({
      ...input,
      role: 'swing',
      previous: [{ role: 'reviewer', output: 'Forbidden' }],
    }),
  /Invalid role handoff/,
);
const usageFactory = () => ({
  startThread: () => ({
    run: async () => ({
      items: [],
      finalResponse: JSON.stringify({
        output: 'Intraday report',
        proposal: null,
        lesson: null,
        researchSymbols: ['BTC'],
      }),
      usage: { input_tokens: 11, output_tokens: 7, cached_input_tokens: 3 },
    }),
  }),
});
assert.deepEqual(
  await CodexTeamWorker.run(
    { ...input, role: 'intraday' },
    usageFactory,
    source,
  ),
  {
    output: 'Intraday report',
    proposal: null,
    lesson: null,
    usage: { inputTokens: 11, outputTokens: 7, cachedInputTokens: 3 },
    researchSymbols: ['BTC'],
  },
);
console.log('Specialist handoffs, permissions and SDK usage metadata passed.');
