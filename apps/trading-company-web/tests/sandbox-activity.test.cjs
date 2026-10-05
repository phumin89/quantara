const { test } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { initialActivity, receiveActivity, staffWithSavedReports } = require('./load-component.cjs')(path.join(__dirname, '../app/sandboxActivity.ts'));
const event = (id, status = 'completed') => ({ type: 'AgentCompletedTask', correlationId: 'fixture', occurredAt: '2026-10-01T02:00:00Z', payload: { agentId: id, role: id, status, activity: 'Fixture work', summary: 'Validated fixture summary' } });
test('eight meeting stages update the configured staff without creating duplicates', () => {
  let activity = initialActivity;
  for (const role of ['ceo', 'chief-of-staff', 'market-research', 'security-analysis', 'quant-research', 'portfolio-initial', 'risk-critic', 'portfolio-final']) activity = receiveActivity(activity, event(role));
  assert.equal(activity.agents.length, 8);
  assert.equal(activity.agents.filter(a => a.status === 'completed').length, 7);
  assert.equal(activity.events.length, 8);
  assert.equal(activity.agents.find(a => a.id === 'portfolio-manager').role, 'ผู้จัดการพอร์ต');
});
test('Live, malformed and out-of-scope events cannot mutate Sandbox activity', () => {
  for (const incoming of [{ ...event('ceo'), accountContext: { mode: 1 } }, { ...event('ceo'), occurredAt: 'bad' }, event('unknown'), event('ceo', 'bogus')]) assert.equal(receiveActivity(initialActivity, incoming), initialActivity);
});
test('retained activity stays bounded and is not overwritten by restored reports', () => {
  let activity = initialActivity;
  for (let n = 0; n < 150; n++) activity = receiveActivity(activity, event('ceo', 'working'));
  assert.equal(activity.events.length, 100);
  const saved = { status: 'COMPLETED', businessDate: '2026-10-01', summary: { reports: [{ role: 'ceo', summary: 'Old report' }, { role: 'portfolio-final', summary: 'Saved decision' }] } };
  const staff = staffWithSavedReports(activity.agents, saved);
  assert.equal(staff.find(a => a.id === 'ceo').status, 'working');
  assert.equal(staff.find(a => a.id === 'portfolio-manager').summary, 'Saved decision');
  assert.equal(activity.agents.find(a => a.id === 'portfolio-manager').status, 'configured');
  assert.equal(staffWithSavedReports(initialActivity.agents, { ...saved, status: 'INVALID_FINAL' }), initialActivity.agents);
});
