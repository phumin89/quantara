import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingControlsProps {
  readonly overview: TradingOverview;
  readonly liveOn: boolean;
  readonly busy: boolean;
  readonly dirty: boolean;
  readonly error: string | null;
  readonly balanceReviewNeeded?: boolean;
  readonly orderReviewNeeded?: boolean;
  readonly onReview: () => void;
  readonly onSettings: () => void;
  readonly onDisable: () => void;
  readonly onEmergencyStop: () => void;
}
