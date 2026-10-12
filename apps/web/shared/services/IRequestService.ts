export interface IRequestService {
  get(url: string, fallback: string, signal?: AbortSignal): Promise<unknown>;

  post(url: string, fallback: string, signal?: AbortSignal): Promise<void>;
}
