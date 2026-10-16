import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { LocalRequestPolicy } from '../../../shared/api/LocalRequestPolicy';
import { tradingRulesSchema } from '../../../features/trading/contracts/TradingRules';

export class TradingController {
  private static readonly actions = {
    config: z.strictObject({ rules: tradingRulesSchema }),
    mode: z.strictObject({
      mode: z.enum(['paper', 'manualReview', 'stopped']),
    }),
    activate: z.strictObject({
      accountKey: z.string().min(1).max(128),
      rulesHash: z.string().min(1).max(128),
      expiresAt: z.iso
        .datetime({ offset: true })
        .refine(
          (date) => Date.parse(date) > Date.now(),
          'Mandate expiry must be in the future.',
        ),
      acknowledgement: z.literal('enable-auto-trading'),
    }),
    disable: z.strictObject({}),
    'emergency-stop': z.strictObject({}),
  };

  public static get(this: void, request: NextRequest): Promise<NextResponse> {
    return TradingController.proxy(request, '');
  }

  public static async action(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ action: string }> },
  ): Promise<NextResponse> {
    const { action } = await context.params;
    if (!Object.hasOwn(TradingController.actions, action)) {
      return NextResponse.json(
        { error: 'Unknown trading action.' },
        { status: 404 },
      );
    }

    return TradingController.proxy(
      request,
      `/${action}`,
      TradingController.actions[
        action as keyof typeof TradingController.actions
      ],
    );
  }

  private static async proxy(
    request: NextRequest,
    path: string,
    schema?: z.ZodType,
  ): Promise<NextResponse> {
    if (!LocalRequestPolicy.allows(request)) {
      return NextResponse.json(
        { error: 'Local trading access is required.' },
        { status: 403 },
      );
    }

    const init: RequestInit = {
      method: schema ? 'POST' : 'GET',
      cache: 'no-store',
      signal: AbortSignal.any([request.signal, AbortSignal.timeout(30000)]),
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
      const reader = request.body?.getReader();
      if (!reader) {
        return NextResponse.json(
          { error: 'A JSON body is required.' },
          { status: 400 },
        );
      }

      const chunks: Uint8Array[] = [];
      let size = 0;
      try {
        for (;;) {
          const chunk = await reader.read();
          if (chunk.done) {
            break;
          }

          size += chunk.value.byteLength;
          if (size > 8192) {
            void reader.cancel();

            return NextResponse.json(
              { error: 'Request too large.' },
              { status: 413 },
            );
          }

          chunks.push(chunk.value);
        }

        const bytes = new Uint8Array(size);
        let offset = 0;
        for (const chunk of chunks) {
          bytes.set(chunk, offset);
          offset += chunk.byteLength;
        }

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
      } catch {
        return NextResponse.json(
          { error: 'Invalid JSON trading control request.' },
          { status: 400 },
        );
      } finally {
        reader.releaseLock();
      }
    }

    try {
      const response = await fetch(
        `${(process.env.PORTFOLIO_SERVICE_URL ?? 'http://127.0.0.1:5050').replace(/\/$/, '')}/api/trading${path}`,
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
        { error: 'Trading service is unavailable.' },
        { status: 503 },
      );
    }
  }
}
