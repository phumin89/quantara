import { JsonResponse } from '../api/JsonResponse';
import type { IRequestService } from './IRequestService';

export class RequestService implements IRequestService {
  public constructor(
    private readonly baseUrl: string = '/api',
    private readonly transport: typeof fetch = fetch,
  ) {}

  public async get(
    path: string,
    fallback: string,
    signal?: AbortSignal,
  ): Promise<unknown> {
    const response = await this.request(path, 'GET', fallback, signal);

    return JsonResponse.read(response);
  }

  public async post(
    path: string,
    fallback: string,
    signal?: AbortSignal,
  ): Promise<void> {
    await this.request(path, 'POST', fallback, signal);
  }

  public async send(
    path: string,
    body: unknown,
    fallback: string,
    signal?: AbortSignal,
  ): Promise<unknown> {
    const response = await this.request(path, 'POST', fallback, signal, body);

    return JsonResponse.read(response);
  }

  private async request(
    path: string,
    method: 'GET' | 'POST',
    fallback: string,
    signal?: AbortSignal,
    body?: unknown,
  ): Promise<Response> {
    const target = `${this.baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
    const response = await this.transport.call(globalThis, target, {
      method,
      cache: 'no-store',
      ...(body !== undefined
        ? {
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
          }
        : {}),
      ...(signal ? { signal } : {}),
    });
    if (!response.ok) {
      throw new Error(
        JsonResponse.error(await JsonResponse.read(response), fallback),
      );
    }

    return response;
  }
}
