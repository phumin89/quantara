import type { IPortfolioService } from '../../crypto/services/IPortfolioService';
import type { ITradingService } from '../../trading/services/ITradingService';

export interface IDashboardServices {
  readonly portfolio: IPortfolioService;
  readonly trading: ITradingService;
}
