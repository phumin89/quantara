import {
  portfolioSchema,
  type PortfolioResponse,
} from '../contracts/PortfolioResponse';
import type { IRequestService } from '../../../shared/services/IRequestService';

export class PortfolioService {
  private static readonly basePath: string = '/crypto';

  public constructor(private readonly requests: IRequestService) {}

  public async get(signal?: AbortSignal): Promise<PortfolioResponse> {
    const body = await this.requests.get(
      `${PortfolioService.basePath}/portfolio`,
      'Unable to load portfolio.',
      signal,
    );
    const parsed = portfolioSchema.safeParse(body);
    if (!parsed.success) {
      throw new Error('Portfolio response is invalid.');
    }

    return parsed.data;
  }

  public async sync(signal?: AbortSignal): Promise<void> {
    await this.requests.post(
      `${PortfolioService.basePath}/sync`,
      'Unable to sync Bitkub data.',
      signal,
    );
  }
}
