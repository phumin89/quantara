import type { IHistoryService } from '../services/IHistoryService';
import type { IPortfolioService } from '../services/IPortfolioService';

export interface ICryptoServices {
  readonly history: IHistoryService;
  readonly portfolio: IPortfolioService;
}
