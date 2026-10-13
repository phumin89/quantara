import type { OrderView } from '../../contracts/OrderView';
import type { OrderSide } from '../../contracts/OrderSide';

export interface OrderReviewPanelState {
  proposals: OrderView[];
  loading: boolean;
  error: string | null;
  busyId: string | null;
  symbol: string;
  side: OrderSide;
  amount: string;
  limitPrice: string;
  reason: string;
  seconds: number;
  reviewedIds: string[];
}
