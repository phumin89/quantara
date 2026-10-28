import assert from 'node:assert/strict';
import { WorkerFailure } from '../services/ai-worker/WorkerFailure.mjs';

const cases = [
  ['Input is too large.', 'worker_input_too_large'],
  ['Invalid worker input.', 'worker_input_invalid'],
  ['Configure Codex authentication on the server.', 'worker_auth_unavailable'],
  ['401 unauthorized', 'worker_authentication_failed'],
  ['429 quota exceeded', 'worker_rate_limited'],
  ["You've hit your usage limit", 'worker_rate_limited'],
  ['usage_limit_reached', 'worker_rate_limited'],
  ['connect ETIMEDOUT 203.0.113.1:443 secret=do-not-leak', 'worker_timed_out'],
  [
    'connect ENETUNREACH 203.0.113.1:443 secret=do-not-leak',
    'worker_network_failed',
  ],
  [
    'connect EHOSTUNREACH 203.0.113.1:443 secret=do-not-leak',
    'worker_network_failed',
  ],
  ['refresh_token_reused secret=do-not-leak', 'worker_authentication_failed'],
  ['stream disconnected before completion', 'worker_network_failed'],
  ['invalid JSON response secret=do-not-leak', 'worker_response_invalid'],
  ['unsupported schema', 'worker_response_invalid'],
  [
    'The worker returned an invalid pending proposal.',
    'worker_proposal_invalid',
  ],
  ['The worker returned an invalid learning lesson.', 'worker_lesson_invalid'],
  [
    'The worker returned an invalid response research selection.',
    'worker_selection_invalid',
  ],
  ['model refusal secret=do-not-leak', 'worker_response_rejected'],
  ['connect ECONNREFUSED', 'worker_network_failed'],
  ['fetch failed: ECONNREFUSED secret=do-not-leak', 'worker_network_failed'],
  ['connection refused', 'worker_network_failed'],
  ['connection-refused', 'worker_network_failed'],
  ['connection was refused', 'worker_network_failed'],
  ['model refused the request', 'worker_response_rejected'],
  ['fetch failed: 401 unauthorized', 'worker_authentication_failed'],
  ['fetch failed: ECONNRESET', 'worker_network_failed'],
  [
    Object.assign(new Error('cancelled'), { name: 'AbortError' }),
    'worker_timed_out',
  ],
  ['The worker returned an invalid report.', 'worker_response_invalid'],
  ['The worker attempted an unsupported tool.', 'worker_unsupported_tool'],
  ['totally unexpected secret=do-not-leak', 'worker_runtime_failed'],
];
for (const [error, code] of cases) {
  const safe = WorkerFailure.from(error);
  assert.equal(safe.code, code);
  assert(!JSON.stringify(safe).includes('do-not-leak'));
  assert.equal(Object.keys(safe).sort().join(','), 'code,message');
}
console.log('Worker failure classification and safe diagnostics passed.');
