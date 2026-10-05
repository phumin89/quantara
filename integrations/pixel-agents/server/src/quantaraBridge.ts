import {
  type HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

import type { AgentStateStore } from './agentStateStore.js';
import type { AgentState } from './types.js';

type QuantaraPayload = {
  agentId?: string; role?: string; status?: string; activity?: string;
  location?: string; summary?: string; presentation?: { characterId?: string };
};
type TradingEvent = { type?: string; payload?: QuantaraPayload; accountContext?: { mode?: number } };
const CONFIGURED_ROSTER = [
  ['treasury', 'Treasury'], ['ceo', 'CEO'], ['chief-of-staff', 'Chief of Staff'],
  ['portfolio-manager', 'Portfolio Manager'], ['news', 'Market Research'],
  ['technical', 'Equity & Crypto Analyst'], ['quant', 'Quant Research'],
  ['risk', 'Chief Risk Officer'],
] as const;

export function stableAgentNumber(agentId: string): number {
  let hash = 2166136261;
  for (const char of agentId) hash = Math.imul(hash ^ char.charCodeAt(0), 16777619);
  return (hash >>> 0) % 2000000000 + 1;
}

export function statusForPixel(status: string | undefined): string {
  const value = (status ?? '').toLowerCase();
  if (!value || value.includes('configur')) return 'waiting';
  if (value.includes('fail') || value.includes('error')) return 'waiting';
  if (value.includes('complete') || value.includes('idle') || value.includes('done')) return 'waiting';
  return 'active';
}

function makeAgent(id: number, payload: QuantaraPayload): AgentState {
  return {
    id, sessionId: `quantara:${payload.agentId ?? id}`, isExternal: true,
    projectDir: 'quantara', jsonlFile: '', fileOffset: 0, lineBuffer: '',
    activeToolIds: new Set(), activeToolStatuses: new Map(), activeToolNames: new Map(),
    activeSubagentToolIds: new Map(), activeSubagentToolNames: new Map(),
    backgroundAgentToolIds: new Set(), isWaiting: true, permissionSent: false,
    hadToolsInTurn: false, lastDataAt: Date.now(), linesProcessed: 0,
    seenUnknownRecordTypes: new Set(), hookDelivered: true, hooksOnly: true,
    providerId: 'quantara', agentName: payload.role ?? payload.agentId,
    contextTokens: 0, maxContextTokens: 0,
  };
}

export class QuantaraBridge {
  private connection: HubConnection | undefined;
  private readonly ids = new Map<string, number>();
  private retryTimer: ReturnType<typeof setTimeout> | undefined;
  private stopped = false;
  constructor(private readonly store: AgentStateStore, private readonly url: string) {}

  private seedConfiguredRoster(): void {
    for (const [agentId, role] of CONFIGURED_ROSTER) {
      const id = this.ids.get(agentId) ?? stableAgentNumber(agentId);
      this.ids.set(agentId, id);
      if (this.store.has(id)) continue;
      const agent = makeAgent(id, { agentId, role, status: 'configured' });
      this.store.set(id, agent);
      this.store.broadcast({ type: 'agentStatus', id, status: 'waiting' });
    }
  }

  async start(): Promise<void> {
    this.stopped = false;
    this.seedConfiguredRoster();
    this.connection = new HubConnectionBuilder().withUrl(this.url).configureLogging(LogLevel.Warning)
      .withAutomaticReconnect([0, 2000, 5000, 10000]).build();
    this.connection.on('TradingEvent', (event: TradingEvent) => this.handle(event));
    this.connection.onclose(() => this.scheduleReconnect());
    await this.connect();
  }

  private async connect(): Promise<void> {
    if (this.stopped || !this.connection || this.connection.state !== HubConnectionState.Disconnected) return;
    try { await this.connection.start(); }
    catch (error) {
      console.error('[Pixel Agents] Quantara bridge unavailable:', error);
      this.scheduleReconnect();
    }
  }

  private scheduleReconnect(): void {
    if (this.stopped || this.retryTimer) return;
    this.retryTimer = setTimeout(() => {
      this.retryTimer = undefined;
      void this.connect();
    }, 5000);
  }

  handle(event: TradingEvent): void {
    if (event.accountContext && event.accountContext.mode !== 0) return;
    const payload = event.payload;
    if (!payload?.agentId) return;
    const aliases: Record<string, string> = { 'market-research': 'news', 'security-analysis': 'technical', 'quant-research': 'quant', 'portfolio-initial': 'portfolio-manager', 'portfolio-final': 'portfolio-manager', 'risk-critic': 'risk' };
    const agentId = aliases[payload.agentId] ?? payload.agentId;
    if (!CONFIGURED_ROSTER.some(([id]) => id === agentId)) return;
    const id = this.ids.get(agentId) ?? stableAgentNumber(agentId);
    this.ids.set(agentId, id);
    const agent = this.store.get(id) ?? makeAgent(id, { ...payload, agentId });
    agent.lastDataAt = Date.now();
    agent.agentName = payload.role ?? agent.agentName;
    const status = statusForPixel(payload.status);
    agent.isWaiting = status === 'waiting';
    this.store.set(id, agent);
    this.store.broadcast({ type: 'agentStatus', id, status });
    this.store.broadcast({ type: 'agentToolsClear', id });
    if (status === 'active' && (payload.activity || payload.summary)) {
      const toolId = `quantara:${event.type ?? 'event'}:${id}`;
      const activity = payload.activity ?? payload.summary ?? 'Quantara task';
      this.store.broadcast({ type: 'agentToolStart', id, toolId, status: activity, toolName: activity });
    }
  }

  async stop(): Promise<void> {
    this.stopped = true;
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = undefined;
    if (this.connection) await this.connection.stop();
    this.connection = undefined;
  }
}
