import type { Position } from '../../contracts/Position';
import type { Trade } from '../../contracts/Trade';
import type { PortfolioFilter } from '../../contracts/PortfolioFilter';

export interface PortfolioTableProps {
  positions: Position[];
  hasRecords: boolean;
  readonly holdingsKnown: boolean;
  readonly filter?: PortfolioFilter;
  expanded: string | null;
  onOpenHistory: (symbol: string) => void;
  history: Record<string, Trade[]>;
  historyLoading: Record<string, boolean>;
  historyErrors: Record<string, string>;
  onRetry: (symbol: string) => void;
}
