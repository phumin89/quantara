import assert from 'node:assert/strict';
import { CodexTeamWorker } from '../services/ai-worker/CodexTeamWorker.mjs';
import { WorkerFailure } from '../services/ai-worker/WorkerFailure.mjs';
import { WorkerResponseDiagnostics } from '../services/ai-worker/WorkerResponseDiagnostics.mjs';

const valid = {
  output: 'Synthetic report',
  proposal: null,
  lesson: {
    lesson: 'Known limit',
    evidence: 'Synthetic evidence',
    nextTest: 'Check context',
  },
  researchSymbols: [],
  assessment: { schemaVersion: 'research-assessment-v1' },
};
const result = CodexTeamWorker.decodeResponse(
  JSON.stringify(valid),
  'reviewer',
  true,
);
assert.equal(result.output, valid.output);
assert(!Object.hasOwn(result, 'responseDiagnostic'));
// The C# validator still owns semantic assessment validation; this fixture checks only the Node parser boundary.
const cases = [
  ['{"secret":"do-not-leak"', 'response_json'],
  ['null', 'response_shape'],
  ['[]', 'response_shape'],
  [JSON.stringify({ ...valid, command: 'do-not-leak' }), 'response_shape'],
  [JSON.stringify({ ...valid, output: '' }), 'report_content'],
  [JSON.stringify({ ...valid, output: ' \t\n' }), 'report_content'],
  [
    JSON.stringify({ ...valid, proposal: { secret: 'do-not-leak' } }),
    'proposal_content',
  ],
  [JSON.stringify({ ...valid, lesson: null }), 'lesson_content'],
  [
    JSON.stringify({
      ...valid,
      lesson: { ...valid.lesson, lesson: ' padded ' },
    }),
    'lesson_content',
  ],
  [
    JSON.stringify({ ...valid, researchSymbols: ['WLD'] }),
    'research_selection',
  ],
  [JSON.stringify({ ...valid, assessment: null }), 'assessment_shape'],
  [JSON.stringify({ ...valid, assessment: [] }), 'assessment_shape'],
];
for (const [response, stage] of cases) {
  assert.throws(
    () => CodexTeamWorker.decodeResponse(response, 'reviewer', true),
    (error) => {
      const diagnostic = error.responseDiagnostic;
      assert.deepEqual(diagnostic, {
        version: 1,
        stage,
        responseCharacters: response.length,
      });
      assert(
        !JSON.stringify({
          error: WorkerFailure.from(error),
          responseDiagnostic: diagnostic,
        }).includes('do-not-leak'),
      );
      assert.deepEqual(Object.keys(diagnostic).sort(), [
        'responseCharacters',
        'stage',
        'version',
      ]);
      return true;
    },
  );
}
const legacy = CodexTeamWorker.decodeResponse(
  '{"output":"Legacy","lesson":null}',
  'analyst',
);
assert.equal(legacy.output, 'Legacy');
assert.equal(legacy.proposal, null);
assert.equal(WorkerResponseDiagnostics.create('do-not-leak', 10), null);
for (const count of [-1, 100001, NaN, Infinity, 'do-not-leak']) {
  assert.equal(
    WorkerResponseDiagnostics.create('response_json', count).responseCharacters,
    null,
  );
}
console.log(
  'Worker response diagnostics: 12 invalid parser fixtures plus legacy, allowlist, bounds and redaction checks passed; no AI, process or network calls.',
);
