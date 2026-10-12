export interface IRequestService {
  get(path: string, fallback: string, signal?: AbortSignal): Promise<unknown>;

  post(path: string, fallback: string, signal?: AbortSignal): Promise<void>;
}
