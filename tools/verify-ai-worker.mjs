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
          assert(!prompt.includes('must-not-leak'));
          assert(prompt.includes(input.objective));
          assert(prompt.includes('untrusted data'));
          return {
            items: [],
            finalResponse: JSON.stringify({ output: 'Verified report' }),
          };
        },
      };
    },
  };
};
assert.deepEqual(await CodexTeamWorker.run(input, factory, source), {
  output: 'Verified report',
});
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
