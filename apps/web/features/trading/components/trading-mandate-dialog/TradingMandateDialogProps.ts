import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingMandateDialogProps {
  readonly overview: TradingOverview;
  readonly open: boolean;
  readonly expiresAt: string;
  readonly busy: boolean;
  readonly dirty: boolean;
  readonly error: string | null;
  readonly onClose: () => void;
  readonly onExpiresAtChange: (expiresAt: string) => void;
  readonly onActivate: () => void;
}
