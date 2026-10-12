export interface IServiceContainer<TServices extends object> {
  resolve<TKey extends keyof TServices>(key: TKey): TServices[TKey];
}
