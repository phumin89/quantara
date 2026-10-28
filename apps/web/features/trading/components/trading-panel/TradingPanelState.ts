import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingPanelState {
  readonly overview: TradingOverview | null;
  readonly error: string | null;
  readonly actionError: string | null;
  readonly validationError: string | null;
  readonly open: boolean;
  readonly busy: boolean;
  readonly refreshing: boolean;
  readonly dirty: boolean;
  readonly reviewOpen: boolean;
  readonly balanceReviewOpen: boolean;
  readonly expiresAt: string;
  readonly aiManaged: boolean;
}
