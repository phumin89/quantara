import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import type { IOrdersService } from '../../../features/orders/services/IOrdersService';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';

export class OrdersServices implements IOrdersService {
  public constructor(
    private readonly container: IServiceContainer<ServiceRegistry>,
  ) {}

  public list(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Orders).list(signal);
  }

  public create(
    proposal: Parameters<IOrdersService['create']>[0],
    signal?: AbortSignal,
  ) {
    return this.container.resolve(ServiceKey.Orders).create(proposal, signal);
  }

  public approve(id: string, signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Orders).approve(id, signal);
  }

  public reject(id: string, signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Orders).reject(id, signal);
  }

  public reconcile(id: string, signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Orders).reconcile(id, signal);
  }
}
