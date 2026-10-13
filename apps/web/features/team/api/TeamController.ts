import { NextRequest, NextResponse } from 'next/server';

export class TeamController {
  private static readonly serviceUrl =
    process.env.TEAM_SERVICE_URL ?? 'http://127.0.0.1:5052';

  public static get(this: void, request: NextRequest) {
    if (!TeamController.allowed(request)) {
      return NextResponse.json(
        { error: 'Use the local application to access the team.' },
        { status: 403 },
      );
    }

    return TeamController.proxy('', 'GET', request);
  }

  public static brief(this: void, request: NextRequest) {
    if (!TeamController.allowed(request)) {
      return NextResponse.json(
        { error: 'Use the local application to access the team.' },
        { status: 403 },
      );
    }

    const date = request.nextUrl.searchParams.get('date');
    if (
      !date ||
      !/^\d{4}-\d{2}-\d{2}$/.test(date) ||
      Number.isNaN(Date.parse(`${date}T00:00:00Z`)) ||
      new Date(`${date}T00:00:00Z`).toISOString().slice(0, 10) !== date ||
      date < '1970-01-01' ||
      date > '9999-12-30'
    ) {
      return NextResponse.json(
        { error: 'Select a valid date.' },
        { status: 400 },
      );
    }

    return TeamController.proxy(
      `/brief?date=${encodeURIComponent(date)}`,
      'GET',
      request,
    );
  }

  public static async start(this: void, request: NextRequest) {
    if (!TeamController.allowed(request)) {
      return NextResponse.json(
        { error: 'Cross-origin tasks are not allowed.' },
        { status: 403 },
      );
    }

    try {
      if (Number(request.headers.get('content-length') ?? 0) > 12000) {
        return NextResponse.json(
          { error: 'Objective is too large.' },
          { status: 400 },
        );
      }

      const text = await request.text();
      if (text.length > 12000) {
        return NextResponse.json(
          { error: 'Objective is too large.' },
          { status: 400 },
        );
      }

      const body: unknown = JSON.parse(text);
      if (
        !body ||
        typeof body !== 'object' ||
        !('objective' in body) ||
        typeof body.objective !== 'string' ||
        !body.objective.trim() ||
        body.objective.length > 2000
      ) {
        return NextResponse.json(
          { error: 'Enter an objective of 1–2000 characters.' },
          { status: 400 },
        );
      }

      return await TeamController.proxy('/runs', 'POST', request, {
        objective: body.objective.trim(),
      });
    } catch {
      return NextResponse.json(
        { error: 'Invalid task request.' },
        { status: 400 },
      );
    }
  }

  public static stop(this: void, request: NextRequest) {
    if (!TeamController.allowed(request)) {
      return NextResponse.json(
        { error: 'Cross-origin tasks are not allowed.' },
        { status: 403 },
      );
    }

    const id = request.nextUrl.searchParams.get('id');
    if (!id || !/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(id)) {
      return NextResponse.json({ error: 'Invalid task ID.' }, { status: 400 });
    }

    return TeamController.proxy(`/runs/${id}/stop`, 'POST', request);
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
        `${TeamController.serviceUrl}/api/team${path}`,
        {
          method,
          cache: 'no-store',
          signal: AbortSignal.any([request.signal, AbortSignal.timeout(10000)]),
          ...(body === undefined
            ? {}
            : {
                headers: { 'Content-Type': 'application/json' },
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
          error: 'The team service is unavailable. Please retry.',
        },
        { status: 503 },
      );
    }
  }
}
