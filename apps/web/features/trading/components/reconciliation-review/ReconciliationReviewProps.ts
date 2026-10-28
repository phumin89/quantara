import type { ReconciliationPreview } from '../../contracts/ReconciliationPreview';

export interface ReconciliationReviewProps {
  readonly preview: ReconciliationPreview;
  readonly acknowledged: boolean;
  readonly busy: boolean;
  readonly blocked: boolean;
  readonly expired: boolean;
  readonly uncertain: boolean;
  readonly onAcknowledge: (checked: boolean) => void;
  readonly onConfirm: () => void;
  readonly onDiscard: () => void;
}
