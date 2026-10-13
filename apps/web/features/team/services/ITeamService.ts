import type { TeamOverview } from '../contracts/TeamOverview';
import type { TeamRun } from '../contracts/TeamRun';
import type { DailyBrief } from '../contracts/DailyBrief';

export interface ITeamService {
  get(signal?: AbortSignal): Promise<TeamOverview>;
  brief(date: string, signal?: AbortSignal): Promise<DailyBrief>;
  start(objective: string, signal?: AbortSignal): Promise<TeamRun>;
  stop(id: string, signal?: AbortSignal): Promise<TeamRun>;
}
