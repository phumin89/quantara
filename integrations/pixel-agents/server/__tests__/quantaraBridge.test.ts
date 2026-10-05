import { describe, expect, it } from 'vitest';

import { AgentStateStore } from '../src/agentStateStore.js';
import { QuantaraBridge, stableAgentNumber, statusForPixel } from '../src/quantaraBridge.js';

describe('Quantara bridge mapping', () => {
  it('maps stable ids deterministically into positive Pixel ids', () => {
    expect(stableAgentNumber('risk-engine')).toBe(stableAgentNumber('risk-engine'));
    expect(stableAgentNumber('risk-engine')).toBeGreaterThan(0);
    expect(stableAgentNumber('risk-engine')).not.toBe(stableAgentNumber('news-agent'));
  });
  it.each([
    ['Running analysis', 'active'], ['Meeting started', 'active'],
    ['Completed', 'waiting'], ['Failed', 'waiting'],
    [undefined, 'waiting'], ['configured', 'waiting'],
  ])('maps %s to %s', (input, expected) => expect(statusForPixel(input)).toBe(expected));

  it('seeds eight configured waiting agents idempotently', () => {
    const store = new AgentStateStore();
    const bridge = new QuantaraBridge(store, 'http://unused');
    const seed = (bridge as unknown as { seedConfiguredRoster: () => void }).seedConfiguredRoster.bind(bridge);
    seed();
    expect(store.size).toBe(8);
    expect([...store.values()].every(agent => agent.isWaiting)).toBe(true);
    const treasuryId = stableAgentNumber('treasury');
    const treasury = store.get(treasuryId)!;
    treasury.isWaiting = false;
    seed();
    expect(store.size).toBe(8);
    expect(store.get(treasuryId)).toBe(treasury);
    expect(store.get(treasuryId)!.isWaiting).toBe(false);
  });

  it('updates waiting state and broadcasts active to completed transitions', () => {
    const store = new AgentStateStore();
    const broadcasts: Record<string, unknown>[] = [];
    store.on('broadcast', message => broadcasts.push(message));
    const bridge = new QuantaraBridge(store, 'http://unused');
    bridge.handle({ type: 'TradingEvent', payload: { agentId: 'risk', role: 'Chief Risk Officer', status: 'Running analysis', activity: 'review' } });
    const id = stableAgentNumber('risk');
    expect(store.get(id)!.isWaiting).toBe(false);
    expect(broadcasts.at(-3)).toMatchObject({ type: 'agentStatus', id, status: 'active' });
    bridge.handle({ type: 'TradingEvent', payload: { agentId: 'risk', status: 'Completed' } });
    expect(store.get(id)!.isWaiting).toBe(true);
    expect(broadcasts.at(-2)).toMatchObject({ type: 'agentStatus', id, status: 'waiting' });
  });
  it('merges meeting stages into existing staff and ignores Live events', () => {
    const store = new AgentStateStore();
    const bridge = new QuantaraBridge(store, 'http://unused');
    const seed = (bridge as unknown as { seedConfiguredRoster: () => void }).seedConfiguredRoster.bind(bridge);
    seed();
    for (const agentId of ['market-research', 'security-analysis', 'quant-research', 'portfolio-initial', 'risk-critic', 'portfolio-final']) bridge.handle({ payload: { agentId, status: 'working' } });
    expect(store.size).toBe(8);
    const ceo = store.get(stableAgentNumber('ceo'))!;
    bridge.handle({ accountContext: { mode: 1 }, payload: { agentId: 'ceo', status: 'working' } });
    expect(ceo.isWaiting).toBe(true);
    bridge.handle({ payload: { agentId: 'unconfigured', status: 'working' } });
    expect(store.size).toBe(8);
  });
});
