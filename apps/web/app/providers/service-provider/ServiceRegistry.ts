import type { IHistoryService } from '../../../features/crypto/services/IHistoryService';
import type { IPortfolioService } from '../../../features/crypto/services/IPortfolioService';
import type { IRequestService } from '../../../shared/services/IRequestService';
import { ServiceKey } from './ServiceKey';

export interface ServiceRegistry {
  readonly [ServiceKey.Request]: IRequestService;
  readonly [ServiceKey.Portfolio]: IPortfolioService;
  readonly [ServiceKey.History]: IHistoryService;
}
