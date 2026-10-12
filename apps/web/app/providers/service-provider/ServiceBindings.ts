import { HistoryService } from '../../../features/crypto/services/HistoryService';
import { PortfolioService } from '../../../features/crypto/services/PortfolioService';
import { RequestService } from '../../../shared/services/RequestService';
import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import type { ServiceFactories } from '../../../shared/di/ServiceFactories';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';

export class ServiceBindings {
  public static readonly factories: ServiceFactories<ServiceRegistry> = {
    [ServiceKey.Request]: ServiceBindings.createRequest,
    [ServiceKey.Portfolio]: ServiceBindings.createPortfolio,
    [ServiceKey.History]: ServiceBindings.createHistory,
  };

  private static createRequest(this: void): RequestService {
    return new RequestService();
  }

  private static createPortfolio(
    this: void,
    container: IServiceContainer<ServiceRegistry>,
  ): PortfolioService {
    return new PortfolioService(container.resolve(ServiceKey.Request));
  }

  private static createHistory(
    this: void,
    container: IServiceContainer<ServiceRegistry>,
  ): HistoryService {
    return new HistoryService(container.resolve(ServiceKey.Request));
  }
}
