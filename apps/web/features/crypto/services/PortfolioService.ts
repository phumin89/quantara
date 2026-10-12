import {
  portfolioSchema,
  type PortfolioResponse,
} from '../contracts/PortfolioResponse';
import { RequestService } from '../../../shared/services/RequestService';

export class PortfolioService {
  public constructor(private readonly requests: RequestService) {}

  public async get(signal?: AbortSignal): Promise<PortfolioResponse> {
    const body = await this.requests.get(
      '/api/crypto/portfolio',
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
      '/api/crypto/sync',
      'Unable to sync Bitkub data.',
      signal,
    );
  }
}
