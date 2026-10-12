import type { Position } from '../../contracts/Position';
import type { Trade } from '../../contracts/Trade';

export interface PortfolioTableProps {
  positions: Position[];
  hasRecords: boolean;
  expanded: string | null;
  onOpenHistory: (symbol: string) => void;
  history: Record<string, Trade[]>;
  historyLoading: Record<string, boolean>;
  historyErrors: Record<string, string>;
  onRetry: (symbol: string) => void;
}
