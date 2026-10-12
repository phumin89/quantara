import type { PortfolioResponse } from '../contracts/PortfolioResponse';

export interface IPortfolioService {
  get(signal?: AbortSignal): Promise<PortfolioResponse>;

  sync(signal?: AbortSignal): Promise<void>;
}
