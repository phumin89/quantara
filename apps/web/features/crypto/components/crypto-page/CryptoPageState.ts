import type { PortfolioResponse } from '../../contracts/PortfolioResponse';
import type { PortfolioFilter } from '../../contracts/PortfolioFilter';
import type { Trade } from '../../contracts/Trade';

export interface CryptoPageState {
  readonly data: PortfolioResponse | null;
  readonly error: string | null;
  readonly refreshing: boolean;
  readonly filter: PortfolioFilter;
  readonly expanded: string | null;
  readonly history: Record<string, Trade[]>;
  readonly historyLoading: Record<string, boolean>;
  readonly historyErrors: Record<string, string>;
}
