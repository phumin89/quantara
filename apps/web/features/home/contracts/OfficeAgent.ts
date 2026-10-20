import type { TeamRole } from '../../team/contracts/TeamRole';
import type { OfficeAgentStatus } from './OfficeAgentStatus';

export interface OfficeAgent {
  readonly id: number;
  readonly role: TeamRole;
  readonly label: string;
  readonly status: OfficeAgentStatus;
  readonly updatedAt: string | null;
  readonly runId: string | null;
  readonly date: string | null;
  readonly summary: string | null;
}
