import type { ReactNode } from 'react';
import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';
import type { ITeamService } from '../../../features/team/services/ITeamService';
import type { IOrdersService } from '../../../features/orders/services/IOrdersService';
import type { ITradingService } from '../../../features/trading/services/ITradingService';

export interface ServiceProviderProps {
  readonly children: ReactNode;
  readonly services?: ICryptoServices;
  readonly teamService?: ITeamService;
  readonly ordersService?: IOrdersService;
  readonly tradingService?: ITradingService;
}
