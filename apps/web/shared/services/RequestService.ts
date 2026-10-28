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

    return this.readBody(response, 'GET', fallback, signal);
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

    return this.readBody(response, 'POST', fallback, signal);
  }

  private async request(
    path: string,
    method: 'GET' | 'POST',
    fallback: string,
    signal?: AbortSignal,
    body?: unknown,
  ): Promise<Response> {
    const target = `${this.baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
    const options: RequestInit = {
      method,
      cache: 'no-store',
      ...(body !== undefined
        ? {
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
          }
        : {}),
      ...(signal ? { signal } : {}),
    };
    let response: Response;
    try {
      response = await this.transport.call(globalThis, target, options);
    } catch (error) {
      if (method === 'POST') {
        RequestService.writeFailure(error, signal);
      }

      throw error;
    }

    if (!response.ok) {
      throw new Error(
        JsonResponse.error(
          await this.readBody(response, method, fallback, signal),
          method === 'POST' ? JsonResponse.uncertainWrite : fallback,
        ),
      );
    }

    return response;
  }

  private async readBody(
    response: Response,
    method: 'GET' | 'POST',
    fallback: string,
    signal?: AbortSignal,
  ): Promise<unknown> {
    try {
      return await JsonResponse.read(
        response,
        method === 'POST' ? JsonResponse.uncertainWrite : fallback,
      );
    } catch (error) {
      if (method === 'POST') {
        RequestService.writeFailure(error, signal);
      }

      throw error;
    }
  }

  private static writeFailure(error: unknown, signal?: AbortSignal): never {
    if (
      signal?.aborted ||
      (error instanceof Error && error.name === 'AbortError')
    ) {
      throw error;
    }

    throw new Error(JsonResponse.uncertainWrite);
  }
}
