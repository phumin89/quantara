import type { TradingOverview } from '../contracts/TradingOverview';
import type { TradingRules } from '../contracts/TradingRules';
import type { ManualSaleReview } from '../contracts/ManualSaleReview';
import type { CashBaselineReview } from '../contracts/CashBaselineReview';
import type { ReconciliationPreview } from '../contracts/ReconciliationPreview';

export interface ITradingService {
  pendingReconciliation(account: string): ReconciliationPreview | null;
  retainReconciliation(preview: ReconciliationPreview): void;
  clearReconciliation(account: string, proof?: string): void;
  previewManualSale(
    symbol: string,
    executionId: string,
    signal?: AbortSignal,
  ): Promise<ManualSaleReview>;
  applyManualSale(
    proof: string,
    acknowledgement: string,
    signal?: AbortSignal,
  ): Promise<ManualSaleReview>;
  previewCashBaseline(signal?: AbortSignal): Promise<CashBaselineReview>;
  applyCashBaseline(
    proof: string,
    acknowledgement: string,
    signal?: AbortSignal,
  ): Promise<CashBaselineReview>;
  get(signal?: AbortSignal): Promise<TradingOverview>;
  prepare(
    accountKey: string,
    rulesHash: string,
    signal?: AbortSignal,
  ): Promise<TradingOverview>;
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
    aiManaged?: boolean,
  ): Promise<TradingOverview>;
  disable(signal?: AbortSignal): Promise<TradingOverview>;
  emergencyStop(signal?: AbortSignal): Promise<TradingOverview>;
}
