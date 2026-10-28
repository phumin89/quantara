import type { ICryptoServices } from '../../../crypto/context/ICryptoServices';
import type { PortfolioResponse } from '../../../crypto/contracts/PortfolioResponse';

export interface TradingPanelProps {
  readonly date: string;
  readonly portfolio?: PortfolioResponse | null;
  readonly history?: ICryptoServices['history'];
  readonly onBalancesChanged?: () => void;
}
