import type { IRequestService } from '../../../shared/services/IRequestService';
import { JsonResponse } from '../../../shared/api/JsonResponse';
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
      id,
    );
  }

  public async reject(id: string, signal?: AbortSignal): Promise<OrderView> {
    return this.send(
      `/${encodeURIComponent(id)}/reject`,
      {},
      'Unable to reject the order.',
      signal,
      id,
    );
  }

  private async send(
    path: string,
    body: unknown,
    fallback: string,
    signal?: AbortSignal,
    expectedId?: string,
  ): Promise<OrderView> {
    const response = await this.requests.send(
      `${OrdersService.basePath}${path}`,
      body,
      fallback,
      signal,
    );
    const parsed = orderViewSchema.safeParse(response);
    if (
      !parsed.success ||
      (expectedId !== undefined &&
        parsed.data.id.toLowerCase() !== expectedId.toLowerCase())
    ) {
      throw new Error(JsonResponse.uncertainWrite);
    }

    return parsed.data;
  }

  public async reconcile(id: string, signal?: AbortSignal): Promise<OrderView> {
    return this.send(
      `/${encodeURIComponent(id)}/reconcile`,
      {},
      'Unable to verify broker order status.',
      signal,
      id,
    );
  }
}
