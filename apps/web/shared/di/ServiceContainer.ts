import type { IServiceContainer } from './IServiceContainer';
import type { ServiceFactories } from './ServiceFactories';

export class ServiceContainer<
  TServices extends object,
> implements IServiceContainer<TServices> {
  private readonly instances: Partial<TServices> = {};

  private readonly resolving = new Set<keyof TServices>();

  public constructor(private readonly factories: ServiceFactories<TServices>) {}

  public resolve<TKey extends keyof TServices>(key: TKey): TServices[TKey] {
    if (!Object.hasOwn(this.factories, key)) {
      throw new Error(`Service ${String(key)} is not registered.`);
    }

    const cached = this.instances[key];
    if (Object.hasOwn(this.instances, key) && cached !== undefined) {
      return cached;
    }

    if (this.resolving.has(key)) {
      throw new Error(`Circular service dependency: ${String(key)}.`);
    }

    this.resolving.add(key);
    try {
      const instance = this.factories[key](this);
      if (instance === undefined) {
        throw new Error(`Service ${String(key)} returned no instance.`);
      }

      Object.defineProperty(this.instances, key, { value: instance });

      return instance;
    } finally {
      this.resolving.delete(key);
    }
  }
}
