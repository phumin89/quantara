import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as promises from 'node:fs/promises';
import { syncBuiltinESMExports } from 'node:module';
import { tmpdir } from 'node:os';
import { basename, dirname, resolve, sep } from 'node:path';

const workerUrl = new URL(
  '../services/ai-worker/CodexTeamWorker.mjs',
  import.meta.url,
);
const { CodexTeamWorker } = await import(workerUrl.href);
const { WorkerFailure } =
  await import('../services/ai-worker/WorkerFailure.mjs');
const originalRm = fs.promises.rm;
const temporaryRoot = resolve(tmpdir());
const physicalRoot = await promises.realpath(temporaryRoot);
const created = new Set();
const captured = new Set();
const observations = [];
let failCleanup = false;
let fakeCalls = 0;

async function verifyTarget(target) {
  const absolute = resolve(target);
  assert.equal(dirname(absolute), temporaryRoot);
  assert(basename(absolute).startsWith('quantara-codex-'));
  assert(
    created.has(absolute),
    'Only directories created for this probe are allowed.',
  );
  const physical = await promises.realpath(absolute);
  assert(
    physical.startsWith(physicalRoot + sep),
    'The directory stays inside the actual temporary root.',
  );
  return absolute;
}

const mockRm = async (target, options) => {
  const absolute = await verifyTarget(target);
  captured.add(absolute);
  assert.deepEqual(options, { recursive: true, force: true });
  if (failCleanup)
    throw new Error('Synthetic cleanup blocked secret=do-not-leak');
  await originalRm(absolute, options);
};

try {
  fs.promises.rm = mockRm;
  syncBuiltinESMExports();
  assert.equal(
    promises.rm,
    mockRm,
    'The loaded ESM rm binding uses the process-local mock.',
  );
  for (const cleanupFailure of [false, true]) {
    for (const malformed of [false, true]) {
      failCleanup = cleanupFailure;
      let kind = 'result';
      let value;
      try {
        value = await CodexTeamWorker.run(
          {
            role: 'news',
            objective: 'Synthetic cleanup audit',
            previous: [],
            portfolio: null,
          },
          (options) => {
            const target = resolve(options.env.CODEX_HOME);
            created.add(target);
            return {
              startThread(threadOptions) {
                assert.equal(resolve(threadOptions.workingDirectory), target);
                return {
                  async run() {
                    fakeCalls++;
                    return {
                      items: [],
                      usage: {
                        input_tokens: 10,
                        output_tokens: 2,
                        cached_input_tokens: 1,
                      },
                      finalResponse: malformed
                        ? '{malformed'
                        : JSON.stringify({
                            output: 'Synthetic report',
                            proposal: null,
                            lesson: null,
                            researchSymbols: [],
                            assessment: null,
                          }),
                    };
                  },
                };
              },
            };
          },
          { CODEX_API_KEY: 'synthetic-cleanup-probe-not-a-credential' },
        );
      } catch (error) {
        kind = 'error';
        value = error;
      }
      observations.push({
        cleanupFailure,
        malformed,
        kind,
        code: kind === 'error' ? WorkerFailure.from(value).code : null,
        message: kind === 'error' ? value.message : null,
        hasUsage: Object.hasOwn(value, 'usage'),
        usage: value.usage ?? null,
        hasModelAudit: Object.hasOwn(value, 'modelAudit'),
        modelAudit: value.modelAudit ?? null,
        hasResponseDiagnostic: Object.hasOwn(value, 'responseDiagnostic'),
        responseDiagnostic: value.responseDiagnostic ?? null,
      });
    }
  }
} finally {
  fs.promises.rm = originalRm;
  syncBuiltinESMExports();
  assert.equal(
    promises.rm,
    originalRm,
    'Restore the builtin before removing leftover probe directories.',
  );
  for (const target of captured) {
    assert(created.has(target));
    try {
      await promises.access(target);
    } catch (error) {
      if (error.code === 'ENOENT') continue;
      throw error;
    }
    const verified = await verifyTarget(target);
    await originalRm(verified, { recursive: true, force: true });
  }
}

assert.equal(fakeCalls, 4);
assert.equal(created.size, 4);
assert.equal(captured.size, 4);
for (const target of created)
  await assert.rejects(promises.access(target), { code: 'ENOENT' });
const expectedUsage = {
  inputTokens: 10,
  outputTokens: 2,
  cachedInputTokens: 1,
};
assert.equal(observations[0].kind, 'result');
assert.deepEqual(observations[0].usage, expectedUsage);
assert.equal(observations[1].code, 'worker_response_invalid');
assert.deepEqual(observations[1].usage, expectedUsage);
assert.equal(observations[1].responseDiagnostic.stage, 'response_json');
for (const observation of observations.filter((item) => item.cleanupFailure)) {
  assert.deepEqual(
    observation.usage,
    expectedUsage,
    'Cleanup failure must retain already-known usage.',
  );
  assert.deepEqual(
    observation.modelAudit,
    observations[0].modelAudit,
    'Cleanup failure must retain worker-owned model audit.',
  );
  if (observation.malformed)
    assert.equal(
      observation.responseDiagnostic?.stage,
      'response_json',
      'Cleanup failure must not erase the original decode diagnostic.',
    );
  assert(
    !observation.message.includes('do-not-leak'),
    'Cleanup errors do not expose private filesystem details.',
  );
}
assert.equal(
  observations[2].kind,
  'error',
  'Cleanup failure must not report a successful worker result.',
);
assert.equal(observations[2].code, 'worker_runtime_failed');
assert.equal(
  observations[3].code,
  'worker_response_invalid',
  'Cleanup preserves the original decode failure.',
);
console.log(
  'Worker cleanup: known usage/model audit and primary diagnostics survive cleanup failures; four fake SDK calls, builtin restored and owned temporary directories removed.',
);
