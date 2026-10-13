import type { OrderResponse } from '../contracts/OrderResponse';
import type { OrderView } from '../contracts/OrderView';
import type { OrderDraft } from '../contracts/OrderDraft';

export interface IOrdersService {
  list(signal?: AbortSignal): Promise<OrderResponse>;
  create(proposal: OrderDraft, signal?: AbortSignal): Promise<OrderView>;
  approve(id: string, signal?: AbortSignal): Promise<OrderView>;
  reject(id: string, signal?: AbortSignal): Promise<OrderView>;
}
