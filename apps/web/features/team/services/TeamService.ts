import type { IRequestService } from '../../../shared/services/IRequestService';
import { teamOverviewSchema } from '../contracts/TeamOverview';
import { teamRunSchema, type TeamRun } from '../contracts/TeamRun';
import type { TeamOverview } from '../contracts/TeamOverview';
import type { ITeamService } from './ITeamService';
import { dailyBriefSchema, type DailyBrief } from '../contracts/DailyBrief';
import {
  companyLoopOverviewSchema,
  type CompanyLoopOverview,
} from '../contracts/CompanyLoopOverview';

export class TeamService implements ITeamService {
  private static readonly basePath: string = '/team';

  public constructor(private readonly requests: IRequestService) {}

  public async get(signal?: AbortSignal): Promise<TeamOverview> {
    const body = await this.requests.get(
      TeamService.basePath,
      'Unable to load AI team.',
      signal,
    );
    const parsed = teamOverviewSchema.safeParse(body);
    if (!parsed.success) {
      throw new Error('AI team response is invalid.');
    }

    return parsed.data;
  }

  public async brief(date: string, signal?: AbortSignal): Promise<DailyBrief> {
    const body = await this.requests.get(
      `${TeamService.basePath}/brief?date=${encodeURIComponent(date)}`,
      'Unable to load the daily brief.',
      signal,
    );
    const parsed = dailyBriefSchema.safeParse(body);
    if (!parsed.success || parsed.data.date !== date) {
      throw new Error('Daily brief response is invalid.');
    }

    return parsed.data;
  }

  public async setCompanyEnabled(
    enabled: boolean,
    signal?: AbortSignal,
  ): Promise<CompanyLoopOverview> {
    const body = await this.requests.send(
      `${TeamService.basePath}/company`,
      { enabled },
      'Unable to update company operation.',
      signal,
    );
    const parsed = companyLoopOverviewSchema.safeParse(body);
    if (!parsed.success) {
      throw new Error('Company response is invalid.');
    }

    return parsed.data;
  }

  public async start(
    objective: string,
    signal?: AbortSignal,
  ): Promise<TeamRun> {
    return this.send('/runs', { objective }, signal);
  }

  public async stop(id: string, signal?: AbortSignal): Promise<TeamRun> {
    return this.send(`/stop?id=${encodeURIComponent(id)}`, {}, signal);
  }

  private async send(
    path: string,
    body: unknown,
    signal?: AbortSignal,
  ): Promise<TeamRun> {
    const response = await this.requests.send(
      `${TeamService.basePath}${path}`,
      body,
      'Unable to update AI team.',
      signal,
    );
    const parsed = teamRunSchema.safeParse(response);
    if (!parsed.success) {
      throw new Error('AI team run response is invalid.');
    }

    return parsed.data;
  }
}
