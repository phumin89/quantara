import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';
import type { ITradingService } from '../../../features/trading/services/ITradingService';

export class TradingServices implements ITradingService {
  public constructor(
    private readonly container: IServiceContainer<ServiceRegistry>,
  ) {}

  public get(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Trading).get(signal);
  }

  public configure(...args: Parameters<ITradingService['configure']>) {
    return this.container.resolve(ServiceKey.Trading).configure(...args);
  }

  public setMode(...args: Parameters<ITradingService['setMode']>) {
    return this.container.resolve(ServiceKey.Trading).setMode(...args);
  }

  public activate(...args: Parameters<ITradingService['activate']>) {
    return this.container.resolve(ServiceKey.Trading).activate(...args);
  }

  public disable(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Trading).disable(signal);
  }

  public emergencyStop(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Trading).emergencyStop(signal);
  }
}
