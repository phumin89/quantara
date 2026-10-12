import type { TeamOverview } from '../contracts/TeamOverview';
import type { TeamRun } from '../contracts/TeamRun';

export interface ITeamService {
  get(signal?: AbortSignal): Promise<TeamOverview>;
  start(objective: string, signal?: AbortSignal): Promise<TeamRun>;
  stop(id: string, signal?: AbortSignal): Promise<TeamRun>;
}
