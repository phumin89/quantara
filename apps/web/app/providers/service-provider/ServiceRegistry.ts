import type { IHistoryService } from '../../../features/crypto/services/IHistoryService';
import type { IPortfolioService } from '../../../features/crypto/services/IPortfolioService';
import type { IRequestService } from '../../../shared/services/IRequestService';
import type { ITeamService } from '../../../features/team/services/ITeamService';
import { ServiceKey } from './ServiceKey';
import type { IOrdersService } from '../../../features/orders/services/IOrdersService';

export interface ServiceRegistry {
  readonly [ServiceKey.Request]: IRequestService;
  readonly [ServiceKey.Portfolio]: IPortfolioService;
  readonly [ServiceKey.History]: IHistoryService;
  readonly [ServiceKey.Team]: ITeamService;
  readonly [ServiceKey.Orders]: IOrdersService;
}
