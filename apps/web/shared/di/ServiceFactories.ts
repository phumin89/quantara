import type { IServiceContainer } from './IServiceContainer';

export type ServiceFactories<TServices extends object> = {
  readonly [TKey in keyof TServices]: (
    container: IServiceContainer<TServices>,
  ) => TServices[TKey];
};
