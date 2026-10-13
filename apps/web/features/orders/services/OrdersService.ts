import type { IRequestService } from '../../../shared/services/IRequestService';
import { orderResponseSchema } from '../contracts/OrderResponse';
import { orderViewSchema, type OrderView } from '../contracts/OrderView';
import type { IOrdersService } from './IOrdersService';
import type { OrderDraft } from '../contracts/OrderDraft';

export class OrdersService implements IOrdersService {
  private static readonly basePath = '/crypto/orders';

  public constructor(private readonly requests: IRequestService) {}

  public async list(signal?: AbortSignal) {
    const body = await this.requests.get(
      OrdersService.basePath,
      'Unable to load order proposals.',
      signal,
    );
    const parsed = orderResponseSchema.safeParse(body);
    if (!parsed.success) {
      throw new Error('Order proposals response is invalid.');
    }

    return parsed.data;
  }

  public async create(
    proposal: OrderDraft,
    signal?: AbortSignal,
  ): Promise<OrderView> {
    return this.send(
      '',
      proposal,
      'Unable to create the order proposal.',
      signal,
    );
  }

  public async approve(id: string, signal?: AbortSignal): Promise<OrderView> {
    return this.send(
      `/${encodeURIComponent(id)}/approve`,
      { confirmed: true },
      'Unable to approve the order.',
      signal,
    );
  }

  public async reject(id: string, signal?: AbortSignal): Promise<OrderView> {
    return this.send(
      `/${encodeURIComponent(id)}/reject`,
      {},
      'Unable to reject the order.',
      signal,
    );
  }

  private async send(
    path: string,
    body: unknown,
    fallback: string,
    signal?: AbortSignal,
  ): Promise<OrderView> {
    const response = await this.requests.send(
      `${OrdersService.basePath}${path}`,
      body,
      fallback,
      signal,
    );
    const parsed = orderViewSchema.safeParse(response);
    if (!parsed.success) {
      throw new Error('Order response is invalid.');
    }

    return parsed.data;
  }

  public async reconcile(id: string, signal?: AbortSignal): Promise<OrderView> {
    return this.send(
      `/${encodeURIComponent(id)}/reconcile`,
      {},
      'Unable to verify broker order status.',
      signal,
    );
  }
}
