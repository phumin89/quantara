import {
  historyResponseSchema,
  type HistoryResponse,
} from '../contracts/HistoryResponse';
import type { IRequestService } from '../../../shared/services/IRequestService';
import type { IHistoryService } from './IHistoryService';

export class HistoryService implements IHistoryService {
  private static readonly basePath: string = '/crypto/history';

  public constructor(private readonly requests: IRequestService) {}

  public async get(
    symbol: string,
    signal?: AbortSignal,
  ): Promise<HistoryResponse> {
    const body = await this.requests.get(
      `${HistoryService.basePath}?coin=${encodeURIComponent(symbol)}`,
      'Unable to load history.',
      signal,
    );
    const parsed = historyResponseSchema.safeParse(body);
    if (
      !parsed.success ||
      parsed.data.trades.some(
        (trade) => trade.symbol.toUpperCase() !== symbol.toUpperCase(),
      )
    ) {
      throw new Error('History response is invalid.');
    }

    return parsed.data;
  }
}
