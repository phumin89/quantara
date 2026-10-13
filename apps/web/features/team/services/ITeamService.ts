import type { TeamOverview } from '../contracts/TeamOverview';
import type { TeamRun } from '../contracts/TeamRun';
import type { DailyBrief } from '../contracts/DailyBrief';
import type { CompanyLoopOverview } from '../contracts/CompanyLoopOverview';

export interface ITeamService {
  get(signal?: AbortSignal): Promise<TeamOverview>;
  brief(date: string, signal?: AbortSignal): Promise<DailyBrief>;
  setCompanyEnabled(
    enabled: boolean,
    signal?: AbortSignal,
  ): Promise<CompanyLoopOverview>;
  start(objective: string, signal?: AbortSignal): Promise<TeamRun>;
  stop(id: string, signal?: AbortSignal): Promise<TeamRun>;
}
