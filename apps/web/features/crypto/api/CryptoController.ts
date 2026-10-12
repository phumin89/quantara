import { NextRequest, NextResponse } from 'next/server';
import { CryptoProxy } from '../../../shared/api/CryptoProxy';

export class CryptoController {
  public static portfolio(this: void) {
    return CryptoProxy.request('portfolio');
  }

  public static history(this: void, request: NextRequest) {
    const coin = request.nextUrl.searchParams.get('coin');
    if (!coin || !/^[a-zA-Z0-9]{1,30}$/.test(coin)) {
      return NextResponse.json(
        { error: 'Invalid coin symbol.' },
        { status: 400 },
      );
    }

    return CryptoProxy.request(`history?coin=${encodeURIComponent(coin)}`);
  }

  public static sync(this: void, request: NextRequest) {
    const origin = request.headers.get('origin');
    if (origin && origin !== request.nextUrl.origin) {
      return NextResponse.json(
        { error: 'Cross-origin sync is not allowed.' },
        { status: 403 },
      );
    }

    return CryptoProxy.request('sync', 'POST');
  }
}
