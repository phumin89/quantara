import { NextRequest, NextResponse } from 'next/server';

export class OrdersController {
  private static readonly serviceUrl =
    process.env.PORTFOLIO_SERVICE_URL ?? 'http://127.0.0.1:5050';

  private static readonly uuid =
    /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;

  private static readonly decimal = /^(?:0|[1-9]\d{0,17})(?:\.\d{1,18})?$/;

  public static get(this: void, request: NextRequest) {
    if (!OrdersController.allowed(request)) {
      return NextResponse.json(
        { error: 'Cross-origin order access is not allowed.' },
        { status: 403 },
      );
    }

    return OrdersController.proxy('', 'GET', request);
  }

  public static async create(this: void, request: NextRequest) {
    if (!OrdersController.allowed(request)) {
      return NextResponse.json(
        { error: 'Cross-origin order actions are not allowed.' },
        { status: 403 },
      );
    }

    try {
      const body = await OrdersController.body(request);
      if (!body || !OrdersController.validProposal(body)) {
        return NextResponse.json(
          { error: 'Invalid order proposal.' },
          { status: 400 },
        );
      }

      return await OrdersController.proxy('', 'POST', request, body);
    } catch {
      return NextResponse.json(
        { error: 'Invalid order proposal.' },
        { status: 400 },
      );
    }
  }

  public static approve(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ id: string }> },
  ) {
    return OrdersController.mutate(request, context, 'approve');
  }

  public static reject(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ id: string }> },
  ) {
    return OrdersController.mutate(request, context, 'reject');
  }

  public static reconcile(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ id: string }> },
  ) {
    return OrdersController.mutate(request, context, 'reconcile');
  }

  private static async mutate(
    request: NextRequest,
    context: { params: Promise<{ id: string }> },
    action: 'approve' | 'reject' | 'reconcile',
  ) {
    if (!OrdersController.allowed(request)) {
      return NextResponse.json(
        { error: 'Cross-origin order actions are not allowed.' },
        { status: 403 },
      );
    }

    const { id } = await context.params;
    if (!OrdersController.uuid.test(id)) {
      return NextResponse.json({ error: 'Invalid order ID.' }, { status: 400 });
    }

    try {
      const body = await OrdersController.body(request);
      if (action === 'approve' && (!body || body.confirmed !== true)) {
        return NextResponse.json(
          { error: 'Explicit confirmation is required.' },
          { status: 400 },
        );
      }

      return await OrdersController.proxy(
        `/${id}/${action}`,
        'POST',
        request,
        action === 'approve' ? { confirmed: true } : {},
      );
    } catch {
      return NextResponse.json(
        { error: 'Invalid order action.' },
        { status: 400 },
      );
    }
  }

  private static validProposal(body: Record<string, unknown>): boolean {
    const keys = Object.keys(body).sort().join(',');

    return (
      keys === 'amount,limitPrice,reason,side,symbol' &&
      typeof body.symbol === 'string' &&
      /^[A-Z0-9]{1,30}$/.test(body.symbol) &&
      (body.side === 'buy' || body.side === 'sell') &&
      typeof body.amount === 'string' &&
      OrdersController.decimal.test(body.amount) &&
      typeof body.limitPrice === 'string' &&
      OrdersController.decimal.test(body.limitPrice) &&
      typeof body.reason === 'string' &&
      body.reason.length <= 500 &&
      body.reason.trim().length > 0
    );
  }

  private static async body(
    request: NextRequest,
  ): Promise<Record<string, unknown> | null> {
    if (Number(request.headers.get('content-length') ?? 0) > 2000) {
      return null;
    }

    const text = await request.text();
    if (text.length > 2000) {
      return null;
    }

    const value: unknown = JSON.parse(text);

    return value && typeof value === 'object' && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : null;
  }

  private static allowed(request: NextRequest): boolean {
    const origin = request.headers.get('origin');
    const host = request.headers.get('host');

    return (
      host !== null &&
      /^(?:localhost|127\.0\.0\.1|\[::1\])(?::\d{1,5})?$/i.test(host) &&
      ['127.0.0.1', 'localhost', '[::1]', '::1'].includes(
        request.nextUrl.hostname,
      ) &&
      (!origin || origin === request.nextUrl.origin) &&
      request.headers.get('sec-fetch-site') !== 'cross-site'
    );
  }

  private static async proxy(
    path: string,
    method: 'GET' | 'POST',
    request: NextRequest,
    body?: unknown,
  ) {
    try {
      const response = await fetch(
        `${OrdersController.serviceUrl}/api/crypto/orders${path}`,
        {
          method,
          cache: 'no-store',
          signal: AbortSignal.any([request.signal, AbortSignal.timeout(30000)]),
          ...(body === undefined
            ? {}
            : {
                headers: {
                  'Content-Type': 'application/json',
                  'X-Quantara-Order-Action':
                    method === 'POST' ? 'review' : 'read',
                },
                body: JSON.stringify(body),
              }),
        },
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
          error:
            method === 'POST'
              ? 'Order result is unknown. Check Bitkub and reload; never resubmit automatically.'
              : 'The order service is unavailable. Please retry.',
        },
        { status: 503 },
      );
    }
  }
}
