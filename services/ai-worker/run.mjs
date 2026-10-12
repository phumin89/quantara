import { CodexTeamWorker } from './CodexTeamWorker.mjs';

try {
  let input = '';
  process.stdin.setEncoding('utf8');
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 100000) throw new Error('Input is too large.');
  }
  const result = await CodexTeamWorker.run(JSON.parse(input));
  process.stdout.write(JSON.stringify(result));
} catch {
  // Raw SDK errors may contain prompts or authentication data.
  process.stderr.write(
    'The Codex worker failed. Check server authentication and runtime.\n',
  );
  process.exitCode = 1;
}
