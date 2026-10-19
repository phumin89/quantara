import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingReportsViewProps {
  readonly overview: TradingOverview;
  readonly date: string;
}
