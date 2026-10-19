import { NextRequest, NextResponse } from 'next/server';
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
        statisticsForm = await TradingJobsController.statisticsBody(request);
      } catch (error) {
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
          signal: AbortSignal.any([request.signal, AbortSignal.timeout(15000)]),
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

      const reader = response.body?.getReader();
      const chunks: Uint8Array[] = [];
      let length = 0;
      try {
        if (reader) {
          for (;;) {
            const chunk = await reader.read();
            if (chunk.done) {
              break;
            }

            length += chunk.value.byteLength;
            if (length > 2_000_000) {
              void reader.cancel();
              throw new Error('Job dashboard response exceeds its limit.');
            }

            chunks.push(chunk.value);
          }
        }
      } finally {
        reader?.releaseLock();
      }

      const body = new Uint8Array(length);
      let offset = 0;
      for (const chunk of chunks) {
        body.set(chunk, offset);
        offset += chunk.byteLength;
      }

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

  private static async statisticsBody(request: NextRequest): Promise<string> {
    const length = request.headers.get('content-length');
    if (length !== null && (!/^\d+$/.test(length) || Number(length) > 8192)) {
      throw new RangeError('Statistics form too large.');
    }

    const reader = request.body?.getReader();
    if (!reader) {
      throw new Error('A statistics form is required.');
    }

    let size = 0;
    let text = '';
    const decoder = new TextDecoder('utf-8', { fatal: true });
    try {
      for (;;) {
        const chunk = await reader.read();
        if (chunk.done) {
          break;
        }

        size += chunk.value.byteLength;
        if (size > 8192) {
          void reader.cancel();
          throw new RangeError('Statistics form too large.');
        }

        text += decoder.decode(chunk.value, { stream: true });
      }

      text += decoder.decode();
    } finally {
      reader.releaseLock();
    }

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
