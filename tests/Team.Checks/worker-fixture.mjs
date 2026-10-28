let text = '';
for await (const chunk of process.stdin) text += chunk;
if (text.length > 100000) process.exit(3);
const input = JSON.parse(text);
if (process.env.BITKUB_API_KEY || process.env.BITKUB_API_SECRET)
  process.exit(2);
if (input.objective === 'wait for cancellation') {
  setInterval(() => {}, 1000);
} else if (
  ['mismatched audit success', 'mismatched audit failure'].includes(
    input.objective,
  )
) {
  const expected = JSON.parse(process.env.QUANTARA_CODEX_PINNED_MODEL);
  const modelAudit = {
    ...expected,
    requestedModel: 'gpt-6.1-sol',
    reasoningEffort: 'medium',
    selectionSource: 'role-override',
  };
  const usage = { inputTokens: 20, outputTokens: 4, cachedInputTokens: 1 };
  if (input.objective === 'mismatched audit failure') {
    process.stderr.write(
      JSON.stringify({
        error: { code: 'worker_network_failed' },
        modelAudit,
        usage,
      }),
    );
    process.exitCode = 1;
  } else
    process.stdout.write(
      JSON.stringify({
        output: 'Synthetic report',
        lesson: null,
        modelAudit,
        usage,
      }),
    );
} else if (input.objective === 'utf8 report') {
  process.stdout.write(
    JSON.stringify({ output: 'รายงานภาษาไทย ฿100', lesson: null }),
  );
} else if (input.objective === 'missing lesson') {
  process.stdout.write(JSON.stringify({ output: 'report', lesson: null }));
} else if (input.objective === 'utf8 transport size') {
  process.stdout.write(
    JSON.stringify({
      output: 'UTF8 size accepted',
      lesson: {
        lesson: 'Observed input',
        evidence: 'Unicode preserved',
        nextTest: 'Keep bounded input',
      },
    }),
  );
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
} else if (input.objective === 'classified failure') {
  process.stderr.write(
    JSON.stringify({
      error: {
        code: 'worker_response_invalid',
        message: 'sensitive response details must not cross the boundary',
      },
    }),
  );
  process.exitCode = 1;
} else if (input.objective === 'audit metadata failure cases') {
  const expected = JSON.parse(process.env.QUANTARA_CODEX_PINNED_MODEL);
  const usage = { inputTokens: 20, outputTokens: 4, cachedInputTokens: 1 };
  let modelAudit = expected;
  let responseUsage = usage;
  if (input.portfolio === 'malformed-usage') {
    responseUsage = {
      inputTokens: 'secret',
      outputTokens: 4,
      cachedInputTokens: 1,
    };
  } else if (input.portfolio === 'cache-over-input') {
    responseUsage = { inputTokens: 1, outputTokens: 4, cachedInputTokens: 2 };
  } else if (input.portfolio === 'audit-string') {
    modelAudit = 'secret-audit';
  } else if (input.portfolio === 'audit-array') {
    modelAudit = ['secret-audit'];
  } else if (input.portfolio === 'audit-unknown-fields') {
    modelAudit = { ...expected, secretField: 'secret' };
  } else if (input.portfolio === 'unknown-code') {
    responseUsage = { ...usage, inputTokens: 'secret' };
    process.stderr.write(
      JSON.stringify({
        error: { code: 'evil_injection', message: 'secret' },
        modelAudit,
        usage: responseUsage,
      }),
    );
    process.exitCode = 1;
  }
  if (input.portfolio !== 'unknown-code') {
    process.stderr.write(
      JSON.stringify({
        error: { code: 'worker_response_invalid', message: 'secret' },
        modelAudit,
        usage: responseUsage,
      }),
    );
    process.exitCode = 1;
  }
} else if (input.objective === 'unknown failure code') {
  process.stderr.write(
    JSON.stringify({
      error: { code: 'evil_injection', message: 'secret=do-not-leak' },
    }),
  );
  process.exitCode = 1;
} else {
  process.stdout.write(
    JSON.stringify({
      output: `${input.role}|${input.previous[0]?.output ?? 'none'}|${input.portfolio}`,
      lesson: null,
    }),
  );
}
