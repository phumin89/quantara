import type { IServiceContainer } from '../../../shared/di/IServiceContainer';
import type { IRiskService } from '../../../features/crypto/services/IRiskService';
import { ServiceKey } from './ServiceKey';
import type { ServiceRegistry } from './ServiceRegistry';

export class RiskServices implements IRiskService {
  public constructor(
    private readonly container: IServiceContainer<ServiceRegistry>,
  ) {}

  public get(...args: Parameters<IRiskService['get']>) {
    return this.container.resolve(ServiceKey.Risk).get(...args);
  }

  public refresh(...args: Parameters<IRiskService['refresh']>) {
    return this.container.resolve(ServiceKey.Risk).refresh(...args);
  }

  public save(...args: Parameters<IRiskService['save']>) {
    return this.container.resolve(ServiceKey.Risk).save(...args);
  }
}
