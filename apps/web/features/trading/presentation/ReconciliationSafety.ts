import type { TradingOverview } from '../contracts/TradingOverview';
import type { ReconciliationPreview } from '../contracts/ReconciliationPreview';

export class ReconciliationSafety {
  public static sameAmount(first: string, second: string): boolean {
    return this.canonical(first) === this.canonical(second);
  }

  private static canonical(value: string): string {
    const [whole = '0', fractional = ''] = value.split('.');
    const integer = whole.replace(/^0+(?=\d)/, '');
    const fraction = fractional.replace(/0+$/, '');

    return fraction ? `${integer}.${fraction}` : integer;
  }

  public static stopped(overview: TradingOverview): boolean {
    return (
      overview.mode === 'stopped' &&
      overview.mandate !== null &&
      overview.mandate.revokedAt !== null &&
      !this.unresolvedOrders(overview)
    );
  }

  public static unresolvedOrders(overview: TradingOverview): boolean {
    return overview.live.orders.some((order) => this.unresolved(order));
  }

  private static unresolved(
    order: TradingOverview['live']['orders'][number],
  ): boolean {
    return (
      ['pending', 'submitting', 'submitted', 'partial', 'unknown'].includes(
        order.status,
      ) || ['claimed', 'unknown'].includes(order.cancellationState ?? '')
    );
  }

  public static material(overview: TradingOverview): string {
    return JSON.stringify({
      account: overview.accountKey,
      rules: overview.rulesHash,
      mode: overview.mode,
      emergencyStopped: overview.emergencyStopped,
      mandate: overview.mandate,
      cash: overview.live.cashThb,
      positions: overview.live.positions
        .filter((position) => Number(position.quantity) > 0)
        .map((position) => ({
          symbol: position.symbol,
          quantity: position.quantity,
          costThb: position.costThb,
          openedAt: position.openedAt,
        })),
      orders: overview.live.orders.filter((order) => this.unresolved(order)),
    });
  }

  public static binding(
    overview: TradingOverview,
    symbol?: string,
  ): ReconciliationPreview['binding'] {
    const dailyLimit = overview.rules.adaptive
      ? `${String(overview.rules.adaptive.dailyLossFraction * 100)}% of verified equity`
      : `${String(overview.rules.dailyLossThb)} THB`;

    return {
      accountKey: overview.accountKey,
      rulesHash: overview.rulesHash,
      material: this.material(overview),
      previousCashThb: overview.live.cashThb,
      riskSummary: `Rules ${overview.rules.version}; daily loss limit ${dailyLimit}; maximum ${String(overview.rules.maxPositions)} positions. Known loss records and all existing limits stay unchanged.`,
      originalLot:
        overview.live.positions.find(
          (position) => position.symbol === symbol,
        ) ?? null,
    };
  }

  public static current(
    preview: ReconciliationPreview,
    overview: TradingOverview,
    uncertain: boolean,
  ): boolean {
    // Only the same account may retry an exact saved proof. The server returns
    // retained results before checking current eligibility; new writes still
    // require its original binding, freshness and stopped-state checks.
    return (
      preview.binding.accountKey === overview.accountKey &&
      (uncertain ||
        (this.stopped(overview) &&
          preview.binding.rulesHash === overview.rulesHash &&
          preview.binding.material === this.material(overview)))
    );
  }

  public static usable(preview: ReconciliationPreview, now: number): boolean {
    const observed = Date.parse(preview.result.brokerObservedAt);

    return (
      Number.isFinite(now) &&
      observed <= now + 30000 &&
      Date.parse(preview.result.expiresAt) > now
    );
  }
}
