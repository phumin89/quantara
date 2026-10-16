import type { TradingOverview } from '../contracts/TradingOverview';
import type { TradingRules } from '../contracts/TradingRules';

export interface ITradingService {
  get(signal?: AbortSignal): Promise<TradingOverview>;
  configure(
    rules: TradingRules,
    signal?: AbortSignal,
  ): Promise<TradingOverview>;
  setMode(
    mode: 'paper' | 'manualReview' | 'stopped',
    signal?: AbortSignal,
  ): Promise<TradingOverview>;
  activate(
    accountKey: string,
    rulesHash: string,
    expiresAt: string,
    signal?: AbortSignal,
  ): Promise<TradingOverview>;
  disable(signal?: AbortSignal): Promise<TradingOverview>;
  emergencyStop(signal?: AbortSignal): Promise<TradingOverview>;
}
