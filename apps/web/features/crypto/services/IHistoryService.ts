import type { HistoryResponse } from '../contracts/HistoryResponse';

export interface IHistoryService {
  get(symbol: string, signal?: AbortSignal): Promise<HistoryResponse>;
}
