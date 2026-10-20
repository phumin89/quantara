import type { PortfolioResponse } from '../../../crypto/contracts/PortfolioResponse';
import type { TradingOverview } from '../../../trading/contracts/TradingOverview';
import type { DailyBrief } from '../../../team/contracts/DailyBrief';

export interface DashboardScreenState {
  readonly date: string;
  readonly portfolio: PortfolioResponse | null;
  readonly trading: TradingOverview | null;
  readonly brief: DailyBrief | null;
  readonly loading: boolean;
  readonly briefLoading: boolean;
  readonly portfolioError: string | null;
  readonly tradingError: string | null;
  readonly briefError: string | null;
  readonly checkedAt: number | null;
}
