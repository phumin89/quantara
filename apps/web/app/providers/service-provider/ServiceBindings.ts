import { HistoryService } from '../../../features/crypto/services/HistoryService';
import { PortfolioService } from '../../../features/crypto/services/PortfolioService';
import { RequestService } from '../../../shared/services/RequestService';
import { TeamService } from '../../../features/team/services/TeamService';
import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import type { ServiceFactories } from '../../../shared/di/ServiceFactories';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';
import { OrdersService } from '../../../features/orders/services/OrdersService';
import { TradingService } from '../../../features/trading/services/TradingService';

export class ServiceBindings {
  public static readonly factories: ServiceFactories<ServiceRegistry> = {
    [ServiceKey.Request]: ServiceBindings.createRequest,
    [ServiceKey.Portfolio]: ServiceBindings.createPortfolio,
    [ServiceKey.History]: ServiceBindings.createHistory,
    [ServiceKey.Team]: ServiceBindings.createTeam,
    [ServiceKey.Orders]: ServiceBindings.createOrders,
    [ServiceKey.Trading]: ServiceBindings.createTrading,
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

  private static createTeam(
    this: void,
    container: IServiceContainer<ServiceRegistry>,
  ): TeamService {
    return new TeamService(container.resolve(ServiceKey.Request));
  }

  private static createOrders(
    this: void,
    container: IServiceContainer<ServiceRegistry>,
  ): OrdersService {
    return new OrdersService(container.resolve(ServiceKey.Request));
  }

  private static createTrading(
    this: void,
    container: IServiceContainer<ServiceRegistry>,
  ): TradingService {
    return new TradingService(container.resolve(ServiceKey.Request));
  }
}
