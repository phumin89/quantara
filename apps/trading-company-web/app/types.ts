export type AgentStatus = "configured" | "idle" | "working" | "meeting" | "completed" | "failed";
export type AgentArtifact = { kind: string; title: string; summary: string; facts: Record<string, string> };
export type AgentPresentation = { characterId: string; preferredWorkstation: string; displayRole: string };
export type WorkflowPayload = { agentId: string; role: string; status: AgentStatus; activity: string; location: string; summary: string; presentation?: AgentPresentation; artifact?: AgentArtifact };
export type WorkflowEvent = { type: string; correlationId: string; occurredAt: string; payload: WorkflowPayload; accountContext?: { mode: 0 | 1; accountId: string } };
export type OfficeAgent = { id: string; role: string; status: AgentStatus; activity: string; summary: string; artifacts: AgentArtifact[] };
