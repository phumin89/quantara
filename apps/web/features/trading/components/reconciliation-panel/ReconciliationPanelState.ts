import type { TradingOverview } from '../../contracts/TradingOverview';
import type { Trade } from '../../../crypto/contracts/Trade';
import type { ReconciliationPreview } from '../../contracts/ReconciliationPreview';

export interface ReconciliationPanelState {
  readonly overview: TradingOverview | null;
  readonly symbol: string;
  readonly executionId: string;
  readonly trades: Trade[];
  readonly preview: ReconciliationPreview | null;
  readonly acknowledged: boolean;
  readonly uncertain: boolean;
  readonly unreadableAccount: string | null;
  readonly busy: boolean;
  readonly now: number;
  readonly error: string | null;
  readonly notice: string | null;
}
