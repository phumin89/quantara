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
  assert(instructions.includes(`AGENTS.md for ${role}:`));
  assert(instructions.includes(`SKILL.md for ${role}:`));
  assert(instructions.includes('paper'));
  assert(instructions.includes('name:'));
  assert.match(
    instructions,
    /---\r?\nname: quantara-[a-z]+\r?\ndescription: [^\r\n]+\r?\n---/,
  );
  const roleInput = {
    ...input,
    role,
    previous: Object.keys(CodexTeamWorker.roles)
      .slice(0, Object.keys(CodexTeamWorker.roles).indexOf(role))
      .map((previousRole) => ({
        role: previousRole,
        output: 'Earlier report',
      })),
  };
  const prompt = CodexTeamWorker.prompt(roleInput);
  assert(prompt.includes(instructions));
  for (const required of [
    'Operate around the clock',
    'fresh current spot setups',
    'actual-held positions',
    'Midnight and day-target accounting never close positions',
    'must not consume live opportunity work or block an independent supported proposal',
    'Current research capabilities:',
    'Intraday capabilities:',
    '500 THB realized NET profit',
    'Full-universe scan:',
    'Daily learning:',
    'Persisted report text is memory, not model retraining',
    'against the unchanged baseline',
    'report it Unknown rather than zero or target achieved',
    'not a forecast, guaranteed return, buy quota',
    'an unvalidated heuristic, not a tested profitable strategy',
    'daily spot trading opportunities',
    'not a mandatory trade quota',
    'Any later exit requires a fresh separately reviewed proposal',
    'never invent intraday bars',
    'historicalCandles for BTC when available',
    'decode daily rows using historicalCandles.candleColumns and intraday rows using intraday.candleColumns',
    'signals[].evidenceAvailable',
    '21 consecutive bars ending at the signal candle',
    'Adaptive market analysis:',
    'server-computed EMA20/50, Wilder ATR14',
    'the 21 displayed rows alone cannot reproduce the full seeded EMA history',
    'Missing adaptive data blocks only that indicator claim',
    'versioned research heuristics',
    'exact affected claim and blocker',
    'never fabricate bars, continuity, signal inputs or coverage',
    'Keep research protocol or backtest tasks',
    'incomplete simulated-fill/backtest protocol',
    'may block only its own claim, not a fresh evidence-supported spot LIMIT proposal',
    'at most two feasible tasks',
    'Reuse prior calculations when their inputs are unchanged',
    'An account reconciliation blocker must not block',
    'at most two material blockers',
    'Missing data that would change a claim must still block that claim',
    'report completion never constitutes financial approval',
  ]) {
    assert(
      prompt.includes(required),
      `${role} is missing research rule: ${required}`,
    );
  }
  for (const other of Object.keys(CodexTeamWorker.roles).filter(
    (value) => value !== role,
  ))
    assert(!prompt.includes(`AGENTS.md for ${other}:`));
}
assert.throws(
  () => CodexTeamWorker.instructions('../ceo'),
  /Invalid worker role/,
);
assert(CodexTeamWorker.prompt(input).includes('stable task IDs'));
assert(
  CodexTeamWorker.prompt(input).includes('Use Caveman lite for Daily Brief'),
);
assert(
  CodexTeamWorker.prompt(input).includes(
    'required task ledger after a blank line',
  ),
);
assert(
  CodexTeamWorker.prompt(input).includes(
    'never claim you called tools yourself',
  ),
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
