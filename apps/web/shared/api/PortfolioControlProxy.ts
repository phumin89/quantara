import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { BoundedBodyReader } from './BoundedBodyReader';
import { LocalRequestPolicy } from './LocalRequestPolicy';
import { JsonResponse } from './JsonResponse';

export class PortfolioControlProxy {
  public static async forward(
    request: NextRequest,
    path: string,
    schema?: z.ZodType,
    timeoutMs: number = 30000,
  ): Promise<NextResponse> {
    if (!LocalRequestPolicy.allows(request)) {
      return NextResponse.json(
        { error: 'Local trading access is required.' },
        { status: 403 },
      );
    }

    const signal = AbortSignal.any([
      request.signal,
      AbortSignal.timeout(timeoutMs),
    ]);
    const init: RequestInit = {
      method: schema ? 'POST' : 'GET',
      cache: 'no-store',
      signal,
    };
    if (schema) {
      const token = process.env.QUANTARA_TRADING_CONTROL_TOKEN;
      if (!token?.trim()) {
        return NextResponse.json(
          { error: 'Trading control is unavailable.' },
          { status: 503 },
        );
      }

      if (
        !request.headers.get('content-type')?.startsWith('application/json')
      ) {
        return NextResponse.json(
          { error: 'JSON trading controls are required.' },
          { status: 415 },
        );
      }

      const length = request.headers.get('content-length');
      if (length !== null && (!/^\d+$/.test(length) || Number(length) > 8192)) {
        return NextResponse.json(
          { error: 'Request too large.' },
          { status: 413 },
        );
      }

      // Bound streamed bodies as well as the optional Content-Length header.
      const stream = request.body;
      if (!stream) {
        return NextResponse.json(
          { error: 'A JSON body is required.' },
          { status: 400 },
        );
      }

      try {
        const bytes = await BoundedBodyReader.read(stream, signal, 8192);
        const body: unknown = JSON.parse(
          new TextDecoder('utf-8', { fatal: true }).decode(bytes),
        );
        const parsed = schema.safeParse(body);
        if (!parsed.success) {
          return NextResponse.json(
            { error: 'Invalid trading control request.' },
            { status: 400 },
          );
        }

        init.headers = {
          'Content-Type': 'application/json',
          'X-Quantara-Trading-Control': token,
        };
        init.body = JSON.stringify(parsed.data);
      } catch (error) {
        if (error instanceof RangeError) {
          return NextResponse.json(
            { error: 'Request too large.' },
            { status: 413 },
          );
        }

        if (signal.aborted) {
          return NextResponse.json(
            {
              error: request.signal.aborted
                ? 'Trading control request was cancelled.'
                : 'Trading control request timed out.',
            },
            { status: 408 },
          );
        }

        return NextResponse.json(
          { error: 'Invalid JSON trading control request.' },
          { status: 400 },
        );
      }
    }

    try {
      const response = await fetch(
        `${(process.env.PORTFOLIO_SERVICE_URL ?? 'http://127.0.0.1:5050').replace(/\/$/, '')}${path}`,
        init,
      );

      return new NextResponse(await response.text(), {
        status: response.status,
        headers: {
          'Content-Type': 'application/json',
          'Cache-Control': 'no-store',
        },
      });
    } catch {
      return NextResponse.json(
        {
          error: schema
            ? JsonResponse.uncertainWrite
            : 'Trading service is unavailable. Refresh to try again.',
        },
        { status: 503 },
      );
    }
  }
}
