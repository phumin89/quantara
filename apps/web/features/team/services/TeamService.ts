import type { IRequestService } from '../../../shared/services/IRequestService';
import { teamOverviewSchema } from '../contracts/TeamOverview';
import { teamRunSchema, type TeamRun } from '../contracts/TeamRun';
import type { TeamOverview } from '../contracts/TeamOverview';
import type { ITeamService } from './ITeamService';

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
