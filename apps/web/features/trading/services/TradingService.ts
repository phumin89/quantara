import type { IRequestService } from '../../../shared/services/IRequestService';
import {
  tradingOverviewSchema,
  type TradingOverview,
} from '../contracts/TradingOverview';
import {
  tradingRulesSchema,
  type TradingRules,
} from '../contracts/TradingRules';
import type { ITradingService } from './ITradingService';

export class TradingService implements ITradingService {
  public constructor(private readonly requests: IRequestService) {}

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
  ) {
    return this.write(
      '/activate',
      {
        accountKey,
        rulesHash,
        expiresAt,
        acknowledgement: 'enable-auto-trading',
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
      throw new Error('Trading status response is invalid.');
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
        'Unable to update trading controls.',
        signal,
      ),
    );
    if (!parsed.success) {
      throw new Error('Trading status response is invalid.');
    }

    return parsed.data;
  }
}
