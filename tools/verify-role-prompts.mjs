import assert from 'node:assert/strict';
import { CodexTeamWorker } from '../services/ai-worker/CodexTeamWorker.mjs';
import { CodexOutputSchema } from '../services/ai-worker/CodexOutputSchema.mjs';

for (const role of Object.keys(CodexTeamWorker.roles)) {
  const previous =
    role === 'analyst'
      ? [{ role: 'ceo', output: 'Fixture priority' }]
      : role === 'reviewer'
        ? [{ role: 'news', output: 'Fixture event revision' }]
        : [];
  const prompt = CodexTeamWorker.prompt({
    role,
    objective: 'Fixture task',
    portfolio: '{}',
    previous,
  });
  assert(
    prompt.length <= 12500,
    `${role}: prompt exceeds focused instruction budget (${prompt.length}).`,
  );
  assert.match(prompt, /supplied data/i);
  assert.match(prompt, /untrusted/i);
  assert.match(prompt, /specialistMemory/);
  assert.match(prompt, /not model retraining/);
  assert.match(prompt, /Only the exact value manualReview/);
  assert(!prompt.includes('researchSymbols (empty except for CEO'));
  assert(!prompt.includes('reason (max 500'));
  const maximum = CodexOutputSchema.create(role, false).properties
    .researchSymbols.maxItems;
  assert(
    prompt.includes(`researchSymbols limit: ${maximum}`),
    `${role}: prompt and schema permissions disagree.`,
  );
  if (maximum === 0) {
    assert(prompt.includes('Return researchSymbols=[]'));
  }
  const documents = CodexTeamWorker.instructions(role);
  assert(
    documents.length <= 3600,
    `${role}: role contract exceeds instruction budget.`,
  );
  for (const heading of ['Goal', 'Evidence', 'Done', 'Handoff']) {
    assert.match(
      documents,
      new RegExp(heading, 'i'),
      `${role}: missing ${heading} contract.`,
    );
  }
  assert.match(documents, /correct/i);
  assert.match(documents, /incorrect/i);
  if (role === 'news') {
    assert(
      prompt.length <= 6500,
      'News must not receive the trading execution manual.',
    );
    for (const unrelated of [
      'ATR(20)',
      'entryPlan',
      'BUY amount',
      'fixedOrderThb',
    ]) {
      assert(
        !prompt.includes(unrelated),
        `News received unrelated instruction: ${unrelated}`,
      );
    }
    assert.match(prompt, /NoNewEvents/);
    assert.match(prompt, /Unavailable/);
  }
  if (role === 'quant') {
    assert(!prompt.includes('entryPlan'));
    assert(!prompt.includes('ATR(20)'));
  }
}

const proposalFixture = {
  symbol: 'BTC',
  side: 'buy',
  amount: '100',
  limitPrice: '2000000',
  reason: 'Fixture',
  evidenceAt: '2026-10-20T00:00:00Z',
};
for (const role of ['intraday', 'swing', 'news', 'positions', 'quant']) {
  assert.equal(CodexTeamWorker.validProposal(role, proposalFixture), false);
  assert.equal(
    CodexTeamWorker.validLesson(role, {
      lesson: 'x',
      evidence: 'y',
      nextTest: 'z',
    }),
    false,
  );
}
console.log(
  'Role prompts: focused budgets, role contracts, isolated duties and schema-aligned permissions passed. No model or broker call.',
);
