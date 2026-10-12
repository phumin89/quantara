import type { Position } from '../contracts/Position';
import type { Trade } from '../contracts/Trade';

export interface PortfolioRowProps {
  item: Position;
  expanded: boolean;
  onOpenHistory: () => void;
  history: Trade[] | undefined;
  historyLoading: boolean;
  historyError: string | undefined;
  onRetry: () => void;
}
