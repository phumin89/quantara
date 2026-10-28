import { NextResponse } from 'next/server';

export class CryptoProxy {
  private static readonly serviceUrl =
    process.env.PORTFOLIO_SERVICE_URL ?? 'http://127.0.0.1:5050';

  public static async request(path: string, method: 'GET' | 'POST' = 'GET') {
    try {
      const response = await fetch(
        `${CryptoProxy.serviceUrl}/api/crypto/${path}`,
        {
          method,
          cache: 'no-store',
          signal: AbortSignal.timeout(10000),
        },
      );
      const body = await response.text();

      return new NextResponse(body, {
        status: response.status,
        headers: {
          'Content-Type': 'application/json',
          'Cache-Control': 'no-store',
        },
      });
    } catch {
      return NextResponse.json(
        {
          error:
            method === 'POST'
              ? 'The import request may have started. Refresh status before trying again.'
              : 'The portfolio service is unavailable. Refresh to try again.',
        },
        { status: 503 },
      );
    }
  }
}
