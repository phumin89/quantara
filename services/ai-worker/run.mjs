import { CodexTeamWorker } from './CodexTeamWorker.mjs';
import { WorkerFailure } from './WorkerFailure.mjs';

try {
  let input = '';
  process.stdin.setEncoding('utf8');
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 100000) throw new Error('Input is too large.');
  }
  let request;
  try {
    request = JSON.parse(input);
  } catch {
    throw new Error('Invalid worker input.');
  }
  const result = await CodexTeamWorker.run(request);
  process.stdout.write(JSON.stringify(result));
} catch (error) {
  process.stderr.write(
    `${JSON.stringify({ error: WorkerFailure.from(error) })}\n`,
  );
  process.exitCode = 1;
}
