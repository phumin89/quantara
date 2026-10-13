import type { OrderView } from '../../contracts/OrderView';

export interface OrderReviewPanelState {
  proposals: OrderView[];
  loading: boolean;
  error: string | null;
  busyId: string | null;
  seconds: number;
}
