let text = '';
for await (const chunk of process.stdin) text += chunk;
const input = JSON.parse(text);
if (process.env.BITKUB_API_KEY || process.env.BITKUB_API_SECRET)
  process.exit(2);
if (input.objective === 'wait for cancellation') {
  setInterval(() => {}, 1000);
} else if (input.objective === 'utf8 report') {
  process.stdout.write(
    JSON.stringify({ output: 'รายงานภาษาไทย ฿100', lesson: null }),
  );
} else if (input.objective === 'missing lesson') {
  process.stdout.write(JSON.stringify({ output: 'report', lesson: null }));
} else if (input.objective === 'typed lesson') {
  process.stdout.write(
    JSON.stringify({
      output: 'report',
      lesson: {
        lesson: 'Keep baseline',
        evidence: 'Observed prices',
        nextTest: 'Fixed-hour comparison',
      },
    }),
  );
} else if (input.objective === 'fail safely') {
  process.stderr.write('sensitive fixture stderr must not surface');
  process.exitCode = 1;
} else {
  process.stdout.write(
    JSON.stringify({
      output: `${input.role}|${input.previous[0]?.output ?? 'none'}|${input.portfolio}`,
      lesson: null,
    }),
  );
}
