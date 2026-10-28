import { NextRequest, NextResponse } from 'next/server';
import { BoundedBodyReader } from '../../../../shared/api/BoundedBodyReader';
import { LocalRequestPolicy } from '../../../../shared/api/LocalRequestPolicy';

export class TradingJobsController {
  public static async get(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ path?: string[] }> },
  ): Promise<NextResponse> {
    const { path = [] } = await context.params;
    const statsRead =
      request.method === 'POST' && path.length === 1 && path[0] === 'stats';
    if (
      (request.method !== 'GET' && !statsRead) ||
      !LocalRequestPolicy.allows(request)
    ) {
      return NextResponse.json(
        { error: 'Local read-only access is required.' },
        { status: 403 },
      );
    }

    if (
      path.length > 12 ||
      path.some(
        (part) =>
          !/^[a-zA-Z0-9_.-]{1,120}$/.test(part) ||
          part === '.' ||
          part === '..',
      ) ||
      request.nextUrl.search.length > 2048
    ) {
      return NextResponse.json(
        { error: 'Invalid job dashboard path.' },
        { status: 400 },
      );
    }

    const token = process.env.QUANTARA_TRADING_CONTROL_TOKEN;
    if (!token?.trim()) {
      return NextResponse.json(
        { error: 'Job dashboard access is unavailable.' },
        { status: 503 },
      );
    }

    const signal = AbortSignal.any([
      request.signal,
      AbortSignal.timeout(15000),
    ]);
    let statisticsForm: string | undefined;
    if (statsRead) {
      if (
        request.headers.get('content-type')?.split(';')[0]?.trim() !==
        'application/x-www-form-urlencoded'
      ) {
        return NextResponse.json(
          { error: 'A statistics form is required.' },
          { status: 415 },
        );
      }

      try {
        statisticsForm = await TradingJobsController.statisticsBody(
          request,
          signal,
        );
      } catch (error) {
        if (signal.aborted) {
          return NextResponse.json(
            {
              error: request.signal.aborted
                ? 'Statistics request was cancelled.'
                : 'Statistics request timed out.',
            },
            { status: 408 },
          );
        }

        return NextResponse.json(
          { error: 'Invalid statistics form.' },
          { status: error instanceof RangeError ? 413 : 400 },
        );
      }
    }

    try {
      const base = new URL(
        process.env.PORTFOLIO_SERVICE_URL ?? 'http://127.0.0.1:5050',
      );
      if (
        !['http:', 'https:'].includes(base.protocol) ||
        base.username ||
        base.password
      ) {
        throw new Error('Invalid private service address.');
      }

      const suffix = path.length
        ? `/${path.map(encodeURIComponent).join('/')}`
        : '';
      const response = await fetch(
        `${base.origin}/api/trading/jobs${suffix}${request.nextUrl.search}`,
        {
          method: statsRead ? 'POST' : 'GET',
          cache: 'no-store',
          redirect: 'manual',
          headers: {
            'X-Quantara-Trading-Control': token,
            ...(statsRead
              ? { 'Content-Type': 'application/x-www-form-urlencoded' }
              : {}),
          },
          ...(statisticsForm === undefined ? {} : { body: statisticsForm }),
          signal,
        },
      );
      const location = response.headers.get('location');
      if (location) {
        if (!location.startsWith('/api/trading/jobs/')) {
          throw new Error('Unexpected job dashboard redirect.');
        }

        return new NextResponse(null, {
          status: response.status,
          headers: { Location: location, 'Cache-Control': 'no-store' },
        });
      }

      const body = response.body
        ? await BoundedBodyReader.read(response.body, signal, 2_000_000)
        : new Uint8Array(0);

      return new NextResponse(
        [204, 205, 304].includes(response.status) ? null : body,
        {
          status: response.status,
          headers: {
            'Content-Type':
              response.headers.get('content-type') ??
              'application/octet-stream',
            'Cache-Control': 'no-store',
            'X-Content-Type-Options': 'nosniff',
            'X-Frame-Options': 'DENY',
          },
        },
      );
    } catch {
      return NextResponse.json(
        { error: 'Job dashboard is unavailable.' },
        { status: 503 },
      );
    }
  }

  private static async statisticsBody(
    request: NextRequest,
    signal: AbortSignal,
  ): Promise<string> {
    const length = request.headers.get('content-length');
    if (length !== null && (!/^\d+$/.test(length) || Number(length) > 8192)) {
      throw new RangeError('Statistics form too large.');
    }

    const stream = request.body;
    if (!stream) {
      throw new Error('A statistics form is required.');
    }

    const bytes = await BoundedBodyReader.read(stream, signal, 8192);
    const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    const form = new URLSearchParams(text);
    const entries = [...form.entries()];
    if (
      entries.length > 64 ||
      entries.some(
        ([key, value]) =>
          key !== 'metrics[]' || !/^[a-zA-Z0-9:_.-]{1,100}$/.test(value),
      )
    ) {
      throw new Error('Only metric names are accepted.');
    }

    return form.toString();
  }
}
