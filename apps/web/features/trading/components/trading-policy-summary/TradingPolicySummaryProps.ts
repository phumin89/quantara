import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingPolicySummaryProps {
  readonly overview: TradingOverview;
  readonly showRiskLimits?: boolean;
}
