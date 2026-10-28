import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingMandateDialogProps {
  readonly overview: TradingOverview;
  readonly open: boolean;
  readonly expiresAt: string;
  readonly aiManaged: boolean;
  readonly busy: boolean;
  readonly dirty: boolean;
  readonly error: string | null;
  readonly onClose: () => void;
  readonly onExpiresAtChange: (expiresAt: string) => void;
  readonly onAiManagedChange: (aiManaged: boolean) => void;
  readonly onActivate: () => void;
}
