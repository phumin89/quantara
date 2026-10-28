import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';
import type { ITradingService } from '../../../features/trading/services/ITradingService';

export class TradingServices implements ITradingService {
  public constructor(
    private readonly container: IServiceContainer<ServiceRegistry>,
  ) {}

  public previewManualSale(
    ...args: Parameters<ITradingService['previewManualSale']>
  ) {
    return this.container
      .resolve(ServiceKey.Trading)
      .previewManualSale(...args);
  }

  public applyManualSale(
    ...args: Parameters<ITradingService['applyManualSale']>
  ) {
    return this.container.resolve(ServiceKey.Trading).applyManualSale(...args);
  }

  public previewCashBaseline(
    ...args: Parameters<ITradingService['previewCashBaseline']>
  ) {
    return this.container
      .resolve(ServiceKey.Trading)
      .previewCashBaseline(...args);
  }

  public applyCashBaseline(
    ...args: Parameters<ITradingService['applyCashBaseline']>
  ) {
    return this.container
      .resolve(ServiceKey.Trading)
      .applyCashBaseline(...args);
  }

  public pendingReconciliation(
    ...args: Parameters<ITradingService['pendingReconciliation']>
  ) {
    return this.container
      .resolve(ServiceKey.Trading)
      .pendingReconciliation(...args);
  }

  public retainReconciliation(
    ...args: Parameters<ITradingService['retainReconciliation']>
  ) {
    this.container.resolve(ServiceKey.Trading).retainReconciliation(...args);
  }

  public clearReconciliation(
    ...args: Parameters<ITradingService['clearReconciliation']>
  ) {
    this.container.resolve(ServiceKey.Trading).clearReconciliation(...args);
  }

  public get(signal?: AbortSignal) {
    return this.container.resolve(ServiceKey.Trading).get(signal);
  }

  public prepare(...args: Parameters<ITradingService['prepare']>) {
    return this.container.resolve(ServiceKey.Trading).prepare(...args);
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
