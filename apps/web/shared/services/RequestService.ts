import { JsonResponse } from '../api/JsonResponse';
import type { IRequestService } from './IRequestService';

export class RequestService implements IRequestService {
  public constructor(private readonly transport: typeof fetch = fetch) {}

  public async get(
    url: string,
    fallback: string,
    signal?: AbortSignal,
  ): Promise<unknown> {
    const response = await this.send(url, 'GET', fallback, signal);

    return JsonResponse.read(response);
  }

  public async post(
    url: string,
    fallback: string,
    signal?: AbortSignal,
  ): Promise<void> {
    await this.send(url, 'POST', fallback, signal);
  }

  private async send(
    url: string,
    method: 'GET' | 'POST',
    fallback: string,
    signal?: AbortSignal,
  ): Promise<Response> {
    const response = await this.transport.call(globalThis, url, {
      method,
      cache: 'no-store',
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
