import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import type { ITeamService } from '../../../features/team/services/ITeamService';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';

export class TeamServices implements ITeamService {
  public constructor(
    private readonly container: IServiceContainer<ServiceRegistry>,
  ) {}

  public get(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Team).get(signal);
  }

  public brief(date: string, signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Team).brief(date, signal);
  }

  public setCompanyEnabled(enabled: boolean, signal?: AbortSignal) {
    return this.container
      .resolve(ServiceKey.Team)
      .setCompanyEnabled(enabled, signal);
  }

  public start(objective: string, signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Team).start(objective, signal);
  }

  public stop(id: string, signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Team).stop(id, signal);
  }

  public news(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Team).news(signal);
  }
}
