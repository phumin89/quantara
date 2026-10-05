// Bound the entire read, including response.json(). Mutations are never retried here.
export async function requestJson(url: string, options: RequestInit = {}, fetcher: typeof fetch = fetch, timeoutMs = 8000): Promise<{ status: number; data: unknown }> {
  const controller = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let abort: () => void = () => {};
  const interrupted = new Promise<never>((_, reject) => {
    abort = () => { controller.abort(); reject(new Error("aborted")); };
    timer = setTimeout(() => { controller.abort(); reject(new Error("timeout")); }, timeoutMs);
    options.signal?.addEventListener("abort", abort, { once: true });
    if (options.signal?.aborted) abort();
  });
  try {
    return await Promise.race([
      interrupted,
      (async () => {
        if (controller.signal.aborted) throw new Error("aborted");
        const response = await fetcher(url, { ...options, signal: controller.signal, cache: "no-store" });
        return { status: response.status ?? (response.ok ? 200 : 500), data: response.ok && response.status !== 204 ? await response.json() : null };
      })(),
    ]);
  } finally {
    clearTimeout(timer);
    options.signal?.removeEventListener("abort", abort);
  }
}
