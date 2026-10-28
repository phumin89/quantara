import type { ITradingService } from '../../trading/services/ITradingService';
import type { IRiskService } from '../../crypto/services/IRiskService';

export interface IDashboardServices {
  readonly trading: ITradingService;
  readonly risk: IRiskService;
}
