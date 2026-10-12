import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';
import type { IHistoryService } from '../../../features/crypto/services/IHistoryService';
import type { IPortfolioService } from '../../../features/crypto/services/IPortfolioService';
import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';

export class CryptoServices implements ICryptoServices {
  public constructor(
    private readonly container: IServiceContainer<ServiceRegistry>,
  ) {}

  public get portfolio(): IPortfolioService {
    return this.container.resolve(ServiceKey.Portfolio);
  }

  public get history(): IHistoryService {
    return this.container.resolve(ServiceKey.History);
  }
}
