import type { TradingOverview } from '../../contracts/TradingOverview';

export interface TradingSettingsProps {
  readonly overview: TradingOverview;
  readonly open: boolean;
  readonly busy: boolean;
  readonly refreshing: boolean;
  readonly dirty: boolean;
  readonly error: string | null;
  readonly validationError: string | null;
  readonly onClose: () => void;
  readonly onRetry: () => void;
  readonly onDirty: () => void;
  readonly onSave: (data: FormData) => void;
  readonly onPrepareAdaptiveSetup: () => void;
  readonly onSetMode: (mode: 'paper' | 'manualReview') => void;
}
