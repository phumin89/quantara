import type { Trade } from '../../contracts/Trade';

export interface TradeHistoryProps {
  coinSymbol: string;
  expanded: boolean;
  loading: boolean;
  error: string | undefined;
  trades: Trade[] | undefined;
  onRetry: () => void;
}
