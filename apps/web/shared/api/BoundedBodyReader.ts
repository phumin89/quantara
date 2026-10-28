export class BoundedBodyReader {
  public static async readText(
    request: Request,
    maximumBytes: number,
    timeoutMs: number,
  ): Promise<string> {
    const declaredLength = request.headers.get('content-length');
    if (
      declaredLength !== null &&
      (declaredLength.length === 0 ||
        /[^0-9]/.test(declaredLength) ||
        Number(declaredLength) > maximumBytes)
    ) {
      throw new RangeError('Invalid request Content-Length.');
    }

    const stream = request.body;
    if (stream === null) {
      throw new TypeError('A request body is required.');
    }

    const signal = AbortSignal.any([
      request.signal,
      AbortSignal.timeout(timeoutMs),
    ]);
    const bytes = await BoundedBodyReader.read(stream, signal, maximumBytes);

    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  }

  public static async read(
    stream: ReadableStream<Uint8Array>,
    signal: AbortSignal,
    maximumBytes: number,
  ): Promise<Uint8Array<ArrayBuffer>> {
    const reader = stream.getReader();
    const cancel = BoundedBodyReader.cancel.bind(BoundedBodyReader, reader);
    signal.addEventListener('abort', cancel, { once: true });

    try {
      signal.throwIfAborted();
      const chunks: Uint8Array[] = [];
      let length = 0;
      let chunk = await reader.read();
      while (!chunk.done) {
        signal.throwIfAborted();
        length += chunk.value.byteLength;
        if (length > maximumBytes) {
          throw new RangeError('Response body exceeds its byte limit.');
        }

        chunks.push(chunk.value);
        chunk = await reader.read();
      }

      signal.throwIfAborted();
      const bytes = new Uint8Array(length);
      let offset = 0;
      for (const chunk of chunks) {
        bytes.set(chunk, offset);
        offset += chunk.byteLength;
      }

      return bytes;
    } finally {
      signal.removeEventListener('abort', cancel);
      cancel();
      reader.releaseLock();
    }
  }

  private static cancel(reader: ReadableStreamDefaultReader<Uint8Array>): void {
    // Closing pending reads must not wait for the source's cleanup promise.
    // Observe cleanup rejection without replacing the request's own failure.
    void reader.cancel().catch(() => undefined);
  }
}
