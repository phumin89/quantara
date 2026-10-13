import type { OrderSide } from './OrderSide';

export interface OrderDraft {
  readonly symbol: string;
  readonly side: OrderSide;
  readonly amount: string;
  readonly limitPrice: string;
  readonly reason: string;
}
