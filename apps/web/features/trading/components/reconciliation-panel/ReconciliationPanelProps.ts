import type { ICryptoServices } from '../../../crypto/context/ICryptoServices';
import type { ReconciliationPreview } from '../../contracts/ReconciliationPreview';

export interface ReconciliationPanelProps {
  readonly history: ICryptoServices['history'];
  readonly onApplied: (kind: ReconciliationPreview['kind']) => void;
}
