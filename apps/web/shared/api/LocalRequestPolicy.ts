import type { NextRequest } from 'next/server';

export class LocalRequestPolicy {
  public static allows(request: NextRequest): boolean {
    const host = request.headers.get('host');
    if (
      host === null ||
      !/^(?:localhost|127\.0\.0\.1|\[::1\])(?::\d{1,5})?$/i.test(host)
    ) {
      return false;
    }

    const localUrl = ['127.0.0.1', 'localhost', '[::1]', '::1'].includes(
      request.nextUrl.hostname,
    );
    const containerUrl =
      process.env.QUANTARA_CONTAINER_RUNTIME === 'true' &&
      request.nextUrl.hostname === '0.0.0.0';
    if (!localUrl && !containerUrl) {
      return false;
    }

    try {
      const expectedOrigin = new URL(`${request.nextUrl.protocol}//${host}`)
        .origin;
      const origin = request.headers.get('origin');

      return (
        (!origin || origin === expectedOrigin) &&
        request.headers.get('sec-fetch-site') !== 'cross-site'
      );
    } catch {
      return false;
    }
  }
}
