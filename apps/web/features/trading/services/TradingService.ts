import type { IRequestService } from '../../../shared/services/IRequestService';
import { JsonResponse } from '../../../shared/api/JsonResponse';
import {
  tradingOverviewSchema,
  type TradingOverview,
} from '../contracts/TradingOverview';
import {
  tradingRulesSchema,
  type TradingRules,
} from '../contracts/TradingRules';
import type { ITradingService } from './ITradingService';
import { manualSaleReviewSchema } from '../contracts/ManualSaleReview';
import { cashBaselineReviewSchema } from '../contracts/CashBaselineReview';
import type { ReconciliationPreview } from '../contracts/ReconciliationPreview';
import { ReconciliationProofStore } from './ReconciliationProofStore';

export class TradingService implements ITradingService {
  public constructor(private readonly requests: IRequestService) {}

  public pendingReconciliation(account: string): ReconciliationPreview | null {
    return ReconciliationProofStore.read(account);
  }

  public retainReconciliation(preview: ReconciliationPreview): void {
    ReconciliationProofStore.save(preview);
  }

  public clearReconciliation(account: string, proof?: string): void {
    ReconciliationProofStore.clear(account, proof);
  }

  public async previewManualSale(
    symbol: string,
    executionId: string,
    signal?: AbortSignal,
  ) {
    return manualSaleReviewSchema.parse(
      await this.requests.send(
        '/trading/reconciliation/manual-sale/preview',
        { symbol, executionId },
        'Unable to preview the manual sale.',
        signal,
      ),
    );
  }

  public async applyManualSale(
    proof: string,
    acknowledgement: string,
    signal?: AbortSignal,
  ) {
    return manualSaleReviewSchema.parse(
      await this.requests.send(
        '/trading/reconciliation/manual-sale/apply',
        { proof, acknowledgement },
        'Confirmation outcome is unknown. Do not submit a different confirmation.',
        signal,
      ),
    );
  }

  public async previewCashBaseline(signal?: AbortSignal) {
    return cashBaselineReviewSchema.parse(
      await this.requests.send(
        '/trading/reconciliation/manual-sale/cash-baseline/preview',
        {},
        'Unable to preview current broker cash.',
        signal,
      ),
    );
  }

  public async applyCashBaseline(
    proof: string,
    acknowledgement: string,
    signal?: AbortSignal,
  ) {
    return cashBaselineReviewSchema.parse(
      await this.requests.send(
        '/trading/reconciliation/manual-sale/cash-baseline/apply',
        { proof, acknowledgement },
        'Confirmation outcome is unknown. Do not submit a different confirmation.',
        signal,
      ),
    );
  }

  public async get(signal?: AbortSignal) {
    return this.read('', signal);
  }

  public async prepare(
    accountKey: string,
    rulesHash: string,
    signal?: AbortSignal,
  ) {
    return this.write('/prepare', { accountKey, rulesHash }, signal);
  }

  public async configure(rules: TradingRules, signal?: AbortSignal) {
    return this.write(
      '/config',
      { rules: tradingRulesSchema.parse(rules) },
      signal,
    );
  }

  public async setMode(
    mode: 'paper' | 'manualReview' | 'stopped',
    signal?: AbortSignal,
  ) {
    return this.write('/mode', { mode }, signal);
  }

  public async activate(
    accountKey: string,
    rulesHash: string,
    expiresAt: string,
    signal?: AbortSignal,
    aiManaged = false,
  ) {
    return this.write(
      '/activate',
      {
        accountKey,
        rulesHash,
        expiresAt,
        acknowledgement: aiManaged
          ? 'enable-ai-managed-trading'
          : 'enable-auto-trading',
        ...(aiManaged ? { aiManaged: true } : {}),
      },
      signal,
    );
  }

  public async disable(signal?: AbortSignal) {
    return this.write('/disable', {}, signal);
  }

  public async emergencyStop(signal?: AbortSignal) {
    return this.write('/emergency-stop', {}, signal);
  }

  private async read(
    path: string,
    signal?: AbortSignal,
  ): Promise<TradingOverview> {
    const parsed = tradingOverviewSchema.safeParse(
      await this.requests.get(
        `/trading${path}`,
        'Unable to load trading status.',
        signal,
      ),
    );
    if (!parsed.success) {
      throw new Error('Trading status could not be verified. Refresh status.');
    }

    return parsed.data;
  }

  private async write(
    path: string,
    body: unknown,
    signal?: AbortSignal,
  ): Promise<TradingOverview> {
    const parsed = tradingOverviewSchema.safeParse(
      await this.requests.send(
        `/trading${path}`,
        body,
        JsonResponse.uncertainWrite,
        signal,
      ),
    );
    if (!parsed.success) {
      throw new Error(JsonResponse.uncertainWrite);
    }

    return parsed.data;
  }
}
