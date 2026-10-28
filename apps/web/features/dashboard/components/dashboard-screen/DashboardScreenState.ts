import type { TradingOverview } from '../../../trading/contracts/TradingOverview';
import type { RiskOverview } from '../../../crypto/contracts/RiskOverview';

export interface DashboardScreenState {
  readonly date: string;
  readonly trading: TradingOverview | null;
  readonly risk: RiskOverview | null;
  readonly loading: boolean;
  readonly tradingError: string | null;
  readonly riskError: string | null;
  readonly checkedAt: number | null;
}
