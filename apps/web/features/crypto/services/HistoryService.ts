import {
  historyResponseSchema,
  type HistoryResponse,
} from '../contracts/HistoryResponse';
import type { IRequestService } from '../../../shared/services/IRequestService';

export class HistoryService {
  public constructor(private readonly requests: IRequestService) {}

  public async get(
    symbol: string,
    signal?: AbortSignal,
  ): Promise<HistoryResponse> {
    const body = await this.requests.get(
      `/api/crypto/history?coin=${encodeURIComponent(symbol)}`,
      'Unable to load history.',
      signal,
    );
    const parsed = historyResponseSchema.safeParse(body);
    if (!parsed.success) {
      throw new Error('History response is invalid.');
    }

    return parsed.data;
  }
}
